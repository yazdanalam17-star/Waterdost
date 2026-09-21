using Microsoft.EntityFrameworkCore;
using WaterApp.Domain.Enums;
using WaterApp.Infrastructure.Data;

namespace WaterApp.Infrastructure.Services;

// Why an order was cancelled. Drives what happens to its payment record.
public enum OrderCancelReason
{
    BuyerCancelled,
    SellerCancelled,
    PaymentRejected, // seller says the UPI payment never arrived
    PaymentExpired   // nobody confirmed the UPI payment in time (background job)
}

// Single place that knows which order status changes are legal and how a
// cancellation must affect stock and payment. Before this existed, the buyer
// cancel path restored stock but the seller "Cancel" button just flipped the
// status - so stock leaked - and a seller could jump an order straight from
// Placed to Delivered, or even back to PendingPayment.
//
// Every state change here is an *atomic claim*: the UPDATE only matches while
// the order is still in the status the caller saw (WHERE Status = @from).
// If a buyer cancel, a seller update and the expiry job all race on the same
// order, exactly one wins and the others get "false" back - so stock can
// never be restored twice, and a cancelled order can never be resurrected.
public static class OrderLifecycle
{
    // What a seller may do next, per current status. PendingPayment is
    // deliberately absent: those orders move only through ConfirmPayment
    // (-> Placed) or RejectPayment (-> Cancelled).
    private static readonly Dictionary<OrderStatus, OrderStatus[]> SellerTransitions = new()
    {
        [OrderStatus.Placed] = new[] { OrderStatus.Confirmed, OrderStatus.Cancelled },
        [OrderStatus.Confirmed] = new[] { OrderStatus.OutForDelivery, OrderStatus.Cancelled },
        // Cancel from OutForDelivery covers a failed delivery attempt.
        [OrderStatus.OutForDelivery] = new[] { OrderStatus.Delivered, OrderStatus.Cancelled }
    };

    public static bool CanSellerMove(OrderStatus from, OrderStatus to) =>
        SellerTransitions.TryGetValue(from, out var allowed) && allowed.Contains(to);

    // Buyers can cancel until the order is out for delivery.
    public static bool CanBuyerCancel(OrderStatus status) =>
        status is OrderStatus.PendingPayment or OrderStatus.Placed or OrderStatus.Confirmed;

    // Atomically cancels the order IF it is still in `expectedStatus`, puts
    // every line's quantity back into stock, and settles the payment record.
    // Returns false (and changes nothing) if the order was already moved by
    // someone else - callers should tell the user to refresh.
    //
    // The caller's tracked Order entity is stale afterwards (this uses
    // set-based UPDATEs, which bypass the change tracker); reload it before
    // mapping it to a DTO.
    public static async Task<bool> TryCancelAsync(
        AppDbContext db, Guid orderId, OrderStatus expectedStatus, OrderCancelReason reason)
    {
        await using var tx = await db.Database.BeginTransactionAsync();

        var claimed = await db.Orders
            .Where(o => o.Id == orderId && o.Status == expectedStatus)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Cancelled));

        if (claimed == 0)
        {
            await tx.RollbackAsync();
            return false;
        }

        // Restock with relative increments so we never overwrite a stock
        // figure that changed while this request was in flight (a new order,
        // or the seller editing stock).
        var lines = await db.OrderItems
            .Where(i => i.OrderId == orderId)
            .Select(i => new { i.ProductId, i.Quantity })
            .ToListAsync();

        foreach (var line in lines)
        {
            var qty = line.Quantity;
            var productId = line.ProductId;
            await db.Products
                .Where(p => p.Id == productId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQty, p => p.StockQty + qty));
        }

        // Money already confirmed as received -> mark refunded (the seller
        // still has to send it back; UPI pays the seller directly). A payment
        // that never arrived or timed out -> failed. Anything else (COD not
        // yet collected, or a buyer cancelling an unconfirmed UPI order) is
        // left as-is; revenue reports ignore cancelled orders regardless.
        var currentPaymentStatus = await db.Orders
            .Where(o => o.Id == orderId)
            .Select(o => o.PaymentStatus)
            .FirstAsync();

        PaymentStatus? settled = null;
        if (currentPaymentStatus == PaymentStatus.Success)
            settled = PaymentStatus.Refunded;
        else if (reason is OrderCancelReason.PaymentRejected or OrderCancelReason.PaymentExpired)
            settled = PaymentStatus.Failed;

        if (settled is PaymentStatus newPaymentStatus)
        {
            await db.Orders
                .Where(o => o.Id == orderId)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.PaymentStatus, newPaymentStatus));
            await db.Payments
                .Where(p => p.OrderId == orderId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, newPaymentStatus));
        }

        await tx.CommitAsync();
        return true;
    }

    // Atomically moves the order forward (Confirmed / OutForDelivery /
    // Delivered) IF it is still in `from`. Delivery stamps DeliveredAt and
    // marks a cash-on-delivery payment as collected. Cancellation goes
    // through TryCancelAsync instead so stock is always restored.
    public static async Task<bool> TryAdvanceAsync(
        AppDbContext db, Guid orderId, OrderStatus from, OrderStatus to)
    {
        if (to == OrderStatus.Cancelled)
            throw new ArgumentException("Use TryCancelAsync to cancel an order.", nameof(to));

        await using var tx = await db.Database.BeginTransactionAsync();

        var claimed = await db.Orders
            .Where(o => o.Id == orderId && o.Status == from)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, to));

        if (claimed == 0)
        {
            await tx.RollbackAsync();
            return false;
        }

        if (to == OrderStatus.Delivered)
        {
            DateTime? deliveredAt = DateTime.UtcNow;
            await db.Orders
                .Where(o => o.Id == orderId)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.DeliveredAt, deliveredAt));

            await db.Orders
                .Where(o => o.Id == orderId
                            && o.PaymentMode == PaymentMode.CashOnDelivery
                            && o.PaymentStatus == PaymentStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.PaymentStatus, PaymentStatus.CollectedInCash));

            await db.Payments
                .Where(p => p.OrderId == orderId
                            && p.Gateway == "COD"
                            && p.Status == PaymentStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PaymentStatus.CollectedInCash));
        }

        await tx.CommitAsync();
        return true;
    }
}

using Microsoft.EntityFrameworkCore;
using WaterApp.Application.Interfaces;
using WaterApp.Domain.Enums;
using WaterApp.Infrastructure.Data;
using WaterApp.Infrastructure.Services;

namespace WaterApp.API.Services;

// UPI orders start in PendingPayment and their stock is already reserved.
// If the seller never confirms (or rejects) the payment - a fake UTR, a
// seller who stopped using the app - that stock would stay locked forever.
// This job cancels such orders after Orders:PendingPaymentTimeoutMinutes
// (default 60) and puts the stock back.
//
// Safe to run on several instances at once: OrderLifecycle.TryCancelAsync is
// an atomic claim, so only one of them can ever cancel (and restock) a given
// order - the others just get "false" back and move on.
//
// NOTE: a buyer may genuinely have paid and simply not been confirmed yet, so
// both parties are told exactly what happened and that a refund may be due.
// The lasting fix for this whole class of problem is a payment gateway with
// webhooks, so payments are confirmed automatically - see PENDING.md.
public class PendingPaymentExpiryService : BackgroundService
{
    private const int DefaultTimeoutMinutes = 60;
    private const int MinTimeoutMinutes = 5;
    private const int BatchSize = 50;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<PendingPaymentExpiryService> _logger;

    public PendingPaymentExpiryService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<PendingPaymentExpiryService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the app finish starting (schema bootstrap etc.) before the first sweep.
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExpireStaleOrdersAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Never let one bad sweep kill the loop (or the host).
                _logger.LogError(ex, "Pending-payment expiry sweep failed.");
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ExpireStaleOrdersAsync(CancellationToken ct)
    {
        var configured = int.TryParse(_config["Orders:PendingPaymentTimeoutMinutes"], out var m) ? m : DefaultTimeoutMinutes;
        var timeoutMinutes = Math.Max(configured, MinTimeoutMinutes);
        var cutoff = DateTime.UtcNow.AddMinutes(-timeoutMinutes);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var stale = await db.Orders
            .Where(o => o.Status == OrderStatus.PendingPayment && o.CreatedAt < cutoff)
            .OrderBy(o => o.CreatedAt)
            .Take(BatchSize)
            .Select(o => new
            {
                o.Id,
                o.BuyerId,
                SellerUserId = o.Seller!.UserId,
                o.TotalAmount
            })
            .ToListAsync(ct);

        foreach (var order in stale)
        {
            ct.ThrowIfCancellationRequested();

            var cancelled = await OrderLifecycle.TryCancelAsync(
                db, order.Id, OrderStatus.PendingPayment, OrderCancelReason.PaymentExpired);

            if (!cancelled)
                continue; // confirmed, rejected or cancelled by someone else in the meantime

            _logger.LogInformation("Order {OrderId} expired while awaiting payment confirmation.", order.Id);

            await notifications.NotifyUserAsync(
                order.BuyerId,
                "Order cancelled",
                $"Your order for ₹{order.TotalAmount:F2} wasn't confirmed within {timeoutMinutes} minutes and was cancelled. " +
                "If you already paid, please contact the seller with your UPI reference for a refund.");

            await notifications.NotifyUserAsync(
                order.SellerUserId,
                "Order expired",
                $"An order for ₹{order.TotalAmount:F2} was cancelled because its payment wasn't confirmed in {timeoutMinutes} minutes. " +
                "If you did receive the payment, please refund the buyer.");
        }
    }
}

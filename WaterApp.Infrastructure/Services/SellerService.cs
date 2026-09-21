using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;
using WaterApp.Application.DTOs;
using WaterApp.Application.Interfaces;
using WaterApp.Domain.Entities;
using WaterApp.Domain.Enums;
using WaterApp.Infrastructure.Data;

namespace WaterApp.Infrastructure.Services;

public class SellerService : ISellerService
{
    private const long MaxImageBytes = 5 * 1024 * 1024; // 5 MB
    private static readonly HashSet<string> AllowedImageContentTypes =
        new(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png", "image/webp" };

    private readonly AppDbContext _db;
    private readonly INotificationService _notifications;
    private readonly IConfiguration _config;

    public SellerService(AppDbContext db, INotificationService notifications, IConfiguration config)
    {
        _db = db;
        _notifications = notifications;
        _config = config;
    }

    // ---- Profile / registration ----

    public async Task<SellerProfileDto?> GetMyProfileAsync(Guid userId)
    {
        var seller = await _db.Sellers
            .Include(s => s.ServiceAreas)
            .FirstOrDefaultAsync(s => s.UserId == userId);

        return seller is null ? null : MapProfile(seller);
    }

    public async Task<SellerProfileDto> RegisterAsync(Guid userId, SellerRegisterRequest request)
    {
        var alreadyRegistered = await _db.Sellers.AnyAsync(s => s.UserId == userId);
        if (alreadyRegistered)
            throw new InvalidOperationException("A seller profile already exists for this account.");

        if (string.IsNullOrWhiteSpace(request.CompanyName))
            throw new ArgumentException("Company name is required.");

        var seller = new Seller
        {
            UserId = userId,
            CompanyName = request.CompanyName.Trim(),
            Category = Enum.TryParse<SellerCategory>(request.Category, ignoreCase: true, out var cat) ? cat : SellerCategory.Water,
            BaseLatitude = request.BaseLatitude,
            BaseLongitude = request.BaseLongitude,
            Status = SellerStatus.Pending,
            ServiceAreas = (request.ServicePincodes ?? new List<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => new ServiceArea { Pincode = p.Trim() })
                .ToList()
        };

        _db.Sellers.Add(seller);
        await _db.SaveChangesAsync();

        return MapProfile(seller);
    }

    public async Task<SellerProfileDto> UpdatePaymentSettingsAsync(Guid userId, string? upiId)
    {
        var seller = await _db.Sellers
            .Include(s => s.ServiceAreas)
            .FirstOrDefaultAsync(s => s.UserId == userId)
            ?? throw new KeyNotFoundException("Seller profile not found.");

        var trimmed = upiId?.Trim();
        if (!string.IsNullOrEmpty(trimmed))
        {
            // Basic VPA shape check: name@handle, no spaces. Not a payment
            // guarantee — just prevents obviously malformed IDs.
            if (!Regex.IsMatch(trimmed, @"^[a-zA-Z0-9.\-_]{2,256}@[a-zA-Z]{2,64}$"))
                throw new ArgumentException("Enter a valid UPI ID, e.g. name@bank.");
            seller.UpiId = trimmed;
        }
        else
        {
            seller.UpiId = null; // clearing it disables online payment for this seller
        }

        await _db.SaveChangesAsync();
        return MapProfile(seller);
    }

    // ---- Products ----

    public async Task<List<ProductDto>> GetMyProductsAsync(Guid userId)
    {
        var seller = await GetOwnedSellerAsync(userId);

        var products = await _db.Products
            .Where(p => p.SellerId == seller.Id)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        // A lightweight existence check (product IDs only, never the image
        // bytes) so listing a whole catalog doesn't drag every photo along.
        var productIds = products.Select(p => p.Id).ToList();
        var withImage = (await _db.ProductImages
            .Where(pi => productIds.Contains(pi.ProductId))
            .Select(pi => pi.ProductId)
            .ToListAsync())
            .ToHashSet();

        return products.Select(p => MapProduct(p, withImage.Contains(p.Id))).ToList();
    }

    public async Task<ProductDto> CreateProductAsync(Guid userId, ProductCreateRequest request)
    {
        var seller = await GetOwnedSellerAsync(userId);
        ValidateProductFields(request.Name, request.VolumeLabel, request.Price, request.StockQty);

        var product = new Product
        {
            SellerId = seller.Id,
            Name = request.Name.Trim(),
            Category = Enum.TryParse<SellerCategory>(request.Category, ignoreCase: true, out var pcat) ? pcat : SellerCategory.Water,
            VolumeLabel = request.VolumeLabel.Trim(),
            Price = request.Price,
            StockQty = request.StockQty,
            IsActive = true
        };

        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        return MapProduct(product, hasImage: false);
    }

    public async Task<ProductDto> UpdateProductAsync(Guid userId, Guid productId, ProductUpdateRequest request)
    {
        var seller = await GetOwnedSellerAsync(userId);
        ValidateProductFields(request.Name, request.VolumeLabel, request.Price, request.StockQty);

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId && p.SellerId == seller.Id)
            ?? throw new KeyNotFoundException("Product not found.");

        product.Name = request.Name.Trim();
        if (Enum.TryParse<SellerCategory>(request.Category, ignoreCase: true, out var pcat))
            product.Category = pcat;
        product.VolumeLabel = request.VolumeLabel.Trim();
        product.Price = request.Price;
        product.StockQty = request.StockQty;
        product.IsActive = request.IsActive;

        await _db.SaveChangesAsync();

        var hasImage = await _db.ProductImages.AnyAsync(pi => pi.ProductId == product.Id);
        return MapProduct(product, hasImage);
    }

    public async Task DeleteProductAsync(Guid userId, Guid productId)
    {
        var seller = await GetOwnedSellerAsync(userId);

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId && p.SellerId == seller.Id)
            ?? throw new KeyNotFoundException("Product not found.");

        var hasOrderHistory = await _db.OrderItems.AnyAsync(oi => oi.ProductId == productId);
        if (hasOrderHistory)
        {
            // Preserve order history integrity — soft delete instead of a hard delete.
            product.IsActive = false;
            product.StockQty = 0;
            await _db.SaveChangesAsync();
            return;
        }

        _db.Products.Remove(product);
        await _db.SaveChangesAsync();
    }

    // ---- Product image ----

    public async Task<ProductDto> SetProductImageAsync(Guid userId, Guid productId, Stream? imageStream, string? contentType, long length)
    {
        var seller = await GetOwnedSellerAsync(userId);
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId && p.SellerId == seller.Id)
            ?? throw new KeyNotFoundException("Product not found.");

        if (imageStream is null || length == 0)
            throw new ArgumentException("No image file was provided.");
        if (length > MaxImageBytes)
            throw new ArgumentException("Image must be smaller than 5 MB.");
        if (contentType is null || !AllowedImageContentTypes.Contains(contentType))
            throw new ArgumentException("Image must be a JPEG, PNG, or WEBP file.");

        using var buffer = new MemoryStream();
        await imageStream.CopyToAsync(buffer);
        var bytes = buffer.ToArray();

        var existing = await _db.ProductImages.FirstOrDefaultAsync(pi => pi.ProductId == productId);
        if (existing is not null)
        {
            existing.Data = bytes;
            existing.ContentType = contentType;
            existing.CreatedAt = DateTime.UtcNow;
        }
        else
        {
            _db.ProductImages.Add(new ProductImage
            {
                ProductId = productId,
                Data = bytes,
                ContentType = contentType
            });
        }

        await _db.SaveChangesAsync();
        return MapProduct(product, hasImage: true);
    }

    public async Task<ProductDto> RemoveProductImageAsync(Guid userId, Guid productId)
    {
        var seller = await GetOwnedSellerAsync(userId);
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId && p.SellerId == seller.Id)
            ?? throw new KeyNotFoundException("Product not found.");

        var existing = await _db.ProductImages.FirstOrDefaultAsync(pi => pi.ProductId == productId);
        if (existing is not null)
            _db.ProductImages.Remove(existing);

        product.ImageUrl = null;

        await _db.SaveChangesAsync();
        return MapProduct(product, hasImage: false);
    }

    // ---- Orders ----

    public async Task<List<SellerOrderDto>> GetMyOrdersAsync(Guid userId, string? status, int page = 1, int pageSize = 50)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 100 ? 50 : pageSize;

        var seller = await GetOwnedSellerAsync(userId);

        var query = _db.Orders
            .Include(o => o.Buyer)
            .Include(o => o.Address)
            .Include(o => o.Payment)
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .Where(o => o.SellerId == seller.Id)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<OrderStatus>(status, true, out var parsedStatus))
                throw new ArgumentException($"Unknown order status '{status}'.");
            query = query.Where(o => o.Status == parsedStatus);
        }

        var orders = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        return orders.Select(MapOrder).ToList();
    }

    public async Task<SellerOrderDto> UpdateOrderStatusAsync(Guid userId, Guid orderId, string status)
    {
        var seller = await GetOwnedSellerAsync(userId);

        if (!Enum.TryParse<OrderStatus>(status, true, out var parsedStatus))
            throw new ArgumentException($"Unknown order status '{status}'.");

        var order = await LoadOrderForSellerAsync(seller.Id, orderId);
        var from = order.Status;

        if (from is OrderStatus.Delivered or OrderStatus.Cancelled)
            throw new ArgumentException($"An order that is already {from} cannot be changed.");
        if (from == OrderStatus.PendingPayment)
            throw new ArgumentException("Confirm the payment (or mark it as not received) before updating this order's status.");
        if (!OrderLifecycle.CanSellerMove(from, parsedStatus))
            throw new ArgumentException($"An order that is {from} cannot be moved to {parsedStatus}.");

        // Both paths are atomic "claims" (see OrderLifecycle): if the buyer
        // cancelled - or anything else touched the order - between the load
        // above and the write below, nothing is changed and the seller is
        // asked to refresh instead of silently overwriting the new state.
        bool applied = parsedStatus == OrderStatus.Cancelled
            // Seller cancel now restores stock and settles payment, exactly
            // like a buyer cancel. Previously it only flipped the status, so
            // the cancelled quantity was lost from stock permanently.
            ? await OrderLifecycle.TryCancelAsync(_db, order.Id, from, OrderCancelReason.SellerCancelled)
            : await OrderLifecycle.TryAdvanceAsync(_db, order.Id, from, parsedStatus);

        if (!applied)
            throw new InvalidOperationException("This order was just updated by someone else. Please refresh and try again.");

        await _db.Entry(order).ReloadAsync();

        var message = parsedStatus == OrderStatus.Cancelled && order.PaymentMode == PaymentMode.Online && order.PaymentStatus == PaymentStatus.Refunded
            ? "The seller cancelled your order. Your payment will be refunded by the seller - contact them if it doesn't arrive."
            : parsedStatus == OrderStatus.Cancelled
                ? "The seller cancelled your order."
                : StatusChangeMessage(parsedStatus);

        await _notifications.NotifyUserAsync(order.BuyerId, "Order update", message);

        return MapOrder(order);
    }

    public async Task<SellerOrderDto> ConfirmPaymentAsync(Guid userId, Guid orderId)
    {
        var seller = await GetOwnedSellerAsync(userId);
        var order = await LoadOrderForSellerAsync(seller.Id, orderId);

        if (order.PaymentMode != PaymentMode.Online)
            throw new ArgumentException("Only online (UPI) payments need manual confirmation.");

        // A cancelled/expired order must never be "paid" again - that used to
        // be possible and made cancelled orders count as revenue. If real
        // money did arrive, the seller has to refund it outside the app.
        if (order.Status == OrderStatus.Cancelled)
            throw new ArgumentException("This order was cancelled, so its payment can't be confirmed. If you did receive the money, please refund the buyer directly.");

        if (order.PaymentStatus == PaymentStatus.Success)
            throw new ArgumentException("This payment is already confirmed.");

        var wasPendingPayment = order.Status == OrderStatus.PendingPayment;
        var fromStatus = order.Status;
        var newStatus = wasPendingPayment ? OrderStatus.Placed : fromStatus;
        DateTime? paidAt = DateTime.UtcNow;

        // Atomic claim on the order's current status, so this can't race with
        // the buyer cancelling or the auto-expiry job cancelling it.
        await using (var tx = await _db.Database.BeginTransactionAsync())
        {
            var claimed = await _db.Orders
                .Where(o => o.Id == order.Id && o.Status == fromStatus && o.PaymentStatus != PaymentStatus.Success)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.PaymentStatus, PaymentStatus.Success)
                    .SetProperty(o => o.Status, newStatus));

            if (claimed == 0)
            {
                await tx.RollbackAsync();
                throw new InvalidOperationException("This order was just updated by someone else. Please refresh and try again.");
            }

            await _db.Payments
                .Where(p => p.OrderId == order.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Status, PaymentStatus.Success)
                    .SetProperty(p => p.PaidAt, paidAt));

            await tx.CommitAsync();
        }

        await _db.Entry(order).ReloadAsync();

        await _notifications.NotifyUserAsync(
            order.BuyerId,
            "Payment confirmed",
            wasPendingPayment
                ? $"Your payment of ₹{order.TotalAmount:F2} was confirmed and your order is now placed."
                : $"Your payment of ₹{order.TotalAmount:F2} was confirmed."
        );

        return MapOrder(order);
    }

    // The seller checked their UPI/bank account and the money isn't there.
    // Cancels the order, releases its stock and marks the payment failed.
    // Before this existed a fake or mistyped UTR left the order stuck in
    // PendingPayment - holding stock - with no way for the seller to clear it.
    public async Task<SellerOrderDto> RejectPaymentAsync(Guid userId, Guid orderId)
    {
        var seller = await GetOwnedSellerAsync(userId);
        var order = await LoadOrderForSellerAsync(seller.Id, orderId);

        if (order.PaymentMode != PaymentMode.Online)
            throw new ArgumentException("Only online (UPI) orders have a payment that can be rejected.");
        if (order.Status != OrderStatus.PendingPayment)
            throw new ArgumentException("Only orders that are still awaiting payment can be rejected. To stop a confirmed order, cancel it instead.");

        var cancelled = await OrderLifecycle.TryCancelAsync(
            _db, order.Id, OrderStatus.PendingPayment, OrderCancelReason.PaymentRejected);

        if (!cancelled)
            throw new InvalidOperationException("This order was just updated by someone else. Please refresh and try again.");

        await _db.Entry(order).ReloadAsync();

        await _notifications.NotifyUserAsync(
            order.BuyerId,
            "Payment not received",
            $"The seller couldn't find your payment of ₹{order.TotalAmount:F2}, so the order was cancelled. If money was deducted from your account, share your UPI reference with the seller."
        );

        return MapOrder(order);
    }

    // ---- Dashboard ----

    public async Task<SellerDashboardStatsDto> GetDashboardStatsAsync(Guid userId)
    {
        var seller = await GetOwnedSellerAsync(userId);
        var todayUtc = DateTime.UtcNow.Date;

        var totalProducts = await _db.Products.CountAsync(p => p.SellerId == seller.Id);
        var activeProducts = await _db.Products.CountAsync(p => p.SellerId == seller.Id && p.IsActive);
        var lowStockProducts = await _db.Products.CountAsync(p => p.SellerId == seller.Id && p.IsActive && p.StockQty <= 5);

        var totalOrders = await _db.Orders.CountAsync(o => o.SellerId == seller.Id && o.Status != OrderStatus.PendingPayment);
        var pendingOrders = await _db.Orders.CountAsync(o =>
            o.SellerId == seller.Id &&
            (o.Status == OrderStatus.Placed || o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.OutForDelivery));
        var todayOrders = await _db.Orders.CountAsync(o => o.SellerId == seller.Id && o.CreatedAt >= todayUtc);

        // Cancelled orders never count as revenue, whatever their payment
        // status says (a paid-then-cancelled order is Refunded now, but this
        // also protects any older rows that were left as Success).
        var totalRevenue = await _db.Orders
            .Where(o => o.SellerId == seller.Id && o.Status != OrderStatus.Cancelled &&
                (o.PaymentStatus == PaymentStatus.Success || o.PaymentStatus == PaymentStatus.CollectedInCash))
            .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

        var todayRevenue = await _db.Orders
            .Where(o => o.SellerId == seller.Id && o.CreatedAt >= todayUtc && o.Status != OrderStatus.Cancelled &&
                (o.PaymentStatus == PaymentStatus.Success || o.PaymentStatus == PaymentStatus.CollectedInCash))
            .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

        return new SellerDashboardStatsDto(
            totalProducts,
            activeProducts,
            lowStockProducts,
            pendingOrders,
            totalOrders,
            todayOrders,
            totalRevenue,
            todayRevenue
        );
    }

    // ---- helpers ----

    private async Task<Order> LoadOrderForSellerAsync(Guid sellerId, Guid orderId)
    {
        return await _db.Orders
            .Include(o => o.Buyer)
            .Include(o => o.Address)
            .Include(o => o.Payment)
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.SellerId == sellerId)
            ?? throw new KeyNotFoundException("Order not found.");
    }

    private async Task<Seller> GetOwnedSellerAsync(Guid userId)
    {
        return await _db.Sellers.FirstOrDefaultAsync(s => s.UserId == userId)
            ?? throw new KeyNotFoundException("Seller profile not found. Please register as a seller first.");
    }

    private static string StatusChangeMessage(OrderStatus status) => status switch
    {
        OrderStatus.Confirmed => "Your order has been confirmed by the seller.",
        OrderStatus.OutForDelivery => "Your order is out for delivery.",
        OrderStatus.Delivered => "Your order has been delivered. Enjoy!",
        _ => $"Your order status is now {status}."
    };

    private static void ValidateProductFields(string name, string volumeLabel, decimal price, int stockQty)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Product name is required.");
        if (string.IsNullOrWhiteSpace(volumeLabel))
            throw new ArgumentException("Volume label is required.");
        if (price <= 0)
            throw new ArgumentException("Price must be greater than zero.");
        if (stockQty < 0)
            throw new ArgumentException("Stock quantity cannot be negative.");
    }

    private static SellerProfileDto MapProfile(Seller seller) => new(
        seller.Id,
        seller.CompanyName,
        seller.Status.ToString(),
        seller.LogoUrl,
        seller.UpiId,
        seller.BaseLatitude,
        seller.BaseLongitude,
        seller.ServiceAreas.Select(sa => sa.Pincode).ToList(),
        seller.CreatedAt
    );

    private ProductDto MapProduct(Product p, bool hasImage) => new(
        p.Id, p.SellerId, p.Name, p.Category.ToString(), p.VolumeLabel, p.Price, p.StockQty, p.IsActive, BuildImageUrl(p, hasImage)
    );

    // Computed fresh at read time from whether a ProductImage row exists,
    // rather than stored, so changing App:PublicBaseUrl (e.g. a domain
    // migration) doesn't leave old rows pointing at a dead host.
    private string? BuildImageUrl(Product p, bool hasImage)
    {
        if (hasImage)
        {
            var baseUrl = _config["App:PublicBaseUrl"]?.TrimEnd('/') ?? "";
            return $"{baseUrl}/api/products/{p.Id}/image";
        }
        return p.ImageUrl;
    }

    private static SellerOrderDto MapOrder(Order o) => new(
        o.Id,
        o.Buyer?.Name ?? "",
        o.Buyer?.Phone ?? "",
        o.Status.ToString(),
        o.PaymentMode.ToString(),
        o.PaymentStatus.ToString(),
        o.Payment?.TransactionId,
        o.TotalAmount,
        o.CreatedAt,
        o.DeliveredAt,
        o.Address is null ? null : $"{o.Address.Line1}, {o.Address.City}, {o.Address.State} {o.Address.Pincode}",
        o.Items.Select(i => new SellerOrderItemDto(
            i.ProductId,
            i.Product?.Name ?? "",
            i.Product?.VolumeLabel ?? "",
            i.Quantity,
            i.PriceAtPurchase
        )).ToList()
    );
}

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

        var order = await _db.Orders
            .Include(o => o.Buyer)
            .Include(o => o.Address)
            .Include(o => o.Payment)
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.SellerId == seller.Id)
            ?? throw new KeyNotFoundException("Order not found.");

        if (order.Status is OrderStatus.Delivered or OrderStatus.Cancelled)
            throw new ArgumentException($"An order that is already {order.Status} cannot be changed.");
        if (order.Status == OrderStatus.PendingPayment)
            throw new ArgumentException("Confirm the payment before updating this order's status.");
        if (parsedStatus == OrderStatus.Placed)
            throw new ArgumentException("Cannot move an order back to Placed.");

        order.Status = parsedStatus;
        if (parsedStatus == OrderStatus.Delivered)
        {
            order.DeliveredAt = DateTime.UtcNow;
            if (order.PaymentMode == PaymentMode.CashOnDelivery && order.PaymentStatus == PaymentStatus.Pending)
                order.PaymentStatus = PaymentStatus.CollectedInCash;
        }

        await _db.SaveChangesAsync();

        await _notifications.NotifyUserAsync(
            order.BuyerId,
            "Order update",
            StatusChangeMessage(parsedStatus)
        );

        return MapOrder(order);
    }

    public async Task<SellerOrderDto> ConfirmPaymentAsync(Guid userId, Guid orderId)
    {
        var seller = await GetOwnedSellerAsync(userId);

        var order = await _db.Orders
            .Include(o => o.Buyer)
            .Include(o => o.Address)
            .Include(o => o.Payment)
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.SellerId == seller.Id)
            ?? throw new KeyNotFoundException("Order not found.");

        if (order.PaymentMode != PaymentMode.Online)
            throw new ArgumentException("Only online (UPI) payments need manual confirmation.");
        if (order.PaymentStatus == PaymentStatus.Success)
            throw new ArgumentException("This payment is already confirmed.");

        order.PaymentStatus = PaymentStatus.Success;
        if (order.Payment is not null)
        {
            order.Payment.Status = PaymentStatus.Success;
            order.Payment.PaidAt = DateTime.UtcNow;
        }

        // Confirming payment activates a pending online order into the queue.
        var wasPendingPayment = order.Status == OrderStatus.PendingPayment;
        if (wasPendingPayment)
            order.Status = OrderStatus.Placed;

        await _db.SaveChangesAsync();

        await _notifications.NotifyUserAsync(
            order.BuyerId,
            "Payment confirmed",
            wasPendingPayment
                ? $"Your payment of ₹{order.TotalAmount:F2} was confirmed and your order is now placed."
                : $"Your payment of ₹{order.TotalAmount:F2} was confirmed."
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

        var totalRevenue = await _db.Orders
            .Where(o => o.SellerId == seller.Id &&
                (o.PaymentStatus == PaymentStatus.Success || o.PaymentStatus == PaymentStatus.CollectedInCash))
            .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

        var todayRevenue = await _db.Orders
            .Where(o => o.SellerId == seller.Id && o.CreatedAt >= todayUtc &&
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

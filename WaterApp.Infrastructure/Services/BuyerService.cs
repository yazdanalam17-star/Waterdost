using Microsoft.EntityFrameworkCore;
using WaterApp.Application.DTOs;
using WaterApp.Application.Interfaces;
using WaterApp.Domain.Entities;
using WaterApp.Domain.Enums;
using WaterApp.Infrastructure.Data;

namespace WaterApp.Infrastructure.Services;

public class BuyerService : IBuyerService
{
    private readonly AppDbContext _db;

    public BuyerService(AppDbContext db)
    {
        _db = db;
    }

    // ==================== Catalog browsing ====================

    public async Task<List<SellerDto>> GetSellersInAreaAsync(string pincode)
    {
        if (string.IsNullOrWhiteSpace(pincode))
            throw new ArgumentException("Pincode is required.");

        var trimmedPincode = pincode.Trim();

        var sellers = await _db.Sellers
            .Where(s => s.Status == SellerStatus.Approved &&
                        s.ServiceAreas.Any(sa => sa.Pincode == trimmedPincode))
            .OrderBy(s => s.CompanyName)
            .ToListAsync();

        return sellers.Select(s => new SellerDto(s.Id, s.CompanyName, s.Status.ToString(), s.LogoUrl, s.UpiId)).ToList();
    }

    public async Task<List<ProductDto>> GetSellerProductsAsync(Guid sellerId)
    {
        var seller = await _db.Sellers.FirstOrDefaultAsync(s => s.Id == sellerId)
            ?? throw new KeyNotFoundException("Seller not found.");

        if (seller.Status != SellerStatus.Approved)
            throw new KeyNotFoundException("Seller not found.");

        var products = await _db.Products
            .Where(p => p.SellerId == sellerId && p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync();

        return products.Select(MapProduct).ToList();
    }

    // ==================== Addresses ====================

    public async Task<List<AddressDto>> GetMyAddressesAsync(Guid userId)
    {
        var addresses = await _db.Addresses
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.IsDefault)
            .ToListAsync();

        return addresses.Select(MapAddress).ToList();
    }

    public async Task<AddressDto> AddAddressAsync(Guid userId, AddressCreateRequest request)
    {
        ValidateAddressFields(request.Line1, request.City, request.State, request.Pincode);

        var isFirstAddress = !await _db.Addresses.AnyAsync(a => a.UserId == userId);

        if ((request.IsDefault || isFirstAddress))
        {
            await UnsetExistingDefaultsAsync(userId);
        }

        var address = new Address
        {
            UserId = userId,
            Line1 = request.Line1.Trim(),
            Line2 = request.Line2?.Trim(),
            City = request.City.Trim(),
            State = request.State.Trim(),
            Pincode = request.Pincode.Trim(),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            IsDefault = request.IsDefault || isFirstAddress
        };

        _db.Addresses.Add(address);
        await _db.SaveChangesAsync();

        return MapAddress(address);
    }

    public async Task<AddressDto> UpdateAddressAsync(Guid userId, Guid addressId, AddressUpdateRequest request)
    {
        ValidateAddressFields(request.Line1, request.City, request.State, request.Pincode);

        var address = await GetOwnedAddressAsync(userId, addressId);

        if (request.IsDefault && !address.IsDefault)
        {
            await UnsetExistingDefaultsAsync(userId);
        }

        address.Line1 = request.Line1.Trim();
        address.Line2 = request.Line2?.Trim();
        address.City = request.City.Trim();
        address.State = request.State.Trim();
        address.Pincode = request.Pincode.Trim();
        address.Latitude = request.Latitude;
        address.Longitude = request.Longitude;
        address.IsDefault = request.IsDefault;

        await _db.SaveChangesAsync();
        return MapAddress(address);
    }

    public async Task DeleteAddressAsync(Guid userId, Guid addressId)
    {
        var address = await GetOwnedAddressAsync(userId, addressId);

        var usedInOrders = await _db.Orders.AnyAsync(o => o.AddressId == addressId);
        if (usedInOrders)
            throw new InvalidOperationException("This address is linked to past orders and cannot be deleted.");

        _db.Addresses.Remove(address);
        await _db.SaveChangesAsync();

        if (address.IsDefault)
        {
            var nextDefault = await _db.Addresses.Where(a => a.UserId == userId).FirstOrDefaultAsync();
            if (nextDefault is not null)
            {
                nextDefault.IsDefault = true;
                await _db.SaveChangesAsync();
            }
        }
    }

    public async Task<AddressDto> SetDefaultAddressAsync(Guid userId, Guid addressId)
    {
        var address = await GetOwnedAddressAsync(userId, addressId);

        await UnsetExistingDefaultsAsync(userId);
        address.IsDefault = true;

        await _db.SaveChangesAsync();
        return MapAddress(address);
    }

    // ==================== Cart ====================

    public async Task<CartDto> GetCartAsync(Guid userId)
    {
        var cart = await GetOrCreateCartAsync(userId);
        return MapCart(cart);
    }

    // NOTE: This method requires a unique index/constraint on
    // CartItems ("CartId", "ProductId") in the database. See the
    // accompanying migration SQL. Without it, ON CONFLICT below will
    // fail with "there is no unique or exclusion constraint matching
    // the ON CONFLICT specification".
    public async Task<CartDto> AddToCartAsync(Guid userId, AddToCartRequest request)
    {
        if (request.Quantity <= 0)
            throw new ArgumentException("Quantity must be greater than zero.");

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId)
            ?? throw new KeyNotFoundException("Product not found.");

        if (!product.IsActive)
            throw new ArgumentException("This product is currently unavailable.");

        var cart = await GetOrCreateCartAsync(userId);

        // Atomic upsert: instead of "read quantity in memory, then write it
        // back", let Postgres do the read-modify-write in a single statement.
        // This makes concurrent "Add to Cart" taps (double-clicks, multiple
        // tabs, etc.) impossible to race against each other, and it removes
        // the need for the previous DbUpdateConcurrencyException retry loop
        // entirely.
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "CartItems" ("Id", "CartId", "ProductId", "Quantity")
            VALUES (gen_random_uuid(), {cart.Id}, {product.Id}, {request.Quantity})
            ON CONFLICT ("CartId", "ProductId")
            DO UPDATE SET "Quantity" = "CartItems"."Quantity" + EXCLUDED."Quantity"
            """);

        var updatedQuantity = await _db.CartItems
            .AsNoTracking()
            .Where(i => i.CartId == cart.Id && i.ProductId == product.Id)
            .Select(i => i.Quantity)
            .FirstAsync();

        if (updatedQuantity > product.StockQty)
        {
            // Roll the quantity back to the stock cap rather than leaving an
            // over-limit row in the cart or silently succeeding.
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "CartItems"
                SET "Quantity" = {product.StockQty}
                WHERE "CartId" = {cart.Id} AND "ProductId" = {product.Id}
                """);

            throw new ArgumentException($"Only {product.StockQty} unit(s) of '{product.Name}' are in stock.");
        }

        return await GetCartAsync(userId);
    }

    public async Task<CartDto> UpdateCartItemAsync(Guid userId, Guid productId, int quantity)
    {
        var cart = await GetOrCreateCartAsync(userId);
        var item = cart.Items.FirstOrDefault(i => i.ProductId == productId)
            ?? throw new KeyNotFoundException("This item is not in your cart.");

        if (quantity <= 0)
        {
            _db.CartItems.Remove(item);
        }
        else
        {
            var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId)
                ?? throw new KeyNotFoundException("Product not found.");

            if (quantity > product.StockQty)
                throw new ArgumentException($"Only {product.StockQty} unit(s) of '{product.Name}' are in stock.");

            item.Quantity = quantity;
        }

        await _db.SaveChangesAsync();
        return await GetCartAsync(userId);
    }

    public async Task<CartDto> RemoveCartItemAsync(Guid userId, Guid productId)
    {
        var cart = await GetOrCreateCartAsync(userId);
        var item = cart.Items.FirstOrDefault(i => i.ProductId == productId)
            ?? throw new KeyNotFoundException("This item is not in your cart.");

        _db.CartItems.Remove(item);
        await _db.SaveChangesAsync();
        return await GetCartAsync(userId);
    }

    public async Task ClearCartAsync(Guid userId)
    {
        var cart = await GetOrCreateCartAsync(userId);
        _db.CartItems.RemoveRange(cart.Items);
        await _db.SaveChangesAsync();
    }

    // ==================== Orders ====================

    public async Task<OrderDto> PlaceOrderAsync(Guid userId, PlaceOrderRequest request)
    {
        var seller = await _db.Sellers.FirstOrDefaultAsync(s => s.Id == request.SellerId)
            ?? throw new KeyNotFoundException("Seller not found.");

        if (seller.Status != SellerStatus.Approved)
            throw new InvalidOperationException("This seller is not currently accepting orders.");

        var address = await GetOwnedAddressAsync(userId, request.AddressId);

        var cart = await GetOrCreateCartAsync(userId);
        var itemsForSeller = cart.Items
            .Where(i => i.Product is not null && i.Product.SellerId == request.SellerId)
            .ToList();

        if (itemsForSeller.Count == 0)
            throw new InvalidOperationException("Your cart has no items from this seller.");

        // Gate online orders: seller must accept UPI, and the buyer must supply
        // a UPI reference/UTR. Without a payment gateway the server can't verify
        // the payment itself, so the order is created in PendingPayment and only
        // becomes active once the seller confirms receipt.
        if (request.PaymentMode == PaymentMode.Online)
        {
            if (string.IsNullOrWhiteSpace(seller.UpiId))
                throw new InvalidOperationException("This seller isn't accepting online payments right now.");
            if (string.IsNullOrWhiteSpace(request.PaymentReference))
                throw new ArgumentException("Enter the UPI reference / UTR from your payment before placing the order.");
        }

        foreach (var item in itemsForSeller)
        {
            var product = item.Product!;
            if (!product.IsActive)
                throw new ArgumentException($"'{product.Name}' is no longer available.");
            if (item.Quantity > product.StockQty)
                throw new ArgumentException($"Only {product.StockQty} unit(s) of '{product.Name}' are in stock.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();

        var order = new Order
        {
            BuyerId = userId,
            SellerId = seller.Id,
            AddressId = address.Id,
            PaymentMode = request.PaymentMode,
            // Online orders wait for the seller to confirm payment before they
            // enter the active queue; COD is active immediately.
            Status = request.PaymentMode == PaymentMode.Online ? OrderStatus.PendingPayment : OrderStatus.Placed,
            PaymentStatus = PaymentStatus.Pending
        };

        decimal total = 0m;
        foreach (var item in itemsForSeller)
        {
            var product = item.Product!;
            var lineTotal = product.Price * item.Quantity;
            total += lineTotal;

            order.Items.Add(new OrderItem
            {
                ProductId = product.Id,
                Quantity = item.Quantity,
                PriceAtPurchase = product.Price
            });

            // Atomic conditional decrement: only succeeds if stock is still
            // sufficient at write time, closing the race where two concurrent
            // orders both pass the earlier in-memory check and oversell.
            var rows = await _db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Products" SET "StockQty" = "StockQty" - {item.Quantity}
                WHERE "Id" = {product.Id} AND "StockQty" >= {item.Quantity}
                """);
            if (rows == 0)
                throw new ArgumentException($"Only a few unit(s) of '{product.Name}' are left in stock. Please refresh your cart.");
        }

        order.TotalAmount = total;

        order.Payment = new Payment
        {
            Gateway = request.PaymentMode == PaymentMode.Online ? "UPI" : "COD",
            Amount = total,
            // Buyer-entered UPI reference (UTR) when paying online; used for
            // manual reconciliation. Payment stays Pending until verified —
            // tapping "I've paid" places the order but does not confirm money.
            TransactionId = request.PaymentMode == PaymentMode.Online ? request.PaymentReference?.Trim() : null,
            Status = PaymentStatus.Pending
        };

        _db.Orders.Add(order);
        _db.CartItems.RemoveRange(itemsForSeller);

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return MapOrder(order);
    }

    public async Task<List<OrderDto>> GetMyOrdersAsync(Guid userId, string? status)
    {
        var query = _db.Orders.Where(o => o.BuyerId == userId).AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<OrderStatus>(status, true, out var parsedStatus))
                throw new ArgumentException($"Unknown order status '{status}'.");
            query = query.Where(o => o.Status == parsedStatus);
        }

        var orders = await query.OrderByDescending(o => o.CreatedAt).ToListAsync();
        return orders.Select(MapOrder).ToList();
    }

    public async Task<BuyerOrderDetailDto> GetOrderDetailAsync(Guid userId, Guid orderId)
    {
        var order = await _db.Orders
            .Include(o => o.Seller)
            .Include(o => o.Address)
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.BuyerId == userId)
            ?? throw new KeyNotFoundException("Order not found.");

        return new BuyerOrderDetailDto(
            order.Id,
            order.SellerId,
            order.Seller?.CompanyName ?? "",
            order.Status.ToString(),
            order.PaymentMode.ToString(),
            order.PaymentStatus.ToString(),
            order.TotalAmount,
            order.CreatedAt,
            order.DeliveredAt,
            order.Address is null ? null : $"{order.Address.Line1}, {order.Address.City}, {order.Address.State} {order.Address.Pincode}",
            order.Items.Select(i => new BuyerOrderItemDto(
                i.ProductId,
                i.Product?.Name ?? "",
                i.Product?.VolumeLabel ?? "",
                i.Quantity,
                i.PriceAtPurchase
            )).ToList()
        );
    }

    public async Task<OrderDto> CancelOrderAsync(Guid userId, Guid orderId)
    {
        var order = await _db.Orders
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.BuyerId == userId)
            ?? throw new KeyNotFoundException("Order not found.");

        if (order.Status is OrderStatus.Delivered or OrderStatus.Cancelled)
            throw new InvalidOperationException($"An order that is already {order.Status} cannot be cancelled.");

        if (order.Status == OrderStatus.OutForDelivery)
            throw new InvalidOperationException("This order is already out for delivery and cannot be cancelled online. Please contact the seller.");

        order.Status = OrderStatus.Cancelled;

        foreach (var item in order.Items)
        {
            if (item.Product is not null)
                item.Product.StockQty += item.Quantity;
        }

        if (order.PaymentStatus == PaymentStatus.Success)
            order.PaymentStatus = PaymentStatus.Refunded;

        await _db.SaveChangesAsync();
        return MapOrder(order);
    }

    // ==================== Reviews ====================

    public async Task<List<SellerReviewDto>> GetSellerReviewsAsync(Guid sellerId)
    {
        var sellerExists = await _db.Sellers.AnyAsync(s => s.Id == sellerId);
        if (!sellerExists)
            throw new KeyNotFoundException("Seller not found.");

        var reviews = await _db.Reviews
            .Include(r => r.Buyer)
            .Where(r => r.SellerId == sellerId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        return reviews.Select(r => new SellerReviewDto(r.Id, r.Buyer?.Name ?? "Anonymous", r.Rating, r.Comment, r.CreatedAt)).ToList();
    }

    public async Task<SellerReviewDto> AddReviewAsync(Guid userId, Guid sellerId, CreateReviewRequest request)
    {
        var sellerExists = await _db.Sellers.AnyAsync(s => s.Id == sellerId);
        if (!sellerExists)
            throw new KeyNotFoundException("Seller not found.");

        if (request.Rating is < 1 or > 5)
            throw new ArgumentException("Rating must be between 1 and 5.");

        var hasDeliveredOrder = await _db.Orders.AnyAsync(o =>
            o.BuyerId == userId && o.SellerId == sellerId && o.Status == OrderStatus.Delivered);
        if (!hasDeliveredOrder)
            throw new InvalidOperationException("You can only review a seller after an order has been delivered.");

        var review = await _db.Reviews.Include(r => r.Buyer)
            .FirstOrDefaultAsync(r => r.SellerId == sellerId && r.BuyerId == userId);

        if (review is null)
        {
            review = new Review
            {
                SellerId = sellerId,
                BuyerId = userId,
                Rating = request.Rating,
                Comment = request.Comment?.Trim()
            };
            _db.Reviews.Add(review);
        }
        else
        {
            review.Rating = request.Rating;
            review.Comment = request.Comment?.Trim();
        }

        await _db.SaveChangesAsync();

        var buyerName = review.Buyer?.Name ?? await _db.Users.Where(u => u.Id == userId).Select(u => u.Name).FirstOrDefaultAsync() ?? "";
        return new SellerReviewDto(review.Id, buyerName, review.Rating, review.Comment, review.CreatedAt);
    }

    // ==================== helpers ====================

    private async Task<Cart> GetOrCreateCartAsync(Guid userId)
    {
        var cart = await _db.Carts
            .Include(c => c.Items).ThenInclude(i => i.Product)
                .ThenInclude(p => p!.Seller)
            .FirstOrDefaultAsync(c => c.BuyerId == userId);

        if (cart is not null)
            return cart;

        cart = new Cart { BuyerId = userId };
        _db.Carts.Add(cart);
        await _db.SaveChangesAsync();
        return cart;
    }

    private async Task<Address> GetOwnedAddressAsync(Guid userId, Guid addressId)
    {
        return await _db.Addresses.FirstOrDefaultAsync(a => a.Id == addressId && a.UserId == userId)
            ?? throw new KeyNotFoundException("Address not found.");
    }

    private async Task UnsetExistingDefaultsAsync(Guid userId)
    {
        var currentDefaults = await _db.Addresses.Where(a => a.UserId == userId && a.IsDefault).ToListAsync();
        foreach (var a in currentDefaults)
            a.IsDefault = false;
    }

    private static void ValidateAddressFields(string line1, string city, string state, string pincode)
    {
        if (string.IsNullOrWhiteSpace(line1))
            throw new ArgumentException("Address line 1 is required.");
        if (string.IsNullOrWhiteSpace(city))
            throw new ArgumentException("City is required.");
        if (string.IsNullOrWhiteSpace(state))
            throw new ArgumentException("State is required.");
        if (string.IsNullOrWhiteSpace(pincode))
            throw new ArgumentException("Pincode is required.");
    }

    private static AddressDto MapAddress(Address a) => new(
        a.Id, a.Line1, a.Line2, a.City, a.State, a.Pincode, a.Latitude, a.Longitude, a.IsDefault
    );

    private static ProductDto MapProduct(Product p) => new(
        p.Id, p.SellerId, p.Name, p.VolumeLabel, p.Price, p.StockQty, p.IsActive, p.ImageUrl
    );

    private static CartDto MapCart(Cart cart)
    {
        var items = cart.Items
            .Where(i => i.Product is not null)
            .Select(i => new CartItemDto(
                i.ProductId,
                i.Product!.Name,
                i.Product!.Price,
                i.Quantity,
                i.Product!.SellerId,
                i.Product!.Seller?.CompanyName ?? "",
                i.Product!.Seller?.UpiId))
            .ToList();

        var total = items.Sum(i => i.Price * i.Quantity);
        return new CartDto(cart.Id, items, total);
    }

    private static OrderDto MapOrder(Order o) => new(
        o.Id, o.Status.ToString(), o.PaymentMode.ToString(), o.PaymentStatus.ToString(), o.TotalAmount, o.CreatedAt
    );
}

using WaterApp.Domain.Enums;

namespace WaterApp.Domain.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Address> Addresses { get; set; } = new List<Address>();
    public Seller? SellerProfile { get; set; }
}

public class Address
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public string Line1 { get; set; } = string.Empty;
    public string? Line2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Pincode { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public bool IsDefault { get; set; }
}

public class Seller
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public string CompanyName { get; set; } = string.Empty;
    public SellerCategory Category { get; set; } = SellerCategory.Water;
    public string? LicenseDocUrl { get; set; }
    public string? LogoUrl { get; set; }
    public string? UpiId { get; set; }
    public SellerStatus Status { get; set; } = SellerStatus.Pending;

    public double BaseLatitude { get; set; }
    public double BaseLongitude { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ServiceArea> ServiceAreas { get; set; } = new List<ServiceArea>();
    public ICollection<Product> Products { get; set; } = new List<Product>();
}

public class ServiceArea
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SellerId { get; set; }
    public Seller? Seller { get; set; }
    public string Pincode { get; set; } = string.Empty;
}

public class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SellerId { get; set; }
    public Seller? Seller { get; set; }

    public string Name { get; set; } = string.Empty;
    public SellerCategory Category { get; set; } = SellerCategory.Water;
    public string VolumeLabel { get; set; } = string.Empty; // unit/size, e.g. "1L", "500g", "1 plate", "14.2kg"
    public decimal Price { get; set; }
    public int StockQty { get; set; }
    public bool IsActive { get; set; } = true;

    // Fallback for a manually pasted external image link (no UI sets this
    // today). The normal path is an uploaded photo — see ProductImage,
    // which is deliberately a separate table/entity rather than columns
    // here: Product rows get loaded constantly (carts, orders, listings)
    // via plain .Include(), and EF Core loads every scalar column on an
    // entity by default. Keeping image bytes in their own table means
    // those everyday queries never drag a multi-hundred-KB blob along for
    // the ride — only the one endpoint that actually serves the image asks
    // for it.
    public string? ImageUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// One row per product that has an uploaded photo. ProductId is both the
// primary key and the FK — a product has at most one stored image.
public class ProductImage
{
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }

    public byte[] Data { get; set; } = Array.Empty<byte>();
    public string ContentType { get; set; } = "image/jpeg";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Cart
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BuyerId { get; set; }
    public User? Buyer { get; set; }

    public ICollection<CartItem> Items { get; set; } = new List<CartItem>();
}

public class CartItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CartId { get; set; }
    public Cart? Cart { get; set; }

    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public int Quantity { get; set; }
}

public class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BuyerId { get; set; }
    public User? Buyer { get; set; }

    public Guid SellerId { get; set; }
    public Seller? Seller { get; set; }

    public Guid AddressId { get; set; }
    public Address? Address { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Placed;
    public PaymentMode PaymentMode { get; set; }
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;

    public decimal TotalAmount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeliveredAt { get; set; }

    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    public Payment? Payment { get; set; }
}

public class OrderItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Order? Order { get; set; }

    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public int Quantity { get; set; }
    public decimal PriceAtPurchase { get; set; }
}

public class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Order? Order { get; set; }

    public string Gateway { get; set; } = "Razorpay";
    public string? GatewayOrderId { get; set; }
    public string? TransactionId { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public decimal Amount { get; set; }
    public DateTime? PaidAt { get; set; }
}

public class Review
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SellerId { get; set; }
    public Seller? Seller { get; set; }

    public Guid BuyerId { get; set; }
    public User? Buyer { get; set; }

    public int Rating { get; set; } // 1-5
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// A device's Expo push token, registered by the app after the user grants
// notification permission and signs in. One token maps to at most one
// user at a time (see the unique index in AppDbContext) — if a different
// account later signs in on the same device, re-registering the token
// reassigns it rather than notifying both accounts.
public class PushToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public string Token { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// A one-time SMS verification code for the "forgot password" flow. Only
// the most recent row per user is ever valid — ForgotPasswordAsync clears
// any earlier ones when it issues a new code.
public class PasswordResetOtp
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public string CodeHash { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// Server-side record of an issued refresh token, so it can actually be
// validated, rotated, and revoked — previously GenerateRefreshToken()'s
// output was handed to the client and never checked again by anything,
// meaning the only way to end a session was the 60-minute access token
// expiring (forcing a full re-login every hour with no way back in
// between). Only TokenHash (SHA-256 of the raw token) is stored, never the
// raw token itself, the same principle as password hashing.
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    // Set the moment this token is used (rotation) or the user logs out.
    // A revoked-but-not-yet-expired row is kept, not deleted, so reuse of
    // an already-rotated token is still detectable rather than looking
    // like "token not found".
    public DateTime? RevokedAt { get; set; }
}

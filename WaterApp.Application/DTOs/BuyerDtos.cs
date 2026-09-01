using System.ComponentModel.DataAnnotations;

namespace WaterApp.Application.DTOs;

// ---- Addresses ----

public record AddressDto(
    Guid Id,
    string Line1,
    string? Line2,
    string City,
    string State,
    string Pincode,
    double? Latitude,
    double? Longitude,
    bool IsDefault
);

public record AddressCreateRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Line1,
    [StringLength(200)] string? Line2,
    [Required, StringLength(100, MinimumLength = 1)] string City,
    [Required, StringLength(100, MinimumLength = 1)] string State,
    [Required, StringLength(10, MinimumLength = 4)] string Pincode,
    double? Latitude,
    double? Longitude,
    bool IsDefault
);

public record AddressUpdateRequest(
    [Required, StringLength(200, MinimumLength = 1)] string Line1,
    [StringLength(200)] string? Line2,
    [Required, StringLength(100, MinimumLength = 1)] string City,
    [Required, StringLength(100, MinimumLength = 1)] string State,
    [Required, StringLength(10, MinimumLength = 4)] string Pincode,
    double? Latitude,
    double? Longitude,
    bool IsDefault
);

// ---- Order detail (buyer view) ----

public record BuyerOrderItemDto(
    Guid ProductId,
    string ProductName,
    string VolumeLabel,
    int Quantity,
    decimal PriceAtPurchase
);

public record BuyerOrderDetailDto(
    Guid Id,
    Guid SellerId,
    string SellerName,
    string Status,
    string PaymentMode,
    string PaymentStatus,
    decimal TotalAmount,
    DateTime CreatedAt,
    DateTime? DeliveredAt,
    string? AddressSummary,
    List<BuyerOrderItemDto> Items
);

// ---- Reviews ----

public record SellerReviewDto(
    Guid Id,
    string BuyerName,
    int Rating,
    string? Comment,
    DateTime CreatedAt
);

public record CreateReviewRequest(
    [Range(1, 5, ErrorMessage = "Rating must be between 1 and 5.")] int Rating,
    [StringLength(1000)] string? Comment
);

// ---- Cart ----

// 0 is a valid, intentional value here — it means "remove this item from
// the cart" (see BuyerService.UpdateCartItemAsync). Only negative values
// and unreasonably large ones are actually invalid.
public record UpdateCartItemRequest([Range(0, 999)] int Quantity);

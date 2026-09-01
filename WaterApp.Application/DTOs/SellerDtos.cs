using System.ComponentModel.DataAnnotations;

namespace WaterApp.Application.DTOs;

public record SellerProfileDto(
    Guid Id,
    string CompanyName,
    string Status,
    string? LogoUrl,
    string? UpiId,
    double BaseLatitude,
    double BaseLongitude,
    List<string> ServicePincodes,
    DateTime CreatedAt
);

// UpiId is deliberately NOT annotated with a format attribute here —
// SellerService.UpdatePaymentSettingsAsync already validates the VPA shape
// with its own regex and error message. Duplicating that check here with a
// second, slightly different pattern would risk the two disagreeing on
// what's valid.
public record UpdatePaymentSettingsRequest(string? UpiId);

// Returned to the buyer at checkout so the app can build the UPI QR.
public record SellerPaymentInfoDto(Guid SellerId, string CompanyName, string? UpiId, bool AcceptsOnline);

public record ProductUpdateRequest(
    [Required, StringLength(100, MinimumLength = 1)] string Name,
    [Required] string Category,
    [Required, StringLength(50, MinimumLength = 1)] string VolumeLabel,
    [Range(typeof(decimal), "0.01", "100000")] decimal Price,
    [Range(0, 100000)] int StockQty,
    bool IsActive
);

public record SellerOrderItemDto(
    Guid ProductId,
    string ProductName,
    string VolumeLabel,
    int Quantity,
    decimal PriceAtPurchase
);

public record SellerOrderDto(
    Guid Id,
    string BuyerName,
    string BuyerPhone,
    string Status,
    string PaymentMode,
    string PaymentStatus,
    string? PaymentReference,
    decimal TotalAmount,
    DateTime CreatedAt,
    DateTime? DeliveredAt,
    string? AddressSummary,
    List<SellerOrderItemDto> Items
);

public record UpdateOrderStatusRequest([Required] string Status);

public record SellerDashboardStatsDto(
    int TotalProducts,
    int ActiveProducts,
    int LowStockProducts,
    int PendingOrders,
    int TotalOrders,
    int TodayOrders,
    decimal TotalRevenue,
    decimal TodayRevenue
);

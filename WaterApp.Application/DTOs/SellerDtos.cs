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

public record UpdatePaymentSettingsRequest(string? UpiId);

// Returned to the buyer at checkout so the app can build the UPI QR.
public record SellerPaymentInfoDto(Guid SellerId, string CompanyName, string? UpiId, bool AcceptsOnline);

public record ProductUpdateRequest(string Name, string VolumeLabel, decimal Price, int StockQty, bool IsActive);

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

public record UpdateOrderStatusRequest(string Status);

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

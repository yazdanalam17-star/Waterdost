using System.ComponentModel.DataAnnotations;

namespace WaterApp.Application.DTOs;

public record AdminStatsResponse(
    int TotalUsers,
    int TotalBuyers,
    int TotalSellers,
    int TotalAdmins,
    int PendingSellers,
    int ApprovedSellers,
    int TotalOrders,
    decimal TotalRevenue
);

public record AdminSellerResponse(
    Guid Id,
    Guid UserId,
    string OwnerName,
    string Phone,
    string? Email,
    string CompanyName,
    string Status,
    DateTime CreatedAt
);

public record UpdateSellerStatusRequest([Required] string Status);

public record AdminBuyerResponse(
    Guid Id,
    string Name,
    string Phone,
    string? Email,
    bool IsActive,
    int OrderCount,
    DateTime CreatedAt
);

public record UpdateBuyerStatusRequest(bool IsActive);

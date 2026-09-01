using WaterApp.Application.DTOs;

namespace WaterApp.Application.Interfaces;

public interface IAdminService
{
    Task<AdminStatsResponse> GetStatsAsync();
    Task<List<AdminSellerResponse>> GetSellersAsync(string? status, int page = 1, int pageSize = 50);
    Task<AdminSellerResponse> UpdateSellerStatusAsync(Guid sellerId, string status);
    Task<List<AdminBuyerResponse>> GetBuyersAsync(string? search, int page = 1, int pageSize = 50);
    Task<AdminBuyerResponse> UpdateBuyerStatusAsync(Guid buyerId, bool isActive);
}

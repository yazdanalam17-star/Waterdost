using WaterApp.Application.DTOs;

namespace WaterApp.Application.Interfaces;

public interface ISellerService
{
    Task<SellerProfileDto?> GetMyProfileAsync(Guid userId);
    Task<SellerProfileDto> RegisterAsync(Guid userId, SellerRegisterRequest request);
    Task<SellerProfileDto> UpdatePaymentSettingsAsync(Guid userId, string? upiId);

    Task<List<ProductDto>> GetMyProductsAsync(Guid userId);
    Task<ProductDto> CreateProductAsync(Guid userId, ProductCreateRequest request);
    Task<ProductDto> UpdateProductAsync(Guid userId, Guid productId, ProductUpdateRequest request);
    Task DeleteProductAsync(Guid userId, Guid productId);

    Task<List<SellerOrderDto>> GetMyOrdersAsync(Guid userId, string? status);
    Task<SellerOrderDto> UpdateOrderStatusAsync(Guid userId, Guid orderId, string status);
    Task<SellerOrderDto> ConfirmPaymentAsync(Guid userId, Guid orderId);

    Task<SellerDashboardStatsDto> GetDashboardStatsAsync(Guid userId);
}

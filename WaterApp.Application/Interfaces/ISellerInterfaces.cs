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

    // Deliberately takes a raw Stream/contentType/length rather than
    // ASP.NET Core's IFormFile — this project doesn't reference the web
    // framework (see WaterApp.Application.csproj), and shouldn't need to
    // just to accept an upload. The controller unwraps IFormFile before
    // calling in.
    Task<ProductDto> SetProductImageAsync(Guid userId, Guid productId, Stream? imageStream, string? contentType, long length);
    Task<ProductDto> RemoveProductImageAsync(Guid userId, Guid productId);

    Task<List<SellerOrderDto>> GetMyOrdersAsync(Guid userId, string? status, int page = 1, int pageSize = 50);
    Task<SellerOrderDto> UpdateOrderStatusAsync(Guid userId, Guid orderId, string status);
    Task<SellerOrderDto> ConfirmPaymentAsync(Guid userId, Guid orderId);

    Task<SellerDashboardStatsDto> GetDashboardStatsAsync(Guid userId);
}

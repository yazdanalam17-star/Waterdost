using WaterApp.Application.DTOs;

namespace WaterApp.Application.Interfaces;

public interface IBuyerService
{
    // ---- Catalog browsing (public) ----
    Task<List<SellerDto>> GetSellersInAreaAsync(string pincode, string? category = null);
    Task<List<ProductWithSellerDto>> GetProductsByCategoryAsync(string pincode, string category);
    Task<List<ProductDto>> GetSellerProductsAsync(Guid sellerId);

    // Raw image bytes for /api/products/{id}/image (public — buyers browsing
    // without an account need to see product photos too). Null means no
    // image is stored for that product.
    Task<(byte[] Data, string ContentType)?> GetProductImageAsync(Guid productId);

    // ---- Addresses ----
    Task<List<AddressDto>> GetMyAddressesAsync(Guid userId);
    Task<AddressDto> AddAddressAsync(Guid userId, AddressCreateRequest request);
    Task<AddressDto> UpdateAddressAsync(Guid userId, Guid addressId, AddressUpdateRequest request);
    Task DeleteAddressAsync(Guid userId, Guid addressId);
    Task<AddressDto> SetDefaultAddressAsync(Guid userId, Guid addressId);

    // ---- Cart ----
    Task<CartDto> GetCartAsync(Guid userId);
    Task<CartDto> AddToCartAsync(Guid userId, AddToCartRequest request);
    Task<CartDto> UpdateCartItemAsync(Guid userId, Guid productId, int quantity);
    Task<CartDto> RemoveCartItemAsync(Guid userId, Guid productId);
    Task ClearCartAsync(Guid userId);

    // ---- Orders ----
    Task<OrderDto> PlaceOrderAsync(Guid userId, PlaceOrderRequest request);
    Task<List<OrderDto>> GetMyOrdersAsync(Guid userId, string? status, int page = 1, int pageSize = 50);
    Task<BuyerOrderDetailDto> GetOrderDetailAsync(Guid userId, Guid orderId);
    Task<OrderDto> CancelOrderAsync(Guid userId, Guid orderId);

    // ---- Reviews ----
    Task<List<SellerReviewDto>> GetSellerReviewsAsync(Guid sellerId, int page = 1, int pageSize = 50);
    Task<SellerReviewDto> AddReviewAsync(Guid userId, Guid sellerId, CreateReviewRequest request);
}

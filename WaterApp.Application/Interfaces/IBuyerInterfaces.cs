using WaterApp.Application.DTOs;

namespace WaterApp.Application.Interfaces;

public interface IBuyerService
{
    // ---- Catalog browsing (public) ----
    Task<List<SellerDto>> GetSellersInAreaAsync(string pincode, string? category = null);
    Task<List<ProductWithSellerDto>> GetProductsByCategoryAsync(string pincode, string category);
    Task<List<ProductDto>> GetSellerProductsAsync(Guid sellerId);

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
    Task<List<OrderDto>> GetMyOrdersAsync(Guid userId, string? status);
    Task<BuyerOrderDetailDto> GetOrderDetailAsync(Guid userId, Guid orderId);
    Task<OrderDto> CancelOrderAsync(Guid userId, Guid orderId);

    // ---- Reviews ----
    Task<List<SellerReviewDto>> GetSellerReviewsAsync(Guid sellerId);
    Task<SellerReviewDto> AddReviewAsync(Guid userId, Guid sellerId, CreateReviewRequest request);
}

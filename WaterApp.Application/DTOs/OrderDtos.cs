using System.ComponentModel.DataAnnotations;
using WaterApp.Domain.Enums;

namespace WaterApp.Application.DTOs;

public record AddToCartRequest(Guid ProductId, [Range(1, 999)] int Quantity);

public record CartItemDto(Guid ProductId, string ProductName, decimal Price, int Quantity, Guid SellerId, string SellerName, string? SellerUpiId);

public record CartDto(Guid CartId, List<CartItemDto> Items, decimal Total);

// PaymentReference is the UPI txn/UTR the buyer enters after paying via QR.
// Optional: null for COD; for Online it's stored on the Payment for later reconciliation.
public record PlaceOrderRequest(
    Guid SellerId,
    Guid AddressId,
    PaymentMode PaymentMode,
    [StringLength(100)] string? PaymentReference
);

public record OrderDto(Guid Id, string Status, string PaymentMode, string PaymentStatus, decimal TotalAmount, DateTime CreatedAt);

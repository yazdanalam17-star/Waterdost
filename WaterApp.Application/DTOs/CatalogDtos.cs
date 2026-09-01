using System.ComponentModel.DataAnnotations;

namespace WaterApp.Application.DTOs;

public record SellerRegisterRequest(
    [Required, StringLength(150, MinimumLength = 1)] string CompanyName,
    [Required] string Category,
    [Range(-90, 90)] double BaseLatitude,
    [Range(-180, 180)] double BaseLongitude,
    [Required, MinLength(1, ErrorMessage = "Add at least one pincode you deliver to.")] List<string> ServicePincodes
);

public record SellerDto(Guid Id, string CompanyName, string Category, string Status, string? LogoUrl, string? UpiId);

public record ProductCreateRequest(
    [Required, StringLength(100, MinimumLength = 1)] string Name,
    [Required] string Category,
    [Required, StringLength(50, MinimumLength = 1)] string VolumeLabel,
    [Range(typeof(decimal), "0.01", "100000")] decimal Price,
    [Range(0, 100000)] int StockQty
);

public record ProductDto(Guid Id, Guid SellerId, string Name, string Category, string VolumeLabel, decimal Price, int StockQty, bool IsActive, string? ImageUrl);

// Product plus its seller's name, for the buyer's product-first category browse.
public record ProductWithSellerDto(Guid Id, Guid SellerId, string SellerName, string Name, string Category, string VolumeLabel, decimal Price, int StockQty, string? ImageUrl);

namespace WaterApp.Application.DTOs;

public record SellerRegisterRequest(string CompanyName, string Category, double BaseLatitude, double BaseLongitude, List<string> ServicePincodes);

public record SellerDto(Guid Id, string CompanyName, string Category, string Status, string? LogoUrl, string? UpiId);

public record ProductCreateRequest(string Name, string Category, string VolumeLabel, decimal Price, int StockQty);

public record ProductDto(Guid Id, Guid SellerId, string Name, string Category, string VolumeLabel, decimal Price, int StockQty, bool IsActive, string? ImageUrl);

// Product plus its seller's name, for the buyer's product-first category browse.
public record ProductWithSellerDto(Guid Id, Guid SellerId, string SellerName, string Name, string Category, string VolumeLabel, decimal Price, int StockQty, string? ImageUrl);

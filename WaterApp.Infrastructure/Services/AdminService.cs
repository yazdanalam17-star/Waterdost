using Microsoft.EntityFrameworkCore;
using WaterApp.Application.DTOs;
using WaterApp.Application.Interfaces;
using WaterApp.Domain.Enums;
using WaterApp.Infrastructure.Data;

namespace WaterApp.Infrastructure.Services;

public class AdminService : IAdminService
{
    private readonly AppDbContext _db;
    private readonly INotificationService _notifications;

    public AdminService(AppDbContext db, INotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    public async Task<AdminStatsResponse> GetStatsAsync()
    {
        var totalUsers = await _db.Users.CountAsync();
        var totalBuyers = await _db.Users.CountAsync(u => u.Role == UserRole.Buyer);
        var totalSellers = await _db.Users.CountAsync(u => u.Role == UserRole.Seller);
        var totalAdmins = await _db.Users.CountAsync(u => u.Role == UserRole.Admin);

        var pendingSellers = await _db.Sellers.CountAsync(s => s.Status == SellerStatus.Pending);
        var approvedSellers = await _db.Sellers.CountAsync(s => s.Status == SellerStatus.Approved);

        var totalOrders = await _db.Orders.CountAsync();
        var totalRevenue = await _db.Orders
            .Where(o => o.PaymentStatus == PaymentStatus.Success || o.PaymentStatus == PaymentStatus.CollectedInCash)
            .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

        return new AdminStatsResponse(
            totalUsers,
            totalBuyers,
            totalSellers,
            totalAdmins,
            pendingSellers,
            approvedSellers,
            totalOrders,
            totalRevenue
        );
    }

    public async Task<List<AdminSellerResponse>> GetSellersAsync(string? status, int page = 1, int pageSize = 50)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 100 ? 50 : pageSize;

        var query = _db.Sellers.Include(s => s.User).AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<SellerStatus>(status, true, out var parsedStatus))
                throw new ArgumentException($"Unknown seller status '{status}'.");
            query = query.Where(s => s.Status == parsedStatus);
        }

        var sellers = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return sellers.Select(s => new AdminSellerResponse(
            s.Id,
            s.UserId,
            s.User?.Name ?? "",
            s.User?.Phone ?? "",
            s.User?.Email,
            s.CompanyName,
            s.Status.ToString(),
            s.CreatedAt
        )).ToList();
    }

    public async Task<AdminSellerResponse> UpdateSellerStatusAsync(Guid sellerId, string status)
    {
        if (!Enum.TryParse<SellerStatus>(status, true, out var parsedStatus))
            throw new ArgumentException($"Unknown seller status '{status}'.");

        var seller = await _db.Sellers.Include(s => s.User).FirstOrDefaultAsync(s => s.Id == sellerId)
            ?? throw new KeyNotFoundException("Seller not found.");

        seller.Status = parsedStatus;
        await _db.SaveChangesAsync();

        if (parsedStatus is SellerStatus.Approved or SellerStatus.Rejected)
        {
            await _notifications.NotifyUserAsync(
                seller.UserId,
                parsedStatus == SellerStatus.Approved ? "Seller application approved" : "Seller application update",
                parsedStatus == SellerStatus.Approved
                    ? "Your store is approved and now visible to buyers."
                    : "Your seller application was not approved. Contact support for details."
            );
        }

        return new AdminSellerResponse(
            seller.Id,
            seller.UserId,
            seller.User?.Name ?? "",
            seller.User?.Phone ?? "",
            seller.User?.Email,
            seller.CompanyName,
            seller.Status.ToString(),
            seller.CreatedAt
        );
    }

    public async Task<List<AdminBuyerResponse>> GetBuyersAsync(string? search, int page = 1, int pageSize = 50)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 100 ? 50 : pageSize;

        var query = _db.Users.Where(u => u.Role == UserRole.Buyer);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(u => u.Name.Contains(term) || u.Phone.Contains(term));
        }

        // Project order counts in a single query rather than N per-user lookups.
        var buyers = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new AdminBuyerResponse(
                u.Id,
                u.Name,
                u.Phone,
                u.Email,
                u.IsActive,
                _db.Orders.Count(o => o.BuyerId == u.Id),
                u.CreatedAt))
            .ToListAsync();

        return buyers;
    }

    public async Task<AdminBuyerResponse> UpdateBuyerStatusAsync(Guid buyerId, bool isActive)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == buyerId && u.Role == UserRole.Buyer)
            ?? throw new KeyNotFoundException("Buyer not found.");

        user.IsActive = isActive;
        await _db.SaveChangesAsync();

        var orderCount = await _db.Orders.CountAsync(o => o.BuyerId == user.Id);

        return new AdminBuyerResponse(
            user.Id,
            user.Name,
            user.Phone,
            user.Email,
            user.IsActive,
            orderCount,
            user.CreatedAt
        );
    }
}

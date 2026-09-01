using Microsoft.EntityFrameworkCore;
using WaterApp.Application.DTOs;
using WaterApp.Application.Interfaces;
using WaterApp.Domain.Entities;
using WaterApp.Domain.Enums;
using WaterApp.Infrastructure.Data;

namespace WaterApp.Infrastructure.Services;

// Handles "delete my account" for both Buyers and Sellers.
//
// This anonymizes rather than hard-deletes the underlying User row.
// Orders and reviews are left in place — a seller's own order/revenue
// history has to stay intact for their accounting and for other buyers
// who rely on review content, and Postgres would refuse to hard-delete a
// User that Orders/Reviews still point to anyway (Restrict FKs in
// AppDbContext). But every personally identifying field on the User
// itself (name, phone, email, password) is scrubbed, so nothing in the
// system traces back to this person once this runs — anywhere the old
// name/phone would have shown (a seller's order list, a review) now
// reads "Deleted User" because those views join live off the User row.
//
// Addresses and the cart are pure account state with no reason to
// outlive the account, so those are hard-deleted where possible (an
// address still referenced by a past order is scrubbed in place instead —
// see DeleteBuyerAccountAsync).
public class AccountService : IAccountService
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher _hasher;

    public AccountService(AppDbContext db, IPasswordHasher hasher)
    {
        _db = db;
        _hasher = hasher;
    }

    public async Task<ProfileDto> GetProfileAsync(Guid userId)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new KeyNotFoundException("Account not found.");

        return new ProfileDto(user.Id, user.Name, user.Phone, user.Email, user.Role.ToString());
    }

    public async Task<ProfileDto> UpdateProfileAsync(Guid userId, UpdateProfileRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new KeyNotFoundException("Account not found.");

        user.Name = request.Name.Trim();
        user.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();

        await _db.SaveChangesAsync();

        return new ProfileDto(user.Id, user.Name, user.Phone, user.Email, user.Role.ToString());
    }

    public async Task DeleteBuyerAccountAsync(Guid userId)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.Role == UserRole.Buyer)
            ?? throw new KeyNotFoundException("Account not found.");

        // Block deletion mid-delivery — otherwise a seller could be left
        // with an order they can no longer reach the buyer about.
        var hasActiveOrder = await _db.Orders.AnyAsync(o =>
            o.BuyerId == userId && o.Status != OrderStatus.Delivered && o.Status != OrderStatus.Cancelled);
        if (hasActiveOrder)
            throw new InvalidOperationException(
                "You have an order in progress. Please wait for delivery, or cancel it, before deleting your account.");

        await using var transaction = await _db.Database.BeginTransactionAsync();

        // Addresses are hard-deleted UNLESS an order still references them —
        // Order.AddressId is a restricted FK (see AppDbContext), so Postgres
        // would reject deleting an address that's part of the buyer's order
        // history. For those, scrub the identifying fields in place instead
        // of deleting the row.
        var addresses = await _db.Addresses.Where(a => a.UserId == userId).ToListAsync();
        var addressIds = addresses.Select(a => a.Id).ToList();
        var referencedAddressIds = (await _db.Orders
            .Where(o => addressIds.Contains(o.AddressId))
            .Select(o => o.AddressId)
            .Distinct()
            .ToListAsync())
            .ToHashSet();

        foreach (var address in addresses)
        {
            if (referencedAddressIds.Contains(address.Id))
            {
                address.Line1 = "Deleted address";
                address.Line2 = null;
                address.Latitude = null;
                address.Longitude = null;
            }
            else
            {
                _db.Addresses.Remove(address);
            }
        }

        var cart = await _db.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.BuyerId == userId);
        if (cart is not null)
        {
            _db.CartItems.RemoveRange(cart.Items);
            _db.Carts.Remove(cart);
        }

        AnonymizeUser(user);

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task DeleteSellerAccountAsync(Guid userId)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.Role == UserRole.Seller)
            ?? throw new KeyNotFoundException("Account not found.");

        var seller = await _db.Sellers
            .Include(s => s.Products)
            .FirstOrDefaultAsync(s => s.UserId == userId);

        await using var transaction = await _db.Database.BeginTransactionAsync();

        if (seller is not null)
        {
            var hasActiveOrder = await _db.Orders.AnyAsync(o =>
                o.SellerId == seller.Id && o.Status != OrderStatus.Delivered && o.Status != OrderStatus.Cancelled);
            if (hasActiveOrder)
                throw new InvalidOperationException(
                    "You have an order in progress. Please complete or cancel it before deleting your account.");

            // Unlist the storefront rather than deleting it: buyers can no
            // longer find this seller or order from it, and it drops out of
            // pincode/category search, but past buyers keep an accurate
            // order history instead of it pointing at a vanished seller.
            seller.Status = SellerStatus.Suspended;
            seller.UpiId = null;
            foreach (var product in seller.Products)
                product.IsActive = false;
        }

        AnonymizeUser(user);

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private void AnonymizeUser(User user)
    {
        user.Name = "Deleted User";
        // Phone has a unique index, so this can't just be cleared — a
        // per-user placeholder keeps the constraint satisfied and can never
        // collide with a real phone number or be re-entered at login.
        user.Phone = $"deleted-{user.Id:N}";
        user.Email = null;
        // IsActive = false is what actually blocks login (existing
        // LoginAsync check), but the password hash is replaced too — with a
        // random, never-communicated password — as defense in depth in case
        // IsActive is ever toggled back by mistake.
        user.PasswordHash = _hasher.Hash(Guid.NewGuid().ToString("N"));
        user.IsActive = false;
    }
}

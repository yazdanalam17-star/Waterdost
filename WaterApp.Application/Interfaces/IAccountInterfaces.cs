using WaterApp.Application.DTOs;

namespace WaterApp.Application.Interfaces;

// Self-service account deletion, available to any signed-in Buyer or
// Seller (see AccountController). Deliberately separate from
// IBuyerService/ISellerService since it's cross-cutting: the same
// "delete my account" action needs different cleanup depending on the
// caller's role, but isn't really a "buyer" or "seller" feature itself.
public interface IAccountService
{
    Task<ProfileDto> GetProfileAsync(Guid userId);
    // Phone is deliberately not editable here — it's the unique login
    // identifier, and changing it safely would need its own re-verification
    // flow (OTP to the new number, uniqueness re-check), which is a bigger
    // feature than "edit my profile". Name and email only for now.
    Task<ProfileDto> UpdateProfileAsync(Guid userId, UpdateProfileRequest request);

    Task DeleteBuyerAccountAsync(Guid userId);
    Task DeleteSellerAccountAsync(Guid userId);
}

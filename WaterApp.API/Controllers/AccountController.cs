using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WaterApp.Application.DTOs;
using WaterApp.Application.Interfaces;

namespace WaterApp.API.Controllers;

// Self-service account management for the currently signed-in user:
// viewing/editing your own profile, and deleting your account (required by
// Google Play's User Data policy — any app that lets people create an
// account must also offer an in-app way to delete it).
//
// [Authorize] with no Roles restriction — any signed-in Buyer, Seller, or
// Admin can view/edit their own profile here. Account *deletion*
// specifically excludes Admin (see DeleteMyAccount) — admin accounts are
// seeded/managed by another administrator, not self-registered, so
// self-deletion doesn't apply to them the same way.
[ApiController]
[Route("api/account")]
[Authorize]
public class AccountController : ControllerBase
{
    private readonly IAccountService _accountService;

    public AccountController(IAccountService accountService)
    {
        _accountService = accountService;
    }

    private Guid CurrentUserId =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private string CurrentRole =>
        User.FindFirstValue(ClaimTypes.Role) ?? "";

    [HttpGet("me")]
    public async Task<ActionResult<ProfileDto>> GetProfile()
    {
        try
        {
            return Ok(await _accountService.GetProfileAsync(CurrentUserId));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPut("me")]
    public async Task<ActionResult<ProfileDto>> UpdateProfile(UpdateProfileRequest request)
    {
        try
        {
            return Ok(await _accountService.UpdateProfileAsync(CurrentUserId, request));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpDelete("me")]
    public async Task<IActionResult> DeleteMyAccount()
    {
        try
        {
            switch (CurrentRole)
            {
                case "Buyer":
                    await _accountService.DeleteBuyerAccountAsync(CurrentUserId);
                    return NoContent();
                case "Seller":
                    await _accountService.DeleteSellerAccountAsync(CurrentUserId);
                    return NoContent();
                default:
                    return StatusCode(403, new
                    {
                        message = "Admin accounts can't be self-deleted. Ask another administrator to deactivate this account."
                    });
            }
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }
}

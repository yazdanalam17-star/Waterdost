using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WaterApp.Application.DTOs;
using WaterApp.Application.Interfaces;

namespace WaterApp.API.Controllers;

// Push-token registration and the in-app notification list. Available to
// any signed-in Buyer or Seller — notifications aren't role-specific, the
// events that trigger them just happen to come from buyer/seller/admin
// actions elsewhere (see BuyerService, SellerService, AdminService).
[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;

    public NotificationsController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    private Guid CurrentUserId =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost("register-token")]
    public async Task<IActionResult> RegisterToken(RegisterPushTokenRequest request)
    {
        try
        {
            await _notificationService.RegisterTokenAsync(CurrentUserId, request.Token);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // Called on logout so a signed-out device stops receiving another
    // account's notifications if someone else signs in on it later.
    [HttpDelete("register-token")]
    public async Task<IActionResult> UnregisterToken(RegisterPushTokenRequest request)
    {
        await _notificationService.UnregisterTokenAsync(request.Token);
        return NoContent();
    }

    [HttpGet]
    public async Task<ActionResult<List<NotificationDto>>> GetMyNotifications()
    {
        return Ok(await _notificationService.GetMyNotificationsAsync(CurrentUserId));
    }

    [HttpPost("{id}/read")]
    public async Task<IActionResult> MarkRead(Guid id)
    {
        try
        {
            await _notificationService.MarkReadAsync(CurrentUserId, id);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        await _notificationService.MarkAllReadAsync(CurrentUserId);
        return NoContent();
    }
}

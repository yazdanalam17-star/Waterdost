using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WaterApp.Application.DTOs;
using WaterApp.Application.Interfaces;

namespace WaterApp.API.Controllers;

// Rate-limited more strictly than the rest of the API (see Program.cs's
// "auth" policy) — these endpoints are the highest-value target for
// brute-force login attempts, credential stuffing, and registration/SMS
// spam, on top of the generous global limiter every endpoint already gets.
[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        try
        {
            var result = await _authService.RegisterAsync(request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // Protected admin-creation endpoint.
    // Requires header: X-Admin-Key: <value of ADMIN_SEED_KEY env var>
    // Use this once to seed your first Admin user, then treat the key as a secret
    // (rotate it in Railway if you suspect it's leaked).
    [HttpPost("register-admin")]
    public async Task<ActionResult<AuthResponse>> RegisterAdmin(
        RegisterRequest request,
        [FromHeader(Name = "X-Admin-Key")] string? adminKey)
    {
        var expectedKey = Environment.GetEnvironmentVariable("ADMIN_SEED_KEY");

        if (string.IsNullOrEmpty(expectedKey))
            return StatusCode(503, new { message = "ADMIN_SEED_KEY is not configured on the server." });

        if (string.IsNullOrEmpty(adminKey) || adminKey != expectedKey)
            return Unauthorized(new { message = "Invalid or missing admin key." });

        try
        {
            var result = await _authService.RegisterAdminAsync(request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        try
        {
            var result = await _authService.LoginAsync(request);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    // Always responds the same way whether or not the phone number is
    // registered, so this can't be used to check which phone numbers have
    // accounts — see AuthService.ForgotPasswordAsync, which enforces the
    // per-phone cooldown before it even looks the number up, precisely so
    // that a Conflict response here can't itself leak registration status.
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request)
    {
        try
        {
            await _authService.ForgotPasswordAsync(request);
            return Ok(new { message = "If that phone number is registered, we've sent a verification code." });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPost("reset-password")]
    public async Task<ActionResult<AuthResponse>> ResetPassword(ResetPasswordRequest request)
    {
        try
        {
            var result = await _authService.ResetPasswordAsync(request);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // Exchanges a still-valid refresh token for a new access/refresh pair.
    // Called automatically by the app's HTTP client whenever a request
    // fails with 401 due to an expired access token — this is what lets a
    // session survive past the 60-minute access token lifetime without the
    // user having to log back in.
    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh(RefreshRequest request)
    {
        try
        {
            var result = await _authService.RefreshAsync(request.RefreshToken);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    // Revokes a refresh token on logout. Deliberately not gated behind
    // [Authorize] — a caller whose *access* token already expired still
    // needs to be able to clean up their (still valid) refresh token, and
    // the refresh token itself is the proof of identity this needs.
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshRequest request)
    {
        await _authService.RevokeRefreshTokenAsync(request.RefreshToken);
        return NoContent();
    }
}

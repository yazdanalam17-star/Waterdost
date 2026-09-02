using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WaterApp.Application.DTOs;
using WaterApp.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using WaterApp.Domain.Entities;
using WaterApp.Domain.Enums;
using WaterApp.Infrastructure.Data;

namespace WaterApp.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokenService;
    private readonly ISmsSender _smsSender;
    private readonly IEmailSender _emailSender;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _config;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        AppDbContext db,
        IPasswordHasher hasher,
        ITokenService tokenService,
        ISmsSender smsSender,
        IEmailSender emailSender,
        IMemoryCache cache,
        IConfiguration config,
        ILogger<AuthService> logger)
    {
        _db = db;
        _hasher = hasher;
        _tokenService = tokenService;
        _smsSender = smsSender;
        _emailSender = emailSender;
        _cache = cache;
        _logger = logger;
        _config = config;
    }

    // Public self-registration. Admin accounts can NEVER be created through this path,
    // even if a caller sends Role: 2 in the request body.
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        if (request.Role == UserRole.Admin)
            throw new InvalidOperationException("Admin accounts cannot be created via public registration.");

        return await CreateUserAsync(request, request.Role);
    }

    // Only reachable via the key-protected /api/auth/register-admin endpoint.
    // Forces Admin role regardless of what the caller passed in Role.
    public async Task<AuthResponse> RegisterAdminAsync(RegisterRequest request)
    {
        return await CreateUserAsync(request, UserRole.Admin);
    }

    private async Task<AuthResponse> CreateUserAsync(RegisterRequest request, UserRole role)
    {
        var exists = await _db.Users.AnyAsync(u => u.Phone == request.Phone);
        if (exists)
            throw new InvalidOperationException("A user with this phone number already exists.");

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            throw new ArgumentException("Password must be at least 8 characters.");

        var user = new User
        {
            Name = request.Name,
            Phone = request.Phone,
            Email = request.Email,
            PasswordHash = _hasher.Hash(request.Password),
            Role = role
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var access = _tokenService.GenerateAccessToken(user);
        var refresh = await IssueRefreshTokenAsync(user.Id);

        return new AuthResponse(user.Id, user.Name, user.Role, access, refresh);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Phone == request.Phone)
            ?? throw new UnauthorizedAccessException("Invalid phone number or password.");

        if (!_hasher.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Invalid phone number or password.");

        if (!user.IsActive)
            throw new UnauthorizedAccessException("This account has been deactivated. Please contact support.");

        var access = _tokenService.GenerateAccessToken(user);
        var refresh = await IssueRefreshTokenAsync(user.Id);

        return new AuthResponse(user.Id, user.Name, user.Role, access, refresh);
    }

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Phone))
            throw new ArgumentException("Phone number is required.");

        var phone = request.Phone.Trim();
        var cooldownKey = $"forgot-password:{phone}";

        // Checked — and set — before the user lookup below, and identically
        // regardless of whether the phone turns out to be registered.
        // Otherwise a Conflict on retry would itself reveal that an
        // account exists, defeating the point of responding the same way
        // either way further down.
        if (_cache.TryGetValue(cooldownKey, out _))
            throw new InvalidOperationException("Please wait a minute before requesting another code.");

        _cache.Set(cooldownKey, true, TimeSpan.FromSeconds(60));

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Phone == phone);
        if (user is null || !user.IsActive)
            return; // Same outward response as the success path below.

        // A new code invalidates any earlier one for this user.
        var oldCodes = await _db.PasswordResetOtps.Where(o => o.UserId == user.Id).ToListAsync();
        _db.PasswordResetOtps.RemoveRange(oldCodes);

        var code = RandomNumberGenerator.GetInt32(100_000, 1_000_000).ToString();

        _db.PasswordResetOtps.Add(new PasswordResetOtp
        {
            UserId = user.Id,
            CodeHash = _hasher.Hash(code),
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        });

        await _db.SaveChangesAsync();

        var message = $"Your Ghartak verification code is {code}. It expires in 10 minutes. Don't share this code with anyone.";

        // Email preferred when available: it isn't subject to India's
        // carrier-level SMS/DLT filtering, which silently drops SMS from
        // unregistered sender templates regardless of what the SMS
        // gateway's own API reports (see ISmsSender — Brevo can return a
        // clean 200 while the carrier discards the message). Falls back
        // to SMS if the user has no email on file, or if the email send
        // itself fails for any reason.
        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            try
            {
                await _emailSender.SendAsync(user.Email, user.Name, "Your Ghartak verification code", message);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Email OTP delivery failed for user {UserId}, falling back to SMS.", user.Id);
            }
        }

        await _smsSender.SendAsync(user.Phone, message);
    }

    public async Task<AuthResponse> ResetPasswordAsync(ResetPasswordRequest request)
    {
        // Checked first so a too-short password never burns one of the
        // limited OTP verification attempts below.
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
            throw new ArgumentException("Password must be at least 8 characters.");

        // Deliberately the same message for "no such phone", "no code
        // requested", and "wrong code" — distinguishing them would leak
        // which phone numbers are registered.
        const string genericError = "Invalid or expired code.";

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Phone == request.Phone.Trim())
            ?? throw new UnauthorizedAccessException(genericError);

        var otp = await _db.PasswordResetOtps
            .Where(o => o.UserId == user.Id)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();

        if (otp is null || otp.ExpiresAt < DateTime.UtcNow)
            throw new UnauthorizedAccessException(genericError);

        if (otp.Attempts >= 5)
        {
            _db.PasswordResetOtps.Remove(otp);
            await _db.SaveChangesAsync();
            throw new UnauthorizedAccessException("Too many incorrect attempts. Please request a new code.");
        }

        if (!_hasher.Verify(request.Code, otp.CodeHash))
        {
            otp.Attempts += 1;
            await _db.SaveChangesAsync();
            throw new UnauthorizedAccessException(genericError);
        }

        user.PasswordHash = _hasher.Hash(request.NewPassword);
        _db.PasswordResetOtps.Remove(otp);
        await _db.SaveChangesAsync();

        // Signs the user straight in — there's no account left to send them
        // back to a login screen for.
        var access = _tokenService.GenerateAccessToken(user);
        var refresh = await IssueRefreshTokenAsync(user.Id);
        return new AuthResponse(user.Id, user.Name, user.Role, access, refresh);
    }

    public async Task<AuthResponse> RefreshAsync(string refreshToken)
    {
        const string genericError = "Invalid or expired session. Please log in again.";

        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new UnauthorizedAccessException(genericError);

        var hash = HashToken(refreshToken);
        var stored = await _db.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.TokenHash == hash);

        if (stored is null || stored.User is null || stored.RevokedAt is not null || stored.ExpiresAt < DateTime.UtcNow)
            throw new UnauthorizedAccessException(genericError);

        if (!stored.User.IsActive)
            throw new UnauthorizedAccessException("This account has been deactivated. Please contact support.");

        // Rotation: this token is single-use. Revoking it here (rather than
        // deleting it) means a second attempt to reuse it — e.g. a stolen
        // copy replayed after the legitimate client already refreshed —
        // fails as "revoked" instead of silently succeeding twice.
        stored.RevokedAt = DateTime.UtcNow;

        var access = _tokenService.GenerateAccessToken(stored.User);
        var newRefresh = await IssueRefreshTokenAsync(stored.User.Id);

        await _db.SaveChangesAsync();

        return new AuthResponse(stored.User.Id, stored.User.Name, stored.User.Role, access, newRefresh);
    }

    public async Task RevokeRefreshTokenAsync(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return;

        var hash = HashToken(refreshToken);
        var stored = await _db.RefreshTokens.FirstOrDefaultAsync(rt => rt.TokenHash == hash);
        if (stored is not null && stored.RevokedAt is null)
        {
            stored.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }

    // Issues and persists a new refresh token, returning the raw value for
    // the caller to send back to the client. Only the SHA-256 hash is ever
    // stored — a deterministic hash (not bcrypt) is deliberate here, since
    // RefreshAsync/RevokeRefreshTokenAsync need to look a token up by exact
    // match, which a randomly-salted hash can't support.
    private async Task<string> IssueRefreshTokenAsync(Guid userId)
    {
        var raw = _tokenService.GenerateRefreshToken();
        var days = int.TryParse(_config["Jwt:RefreshTokenDays"], out var d) ? d : 30;

        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            TokenHash = HashToken(raw),
            ExpiresAt = DateTime.UtcNow.AddDays(days)
        });

        await _db.SaveChangesAsync();
        return raw;
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

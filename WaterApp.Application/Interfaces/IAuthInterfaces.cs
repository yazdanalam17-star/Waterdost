using WaterApp.Application.DTOs;
using WaterApp.Domain.Entities;

namespace WaterApp.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request);
    Task<AuthResponse> RegisterAdminAsync(RegisterRequest request);
    Task<AuthResponse> LoginAsync(LoginRequest request);
    Task ForgotPasswordAsync(ForgotPasswordRequest request);
    Task<AuthResponse> ResetPasswordAsync(ResetPasswordRequest request);
    Task<AuthResponse> RefreshAsync(string refreshToken);
    Task RevokeRefreshTokenAsync(string refreshToken);
}

public interface ITokenService
{
    string GenerateAccessToken(User user);
    string GenerateRefreshToken();
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

// Provider-agnostic SMS sending, used for the forgot-password OTP flow.
// Swap in any gateway (Twilio, MSG91, Fast2SMS, AWS SNS, ...) by
// implementing this — see TwilioSmsSender for a ready-to-configure
// example and LoggingSmsSender for local development without a real
// provider.
public interface ISmsSender
{
    Task SendAsync(string phoneNumber, string message);
}

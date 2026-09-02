using System.ComponentModel.DataAnnotations;
using WaterApp.Domain.Enums;

namespace WaterApp.Application.DTOs;

public record RegisterRequest(
    [Required, StringLength(100, MinimumLength = 1)] string Name,
    [Required, RegularExpression(@"^\+?[0-9]{7,15}$", ErrorMessage = "Enter a valid phone number.")] string Phone,
    [Required, EmailAddress] string Email,
    [Required, MinLength(8, ErrorMessage = "Password must be at least 8 characters.")] string Password,
    UserRole Role
);

public record LoginRequest(
    [Required] string Phone,
    [Required] string Password
);

public record AuthResponse(Guid UserId, string Name, UserRole Role, string AccessToken, string RefreshToken);

public record ForgotPasswordRequest([Required] string Phone);

public record ResetPasswordRequest(
    [Required] string Phone,
    [Required, StringLength(6, MinimumLength = 6)] string Code,
    [Required, MinLength(8, ErrorMessage = "Password must be at least 8 characters.")] string NewPassword
);

public record RefreshRequest([Required] string RefreshToken);

using System.ComponentModel.DataAnnotations;

namespace WaterApp.Application.DTOs;

public record ProfileDto(Guid UserId, string Name, string Phone, string? Email, string Role);

public record UpdateProfileRequest(
    [Required, StringLength(100, MinimumLength = 1)] string Name,
    [EmailAddress] string? Email
);

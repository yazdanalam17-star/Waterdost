using System.ComponentModel.DataAnnotations;

namespace WaterApp.Application.DTOs;

public record RegisterPushTokenRequest([Required] string Token);

public record NotificationDto(Guid Id, string Title, string Body, bool IsRead, DateTime CreatedAt);

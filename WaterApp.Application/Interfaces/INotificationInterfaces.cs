using WaterApp.Application.DTOs;

namespace WaterApp.Application.Interfaces;

public interface INotificationService
{
    // ---- Device push tokens ----
    Task RegisterTokenAsync(Guid userId, string token);
    Task UnregisterTokenAsync(string token);

    // ---- Sending (called by other services on order/status events) ----
    // Persists a Notification row for the user and best-effort pushes it to
    // every device they've registered. Never throws — a push failure should
    // never take down the order/status change that triggered it.
    Task NotifyUserAsync(Guid userId, string title, string body);

    // ---- In-app notification list (buyer/seller notification center) ----
    Task<List<NotificationDto>> GetMyNotificationsAsync(Guid userId);
    Task MarkReadAsync(Guid userId, Guid notificationId);
    Task MarkAllReadAsync(Guid userId);
}

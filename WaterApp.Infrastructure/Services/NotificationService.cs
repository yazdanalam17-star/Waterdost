using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WaterApp.Application.DTOs;
using WaterApp.Application.Interfaces;
using WaterApp.Domain.Entities;
using WaterApp.Infrastructure.Data;

namespace WaterApp.Infrastructure.Services;

// Sends order/status-change notifications two ways at once: an in-app
// Notification row (always written — this is the source of truth the
// buyer/seller can check even if the push never arrives) and a best-effort
// push through Expo's push service, which needs no FCM/APNs credentials of
// our own since Expo's push tokens route through their infrastructure.
public class NotificationService : INotificationService
{
    private const string ExpoPushEndpoint = "https://exp.host/--/api/v2/push/send";

    private readonly AppDbContext _db;
    private readonly HttpClient _httpClient;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(AppDbContext db, HttpClient httpClient, ILogger<NotificationService> logger)
    {
        _db = db;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task RegisterTokenAsync(Guid userId, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("Push token is required.");

        var trimmed = token.Trim();

        // A token identifies one device. If it was previously registered to
        // a different account on this same device (shared device, or a
        // different user logging in after this one logged out), move it
        // over rather than erroring or leaving a duplicate.
        var existing = await _db.PushTokens.FirstOrDefaultAsync(t => t.Token == trimmed);
        if (existing is not null)
        {
            existing.UserId = userId;
        }
        else
        {
            _db.PushTokens.Add(new PushToken { UserId = userId, Token = trimmed });
        }

        await _db.SaveChangesAsync();
    }

    public async Task UnregisterTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return;

        var existing = await _db.PushTokens.FirstOrDefaultAsync(t => t.Token == token.Trim());
        if (existing is not null)
        {
            _db.PushTokens.Remove(existing);
            await _db.SaveChangesAsync();
        }
    }

    public async Task NotifyUserAsync(Guid userId, string title, string body)
    {
        // The in-app list is the source of truth: write it first, and
        // independently of whether push delivery below succeeds.
        _db.Notifications.Add(new Notification { UserId = userId, Title = title, Body = body });

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // A notification failure must never break the caller's actual
            // operation (placing an order, updating its status, etc).
            _logger.LogError(ex, "Failed to save notification for user {UserId}", userId);
            return;
        }

        try
        {
            var tokens = await _db.PushTokens
                .Where(t => t.UserId == userId)
                .Select(t => t.Token)
                .ToListAsync();

            if (tokens.Count == 0)
                return;

            var messages = tokens
                .Select(token => new ExpoPushMessage(token, title, body))
                .ToList();

            var json = JsonSerializer.Serialize(messages);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(ExpoPushEndpoint, content);

            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                _logger.LogWarning(
                    "Expo push send returned {StatusCode} for user {UserId}: {Body}",
                    response.StatusCode, userId, responseBody);
            }
        }
        catch (Exception ex)
        {
            // Best-effort only — the Notification row above already saved,
            // so the user still sees this in-app even if the push itself
            // never lands (no network, invalid/stale token, Expo outage).
            _logger.LogError(ex, "Failed to send push notification to user {UserId}", userId);
        }
    }

    public async Task<List<NotificationDto>> GetMyNotificationsAsync(Guid userId)
    {
        return await _db.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(100)
            .Select(n => new NotificationDto(n.Id, n.Title, n.Body, n.IsRead, n.CreatedAt))
            .ToListAsync();
    }

    public async Task MarkReadAsync(Guid userId, Guid notificationId)
    {
        var notification = await _db.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId)
            ?? throw new KeyNotFoundException("Notification not found.");

        notification.IsRead = true;
        await _db.SaveChangesAsync();
    }

    public async Task MarkAllReadAsync(Guid userId)
    {
        await _db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(setters => setters.SetProperty(n => n.IsRead, true));
    }

    private record ExpoPushMessage(
        [property: JsonPropertyName("to")] string To,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("body")] string Body,
        [property: JsonPropertyName("sound")] string Sound = "default");
}

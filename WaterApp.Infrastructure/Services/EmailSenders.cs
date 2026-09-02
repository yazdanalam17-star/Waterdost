using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WaterApp.Application.Interfaces;

namespace WaterApp.Infrastructure.Services;

// Sends transactional email via Brevo's Messaging API — the same account
// already used for SMS (see SmsSenders.cs), since Brevo started as an
// email platform (Sendinblue) before adding SMS, and one API key generally
// covers both products. Configure with:
//   Email:SenderEmail = <a sender email VERIFIED in your Brevo account —
//                        see Brevo dashboard > Senders. Required; Brevo
//                        rejects sends from unverified senders.>
//   Email:SenderName  = display name, defaults to "Ghartak"
//   Email:BrevoApiKey = optional; falls back to Sms:BrevoApiKey if unset,
//                        since it's normally the same account/key.
// Program.cs only registers this when Email:SenderEmail is present;
// otherwise NullEmailSender is used instead, so a missing config just
// means forgot-password falls back to SMS rather than the app breaking.
public class BrevoEmailSender : IEmailSender
{
    private const string Endpoint = "https://api.brevo.com/v3/smtp/email";

    private readonly HttpClient _httpClient;
    private readonly ILogger<BrevoEmailSender> _logger;
    private readonly string _senderEmail;
    private readonly string _senderName;

    public BrevoEmailSender(HttpClient httpClient, IConfiguration config, ILogger<BrevoEmailSender> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var apiKey = config["Email:BrevoApiKey"] ?? config["Sms:BrevoApiKey"]
            ?? throw new InvalidOperationException("Email:BrevoApiKey (or Sms:BrevoApiKey) is not configured.");
        _senderEmail = config["Email:SenderEmail"]
            ?? throw new InvalidOperationException("Email:SenderEmail is not configured.");
        _senderName = config["Email:SenderName"] ?? "Ghartak";

        _httpClient.DefaultRequestHeaders.Add("api-key", apiKey);
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task SendAsync(string toEmail, string toName, string subject, string textContent)
    {
        var payload = new BrevoEmailRequest(
            Sender: new BrevoEmailAddress(_senderEmail, _senderName),
            To: new[] { new BrevoEmailAddress(toEmail, toName) },
            Subject: subject,
            TextContent: textContent
        );

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(Endpoint, content);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            _logger.LogError("Brevo email send failed ({StatusCode}): {Body}", response.StatusCode, body);
            throw new InvalidOperationException("Couldn't send the verification email.");
        }
    }

    private record BrevoEmailAddress(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("name")] string Name);

    private record BrevoEmailRequest(
        [property: JsonPropertyName("sender")] BrevoEmailAddress Sender,
        [property: JsonPropertyName("to")] BrevoEmailAddress[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("textContent")] string TextContent);
}

// Used when no email sender is configured — ForgotPasswordAsync catches
// the failure and falls back to SMS, so this isn't a dead end, just a
// signal that email isn't available as a channel yet.
public class NullEmailSender : IEmailSender
{
    private readonly ILogger<NullEmailSender> _logger;

    public NullEmailSender(ILogger<NullEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(string toEmail, string toName, string subject, string textContent)
    {
        _logger.LogWarning(
            "No email sender configured (set Email:SenderEmail). Falling back to SMS for {ToEmail}.", toEmail);
        throw new InvalidOperationException("Email sending is not configured.");
    }
}

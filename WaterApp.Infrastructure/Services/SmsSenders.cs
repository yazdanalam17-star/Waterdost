using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WaterApp.Application.Interfaces;

namespace WaterApp.Infrastructure.Services;

// Sends OTP/verification SMS via Twilio's REST API (plain HttpClient, no
// Twilio SDK dependency — just Basic Auth + a form-encoded POST). Configure
// with:
//   Sms:Provider = "Twilio"
//   Sms:AccountSid, Sms:AuthToken, Sms:FromNumber
// See BrevoSmsSender below for the currently-active provider. Both
// implement the same ISmsSender interface, so switching providers is just
// a Program.cs DI change — nothing else in the app needs to know or care
// which one is active.
public class TwilioSmsSender : ISmsSender
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TwilioSmsSender> _logger;
    private readonly string _accountSid;
    private readonly string _fromNumber;

    public TwilioSmsSender(HttpClient httpClient, IConfiguration config, ILogger<TwilioSmsSender> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        _accountSid = config["Sms:AccountSid"]
            ?? throw new InvalidOperationException("Sms:AccountSid is not configured.");
        var authToken = config["Sms:AuthToken"]
            ?? throw new InvalidOperationException("Sms:AuthToken is not configured.");
        _fromNumber = config["Sms:FromNumber"]
            ?? throw new InvalidOperationException("Sms:FromNumber is not configured.");

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_accountSid}:{authToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task SendAsync(string phoneNumber, string message)
    {
        var url = $"https://api.twilio.com/2010-04-01/Accounts/{_accountSid}/Messages.json";
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["To"] = phoneNumber,
            ["From"] = _fromNumber,
            ["Body"] = message
        });

        using var response = await _httpClient.PostAsync(url, form);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            _logger.LogError("Twilio SMS send failed ({StatusCode}): {Body}", response.StatusCode, body);
            throw new InvalidOperationException("Couldn't send the verification code. Please try again in a moment.");
        }
    }
}

// Local-development fallback used when no SMS provider is configured —
// logs the code instead of sending a real message, so the forgot-password
// flow is testable without a real provider account. Program.cs picks this
// automatically whenever no SMS provider's settings are present.
public class LoggingSmsSender : ISmsSender
{
    private readonly ILogger<LoggingSmsSender> _logger;

    public LoggingSmsSender(ILogger<LoggingSmsSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(string phoneNumber, string message)
    {
        _logger.LogWarning(
            "No SMS provider configured (set Sms:Provider plus that provider's settings). " +
            "Would have sent to {PhoneNumber}: {Message}",
            phoneNumber, message);
        return Task.CompletedTask;
    }
}

// Sends OTP/verification SMS via Brevo's (formerly Sendinblue) transactional
// SMS API. Configure with:
//   Sms:Provider = "Brevo"
//   Sms:BrevoApiKey = <your Brevo API key, from Settings > SMTP & API>
//   Sms:SenderName = <approved sender ID, e.g. "Ghartak" — max 11
//                     alphanumeric characters, or 15 if numeric>
// Program.cs only registers this class when Sms:Provider is "Brevo" and
// Sms:BrevoApiKey is present.
public class BrevoSmsSender : ISmsSender
{
    private const string Endpoint = "https://api.brevo.com/v3/transactionalSMS/send";

    private readonly HttpClient _httpClient;
    private readonly ILogger<BrevoSmsSender> _logger;
    private readonly string _senderName;

    public BrevoSmsSender(HttpClient httpClient, IConfiguration config, ILogger<BrevoSmsSender> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var apiKey = config["Sms:BrevoApiKey"]
            ?? throw new InvalidOperationException("Sms:BrevoApiKey is not configured.");
        _senderName = config["Sms:SenderName"] ?? "Ghartak";

        // Brevo authenticates via a plain "api-key" header — not Bearer,
        // not Basic Auth (unlike Twilio).
        _httpClient.DefaultRequestHeaders.Add("api-key", apiKey);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task SendAsync(string phoneNumber, string message)
    {
        var payload = new BrevoSmsRequest(
            Sender: _senderName,
            Recipient: NormalizePhoneNumber(phoneNumber),
            Content: message,
            Type: "transactional", // lowercase required — Brevo rejects "Transactional" with 400 invalid_parameter
            Tag: "password-reset-otp"
        );

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(Endpoint, content);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            _logger.LogError("Brevo SMS send failed ({StatusCode}): {Body}", response.StatusCode, body);
            throw new InvalidOperationException("Couldn't send the verification code. Please try again in a moment.");
        }
    }

    // Brevo expects the recipient's number to include its country code with
    // no leading '+' (their own docs show e.g. "33680065433" for a French
    // number). RegisterRequest's phone validation allows an optional '+',
    // so this strips one if present. It also assumes a bare 10-digit number
    // starting 6-9 (a phone typed without any country code — common when
    // people enter just their own number) is an Indian mobile number and
    // prepends "91", since that's the realistic default for this app;
    // numbers that already include a country code are left untouched.
    private static string NormalizePhoneNumber(string phoneNumber)
    {
        var trimmed = phoneNumber.TrimStart('+').Trim();
        if (trimmed.Length == 10 && trimmed[0] is >= '6' and <= '9')
            return "91" + trimmed;
        return trimmed;
    }

    private record BrevoSmsRequest(
        [property: JsonPropertyName("sender")] string Sender,
        [property: JsonPropertyName("recipient")] string Recipient,
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("tag")] string Tag);
}

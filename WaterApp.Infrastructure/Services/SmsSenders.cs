using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WaterApp.Application.Interfaces;

namespace WaterApp.Infrastructure.Services;

// Sends OTP/verification SMS via Twilio's REST API (plain HttpClient, no
// Twilio SDK dependency — just Basic Auth + a form-encoded POST). Configure
// with environment variables or appsettings:
//   Sms:AccountSid, Sms:AuthToken, Sms:FromNumber
// Program.cs only registers this class when all three are present. Not
// wedded to Twilio specifically — swap in MSG91/Fast2SMS/AWS SNS/etc. by
// writing another ISmsSender and registering it instead; nothing else in
// the app needs to change.
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
// flow is testable without a Twilio (or other) account. Program.cs picks
// this automatically whenever the Sms:* settings are absent.
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
            "No SMS provider configured (set Sms:AccountSid / Sms:AuthToken / Sms:FromNumber). " +
            "Would have sent to {PhoneNumber}: {Message}",
            phoneNumber, message);
        return Task.CompletedTask;
    }
}

namespace WaterApp.Application.Interfaces;

// Provider-agnostic transactional email sending — same design as
// ISmsSender. Used as the preferred channel for the forgot-password OTP
// when a user has an email on file, since email delivery isn't subject to
// India's carrier-level SMS/DLT filtering that blocks ISmsSender sends for
// many Indian numbers regardless of what the SMS gateway's API reports.
public interface IEmailSender
{
    Task SendAsync(string toEmail, string toName, string subject, string textContent);
}

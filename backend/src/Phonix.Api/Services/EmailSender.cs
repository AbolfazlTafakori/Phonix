using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using Phonix.Api.Data;

namespace Phonix.Api.Services;

// Sends mail through the SMTP server configured in the admin panel. Until that's set up the
// message is only written to the application log, so the rest of the app keeps working.
public class EmailSender : IEmailSender
{
    private readonly StoreData _store;
    private readonly ILogger<EmailSender> _logger;

    public EmailSender(StoreData store, ILogger<EmailSender> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task<bool> SendAsync(string to, string subject, string body, string? htmlBody = null)
    {
        if (string.IsNullOrWhiteSpace(to))
        {
            _logger.LogWarning("Email skipped: recipient has no address. Subject: {Subject}", subject);
            return false;
        }

        var settings = _store.GetEmailSettings();
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.Host))
        {
            _logger.LogInformation("EMAIL (SMTP not configured, not sent) → {To} | {Subject}\n{Body}", to, subject, body);
            return false;
        }

        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(settings.FromEmail, settings.FromName),
                Subject = subject,
                Body = body,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8,
                IsBodyHtml = false,
            };
            message.To.Add(to);
            // Attach the HTML alternative so clients that support it render the branded version,
            // while plain-text clients still get a readable message.
            if (!string.IsNullOrWhiteSpace(htmlBody))
            {
                var htmlView = AlternateView.CreateAlternateViewFromString(htmlBody, Encoding.UTF8, MediaTypeNames.Text.Html);
                message.AlternateViews.Add(htmlView);
            }

#pragma warning disable SYSLIB0014 // SmtpClient is adequate for standard SMTP delivery here.
            using var client = new SmtpClient(settings.Host, settings.Port)
            {
                EnableSsl = settings.UseSsl,
                Credentials = new NetworkCredential(settings.Username, settings.Password),
            };
#pragma warning restore SYSLIB0014
            await client.SendMailAsync(message);
            _logger.LogInformation("Email sent to {To}: {Subject}", to, subject);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To}", to);
            return false;
        }
    }
}

namespace Phonix.Api.Services;

public interface IEmailSender
{
    // When htmlBody is supplied the message is sent as multipart (plain text + HTML); otherwise plain text only.
    Task<bool> SendAsync(string to, string subject, string body, string? htmlBody = null);
}

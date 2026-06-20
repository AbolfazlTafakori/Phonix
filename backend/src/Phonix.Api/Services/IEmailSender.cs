namespace Phonix.Api.Services;

public interface IEmailSender
{
    Task<bool> SendAsync(string to, string subject, string body);
}

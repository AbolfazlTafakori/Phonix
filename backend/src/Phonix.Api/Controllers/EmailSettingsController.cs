using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;
using Phonix.Api.Services;

namespace Phonix.Api.Controllers;

public record TestEmailInput(string To);

// What the panel is allowed to know about the outbound mail configuration. The SMTP password is absent BY
// TYPE — only whether one is stored — mirroring MailboxSettingsDto, so no future endpoint can leak it by
// forgetting to strip it.
public sealed record EmailSettingsDto(
    bool Enabled, string Host, int Port, string Username,
    string FromEmail, string FromName, bool UseSsl, bool HasPassword);

// Incoming settings. Password is optional: empty means "leave the stored one alone", which is what lets the
// form round-trip without the browser ever holding the real value.
public sealed record EmailSettingsInput(
    bool Enabled, string? Host, int Port, string? Username,
    string? FromEmail, string? FromName, bool UseSsl, string? Password);

[ApiController]
[Route("api/email-settings")]
[Authorize(Roles = nameof(UserRole.Admin))]
public class EmailSettingsController : ControllerBase
{
    private readonly IDataStore _store;
    private readonly IEmailSender _email;

    public EmailSettingsController(IDataStore store, IEmailSender email)
    {
        _store = store;
        _email = email;
    }

    private static EmailSettingsDto ToDto(EmailSettings s) => new(
        s.Enabled, s.Host, s.Port, s.Username, s.FromEmail, s.FromName, s.UseSsl,
        HasPassword: !string.IsNullOrEmpty(s.Password));

    // The SMTP password used to be handed back here in full. That put a live credential into the browser on
    // every visit to the settings page — where it sits in memory, in the devtools network log, and in reach
    // of any script that ever runs on the panel origin — for no benefit at all: the form only needs to know
    // whether one is set, and the server already has the value.
    [HttpGet]
    public EmailSettingsDto Get() => ToDto(_store.GetEmailSettings());

    [HttpPut]
    public EmailSettingsDto Update(EmailSettingsInput input)
    {
        var current = _store.GetEmailSettings();
        _store.UpdateEmailSettings(new EmailSettings
        {
            Enabled = input.Enabled,
            Host = (input.Host ?? "").Trim(),
            Port = input.Port,
            Username = (input.Username ?? "").Trim(),
            // Blank means "keep": the client cannot echo back what it was never given.
            Password = string.IsNullOrEmpty(input.Password) ? current.Password : input.Password,
            FromEmail = (input.FromEmail ?? "").Trim(),
            FromName = (input.FromName ?? "").Trim(),
            UseSsl = input.UseSsl,
        });
        return ToDto(_store.GetEmailSettings());
    }

    [HttpPost("test")]
    public async Task<IActionResult> Test(TestEmailInput input)
    {
        if (string.IsNullOrWhiteSpace(input.To))
            return BadRequest("ایمیل مقصد را وارد کنید.");
        var ok = await _email.SendAsync(input.To.Trim(), "ایمیل آزمایشی فونیکس", "این یک ایمیل آزمایشی است. اگر آن را دریافت کردید، تنظیمات SMTP درست است.");
        return ok ? Ok(new { ok = true }) : BadRequest("ارسال ناموفق بود. Host/Port/گذرواژه و فعال بودن سرویس را بررسی کنید (جزئیات خطا در لاگ سرور).");
    }
}

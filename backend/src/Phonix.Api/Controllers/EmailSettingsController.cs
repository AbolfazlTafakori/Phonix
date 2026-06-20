using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;
using Phonix.Api.Services;

namespace Phonix.Api.Controllers;

public record TestEmailInput(string To);

[ApiController]
[Route("api/email-settings")]
[Authorize(Roles = AuthExtensions.StaffRoles)]
public class EmailSettingsController : ControllerBase
{
    private readonly StoreData _store;
    private readonly IEmailSender _email;

    public EmailSettingsController(StoreData store, IEmailSender email)
    {
        _store = store;
        _email = email;
    }

    [HttpGet]
    public EmailSettings Get() => _store.GetEmailSettings();

    [HttpPut]
    public EmailSettings Update(EmailSettings settings)
    {
        _store.UpdateEmailSettings(settings);
        return _store.GetEmailSettings();
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

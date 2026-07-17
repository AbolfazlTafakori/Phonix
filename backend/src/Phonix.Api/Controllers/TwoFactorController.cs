using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Security;

namespace Phonix.Api.Controllers;

public record TwoFactorCodeInput(string Code);
public record TwoFactorStatusDto(bool Enabled);
public record TwoFactorSetupDto(string Secret, string OtpAuthUri);

[ApiController]
[Route("api/auth/2fa")]
[Authorize]
public class TwoFactorController : ControllerBase
{
    private const string Issuer = "Phoenix Verify";

    private readonly IDataStore _store;
    public TwoFactorController(IDataStore store) => _store = store;

    [HttpGet("status")]
    public ActionResult<TwoFactorStatusDto> Status()
    {
        if (this.CurrentUserId() is not int id) return Unauthorized();
        var user = _store.GetUser(id);
        return user is null ? Unauthorized() : new TwoFactorStatusDto(user.TwoFactorEnabled);
    }

    // Provisions a fresh secret and returns the otpauth URI the client renders as a QR code. The secret is
    // stored pending until the owner confirms a code through Enable.
    [HttpPost("setup")]
    public ActionResult<TwoFactorSetupDto> Setup()
    {
        if (this.CurrentUserId() is not int id) return Unauthorized();
        var user = _store.GetUser(id);
        if (user is null) return Unauthorized();
        // Re-provisioning silently resets the enabled flag (SetTwoFactorSecret), so with 2FA active it would
        // let a hijacked session strip the second factor WITHOUT the current code that Disable demands.
        // An active 2FA must be disabled (code-verified) before a new secret can be issued.
        if (user.TwoFactorEnabled)
            return BadRequest("ابتدا تأیید دومرحله‌ای فعلی را با کد آن غیرفعال کنید، سپس دوباره راه‌اندازی کنید.");

        var secret = TotpService.GenerateSecret();
        if (!_store.SetTwoFactorSecret(id, secret)) return Unauthorized();

        var account = string.IsNullOrWhiteSpace(user.Email) ? user.Username : user.Email;
        return new TwoFactorSetupDto(secret, TotpService.BuildOtpAuthUri(Issuer, account, secret));
    }

    [HttpPost("enable")]
    public IActionResult Enable(TwoFactorCodeInput input)
    {
        if (this.CurrentUserId() is not int id) return Unauthorized();
        var user = _store.GetUser(id);
        if (user is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(user.TwoFactorSecret))
            return BadRequest("ابتدا فرایند راه‌اندازی را آغاز کنید.");
        if (!TotpService.Verify(user.TwoFactorSecret, input.Code ?? ""))
            return BadRequest("کد واردشده نادرست است.");
        _store.SetTwoFactorEnabled(id, true);
        return NoContent();
    }

    [HttpPost("disable")]
    public IActionResult Disable(TwoFactorCodeInput input)
    {
        if (this.CurrentUserId() is not int id) return Unauthorized();
        var user = _store.GetUser(id);
        if (user is null) return Unauthorized();
        if (!user.TwoFactorEnabled) return NoContent();
        if (!TotpService.Verify(user.TwoFactorSecret, input.Code ?? ""))
            return BadRequest("برای غیرفعال‌سازی، کد فعلی را وارد کنید.");
        _store.SetTwoFactorEnabled(id, false);
        return NoContent();
    }
}

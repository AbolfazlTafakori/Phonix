using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Dtos;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Controllers;

public record ProfileUpdateInput(string? Name, string? Phone, string? Username, string? Avatar);
public record ChangePasswordInput(string CurrentPassword, string NewPassword);
public record ChangeEmailInput(string CurrentPassword, string NewEmail);
public record ReferralReportDto(long TotalEarned, int ReferredCount, IReadOnlyList<ReferralEarning> Earnings);

[ApiController]
[Route("api/account")]
[Authorize]
public class AccountController : ControllerBase
{
    private readonly IDataStore _store;
    private readonly ISessionProtector _sessions;
    private readonly Services.IFileStorageService _files;
    private readonly Services.IEmailSender _email;
    public AccountController(IDataStore store, ISessionProtector sessions, Services.IFileStorageService files, Services.IEmailSender email)
    {
        _store = store;
        _sessions = sessions;
        _files = files;
        _email = email;
    }

    private static string FrontendUrl => Environment.GetEnvironmentVariable("PHONIX_FRONTEND_URL") ?? "http://localhost:3000";

    [HttpGet("me")]
    public ActionResult<UserDto> Me()
    {
        if (this.CurrentUserId() is not int id) return Unauthorized();
        var user = _store.GetUser(id);
        return user is null ? Unauthorized() : user.ToDto();
    }

    // a customer may only edit their own contact details, username and avatar — never role, balance, or status.
    // Email is deliberately NOT here — see ChangeEmail/ConfirmEmailChange below: the account's contact address
    // never changes on a single request, only after the new inbox proves itself.
    [HttpPut("me")]
    public ActionResult<UserDto> UpdateMe(ProfileUpdateInput input)
    {
        if (this.CurrentUserId() is not int id) return Unauthorized();
        // username is the login handle + referral code; validate/uniqueness-check before touching anything else.
        // Data is keyed by the immutable Id, so a rename keeps every order/ticket/transaction attached.
        if (input.Username is not null && _store.SetUsername(id, input.Username) is string usernameError)
            return BadRequest(usernameError);
        // Capture the avatar being replaced so its now-orphaned file can be cleaned up after the update.
        var oldAvatar = _store.GetUser(id)?.Avatar;
        var newAvatar = input.Avatar?.Trim();
        var ok = _store.UpdateUser(id, u =>
        {
            if (input.Name is not null) u.Name = input.Name.Trim();
            if (input.Phone is not null) u.Phone = input.Phone.Trim();
            if (input.Avatar is not null) u.Avatar = newAvatar!;
        });
        if (!ok) return Unauthorized();

        // The user swapped in a different avatar → delete the previous one (only if THEY owned it). Done
        // fire-and-forget off the request path; the helper never throws, so the task can never fault.
        if (input.Avatar is not null && !string.Equals(oldAvatar, newAvatar, StringComparison.Ordinal)
            && !string.IsNullOrEmpty(oldAvatar))
            _ = Task.Run(() => _files.DeletePublicImageByUrl(oldAvatar, requireOwner: id));

        return _store.GetUser(id)!.ToDto();
    }

    [HttpGet("transactions")]
    public IEnumerable<Transaction> MyTransactions()
    {
        if (this.CurrentUserId() is not int id) return Enumerable.Empty<Transaction>();
        return _store.GetUserTransactions(id);
    }

    [HttpGet("referrals")]
    public ActionResult<ReferralReportDto> MyReferrals()
    {
        if (this.CurrentUserId() is not int id) return Unauthorized();
        var earnings = _store.GetReferralEarnings(id);
        return new ReferralReportDto(earnings.Sum(e => e.Commission), _store.CountReferredUsers(id), earnings);
    }

    [HttpPut("password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordInput input)
    {
        if (this.CurrentUserId() is not int id) return Unauthorized();
        var user = _store.GetUser(id);
        if (user is null) return Unauthorized();
        if (!PasswordHasher.Verify(input.CurrentPassword ?? "", user.Password))
            return BadRequest("گذرواژه فعلی نادرست است.");
        if (PasswordPolicy.Validate(input.NewPassword) is string error)
            return BadRequest(error);

        var hash = PasswordHasher.Hash(input.NewPassword);
        _store.UpdateUser(id, u => u.Password = hash);
        // Rotate the stamp so every other session (other devices, a leaked cookie) is invalidated, then
        // re-issue a fresh cookie for THIS device so the user who just changed their password stays signed in.
        _store.RotateSecurityStamp(id);
        if (_store.GetUser(id) is { } refreshed)
            AuthCookies.Issue(Response, _sessions.Protect(refreshed, this.IsAdminScope()), Request.IsHttps);

        // Security tripwire: confirm the change to the account owner so an unauthorized change is noticed.
        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            var (text, html) = Services.EmailTemplates.PasswordChanged($"{FrontendUrl}/forgot-password", Services.JalaliDate.NowFa());
            await _email.SendAsync(user.Email, "گذرواژه‌ی حساب فونیکس شما تغییر کرد", text, html);
        }
        return NoContent();
    }

    // ab*****@example.com — enough for the owner to recognise the address, not enough to read it off outright
    // if the (old) mailbox is later exposed. Mirrors UserMailer's MaskCard.
    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 1) return email;
        return $"{email[..2]}{new string('*', Math.Max(at - 2, 3))}{email[at..]}";
    }

    // Step 1: request a change. The account's Email is untouched here — only a confirmation link to the NEW
    // address, so an attacker holding a hijacked session still can't complete a takeover without also
    // controlling that inbox. The OLD address is notified immediately (not on confirm) so the real owner has
    // a chance to react even if they never see the new inbox at all.
    [HttpPost("change-email")]
    public async Task<IActionResult> ChangeEmail(ChangeEmailInput input)
    {
        if (this.CurrentUserId() is not int id) return Unauthorized();
        var user = _store.GetUser(id);
        if (user is null) return Unauthorized();
        if (!PasswordHasher.Verify(input.CurrentPassword ?? "", user.Password))
            return BadRequest("گذرواژه فعلی نادرست است.");

        var newEmail = (input.NewEmail ?? "").Trim();
        if (!InputValidation.IsEmail(newEmail))
            return BadRequest("ایمیل واردشده معتبر نیست.");
        if (string.Equals(newEmail, user.Email, StringComparison.OrdinalIgnoreCase))
            return BadRequest("این ایمیل هم‌اکنون ایمیل حساب شماست.");
        // Preemptive check for a fast error in the settings form; SetEmail re-checks atomically at confirm
        // time too, since the address could be taken by someone else during the hour this link is live.
        if (_store.EmailExists(newEmail))
            return BadRequest("این ایمیل قبلاً برای حساب دیگری ثبت شده است.");

        var token = _store.CreateToken(id, "change-email", TimeSpan.FromHours(1), newEmail);
        var link = $"{FrontendUrl}/confirm-email-change?token={token}";
        var (confirmText, confirmHtml) = Services.EmailTemplates.ConfirmEmailChange(link);
        await _email.SendAsync(newEmail, "تأیید ایمیل جدید حساب فونیکس", confirmText, confirmHtml);

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            var (noticeText, noticeHtml) = Services.EmailTemplates.EmailChangeRequested(MaskEmail(newEmail), $"{FrontendUrl}/account/password");
            await _email.SendAsync(user.Email, "درخواست تغییر ایمیل حساب فونیکس شما", noticeText, noticeHtml);
        }
        return Ok(new { ok = true });
    }

    // Step 2: the new address proves itself by clicking the link. No [Authorize] here on purpose — the token
    // alone carries the authority, exactly like /auth/verify-email and /auth/reset-password, so the link works
    // even from a browser/device that never had a session to begin with.
    [AllowAnonymous]
    [HttpPost("confirm-email-change")]
    public IActionResult ConfirmEmailChange(TokenInput input)
    {
        if (_store.ConsumeTokenWithData(input.Token, "change-email") is not (int userId, string newEmail))
            return BadRequest("لینک تأیید نامعتبر یا منقضی شده است.");
        if (_store.SetEmail(userId, newEmail) is string error)
            return BadRequest(error);
        // The click itself is the proof of ownership SetEmail can't know about — it always resets
        // EmailVerified to false, so flip it back to true for this one, already-proven, case.
        _store.UpdateUser(userId, u => u.EmailVerified = true);
        return Ok(new { ok = true });
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Dtos;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Controllers;

public record ProfileUpdateInput(string? Name, string? Email, string? Phone, string? Username, string? Avatar);
public record ChangePasswordInput(string CurrentPassword, string NewPassword);
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
    [HttpPut("me")]
    public ActionResult<UserDto> UpdateMe(ProfileUpdateInput input)
    {
        if (this.CurrentUserId() is not int id) return Unauthorized();
        // username is the login handle + referral code; validate/uniqueness-check before touching anything else.
        // Data is keyed by the immutable Id, so a rename keeps every order/ticket/transaction attached.
        if (input.Username is not null && _store.SetUsername(id, input.Username) is string usernameError)
            return BadRequest(usernameError);
        // email, like username, must stay unique to one account.
        if (input.Email is not null && _store.SetEmail(id, input.Email) is string emailError)
            return BadRequest(emailError);
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
            var (text, html) = Services.EmailTemplates.PasswordChanged($"{FrontendUrl}/forgot-password", PersianNow());
            await _email.SendAsync(user.Email, "گذرواژه‌ی حساب فونیکس شما تغییر کرد", text, html);
        }
        return NoContent();
    }

    // Persian (Jalali) date + 24h time in Persian digits, e.g. "۱۴۰۴/۰۵/۱۲ — ساعت ۱۴:۳۰".
    private static string PersianNow()
    {
        DateTime now;
        try { now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran")).DateTime; }
        catch { now = DateTime.Now; }
        var pc = new System.Globalization.PersianCalendar();
        var s = $"{pc.GetYear(now):0000}/{pc.GetMonth(now):00}/{pc.GetDayOfMonth(now):00} — ساعت {now:HH:mm}";
        return new string(s.Select(ch => char.IsDigit(ch) ? (char)('۰' + (ch - '0')) : ch).ToArray());
    }
}

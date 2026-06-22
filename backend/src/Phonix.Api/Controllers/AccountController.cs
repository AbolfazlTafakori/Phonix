using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Dtos;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Controllers;

public record ProfileUpdateInput(string? Name, string? Email, string? Phone);
public record ChangePasswordInput(string CurrentPassword, string NewPassword);
public record ReferralReportDto(long TotalEarned, int ReferredCount, IReadOnlyList<ReferralEarning> Earnings);

[ApiController]
[Route("api/account")]
[Authorize]
public class AccountController : ControllerBase
{
    private readonly StoreData _store;
    private readonly ISessionProtector _sessions;
    public AccountController(StoreData store, ISessionProtector sessions)
    {
        _store = store;
        _sessions = sessions;
    }

    [HttpGet("me")]
    public ActionResult<UserDto> Me()
    {
        if (this.CurrentUserId() is not int id) return Unauthorized();
        var user = _store.GetUser(id);
        return user is null ? Unauthorized() : user.ToDto();
    }

    // a customer may only edit their own contact details — never role, balance, or status.
    [HttpPut("me")]
    public ActionResult<UserDto> UpdateMe(ProfileUpdateInput input)
    {
        if (this.CurrentUserId() is not int id) return Unauthorized();
        var ok = _store.UpdateUser(id, u =>
        {
            if (input.Name is not null) u.Name = input.Name.Trim();
            if (input.Email is not null) u.Email = input.Email.Trim();
            if (input.Phone is not null) u.Phone = input.Phone.Trim();
        });
        return ok ? _store.GetUser(id)!.ToDto() : Unauthorized();
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
    public IActionResult ChangePassword(ChangePasswordInput input)
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
            AuthCookies.Issue(Response, _sessions.Protect(refreshed), Request.IsHttps);
        return NoContent();
    }
}

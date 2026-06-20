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
    public AccountController(StoreData store) => _store = store;

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
        var user = this.CurrentUserId() is int id ? _store.GetUser(id) : null;
        if (user is null) return Enumerable.Empty<Transaction>();
        var name = string.IsNullOrWhiteSpace(user.Name) ? user.Username : user.Name;
        return _store.GetTransactions().Where(t => t.UserName == name);
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
        return NoContent();
    }
}

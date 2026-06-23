using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Admin;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Controllers;

public record StaffDto(int Id, string Code, string Name, string Username, string Email, UserRole Role, bool Blocked, bool TwoFactorEnabled, IReadOnlyList<string> Permissions);
public record CreateStaffInput(string Username, UserRole Role, List<string>? Permissions);
public record UpdateStaffInput(string? Name, string? Email, UserRole? Role, bool? Blocked, List<string>? Permissions);
public record StaffPasswordInput(string Password);

// Super-admin only: add staff accounts and grant each a limited set of panel sections. The granted keys
// are validated against the canonical assignable list so a client can never invent a permission.
[ApiController]
[Route("api/staff")]
[Authorize(Roles = nameof(UserRole.Admin))]
public class StaffController : ControllerBase
{
    private readonly StoreData _store;
    private readonly ISessionProtector _sessions;
    public StaffController(StoreData store, ISessionProtector sessions)
    {
        _store = store;
        _sessions = sessions;
    }

    private static StaffDto ToDto(AppUser u) =>
        new(u.Id, u.Code, u.Name, u.Username, u.Email, u.Role, u.Blocked, u.TwoFactorEnabled, u.Permissions);

    private static List<string> CleanPermissions(IEnumerable<string>? requested) =>
        (requested ?? Enumerable.Empty<string>())
            .Where(AdminMenu.AssignableKeys.Contains)
            .Distinct()
            .ToList();

    [HttpGet]
    public IEnumerable<StaffDto> Get() =>
        _store.GetUsers().Where(u => u.Role != UserRole.Customer).Select(ToDto);

    // The sections an Admin can grant, grouped, so the UI checklist stays in sync with the real menu.
    [HttpGet("permissions")]
    public IEnumerable<AdminPermissionInfo> Permissions() => AdminMenu.AssignablePermissions();

    // Grants staff access to an EXISTING user account by username. We don't accept a new email or password
    // here — the person already registered and owns those. The admin only picks WHO (by username) and WHAT
    // (role + sections).
    [HttpPost]
    public ActionResult<StaffDto> Create(CreateStaffInput input)
    {
        if (input.Role is not (UserRole.Admin or UserRole.Support))
            return BadRequest("نقش باید مدیر یا پشتیبان باشد.");

        var result = _store.PromoteToStaff(input.Username ?? "", input.Role, CleanPermissions(input.Permissions));
        if (result.Error is not null) return BadRequest(result.Error);
        return ToDto(result.User!);
    }

    [HttpPut("{id:int}")]
    public ActionResult<StaffDto> Update(int id, UpdateStaffInput input)
    {
        var target = _store.GetUser(id);
        if (target is null || target.Role == UserRole.Customer) return NotFound();
        // An admin can't strip their own admin role or block themselves and get locked out of the panel.
        var isSelf = this.CurrentUserId() == id;
        if (isSelf && ((input.Role is UserRole r && r != UserRole.Admin) || input.Blocked == true))
            return BadRequest("نمی‌توانید نقش یا دسترسی حساب خودتان را محدود کنید.");
        // email is a unique identity handle — reject a value already taken by another account.
        if (input.Email is not null && _store.SetEmail(id, input.Email) is string emailError)
            return BadRequest(emailError);

        _store.UpdateUser(id, u =>
        {
            if (input.Name is not null) u.Name = input.Name.Trim();
            if (input.Role is UserRole role) u.Role = role;
            if (input.Blocked is bool blocked) u.Blocked = blocked;
        });
        // Permissions only apply to a Support account; promoting to Admin clears them (full access).
        var effectiveRole = input.Role ?? target.Role;
        _store.SetUserPermissions(id, effectiveRole == UserRole.Support ? CleanPermissions(input.Permissions) : new List<string>());
        return ToDto(_store.GetUser(id)!);
    }

    [HttpPost("{id:int}/password")]
    public IActionResult ResetPassword(int id, StaffPasswordInput input)
    {
        var target = _store.GetUser(id);
        if (target is null || target.Role == UserRole.Customer) return NotFound();
        if (PasswordPolicy.Validate(input.Password) is string error) return BadRequest(error);
        _store.UpdateUser(id, u => u.Password = PasswordHasher.Hash(input.Password));
        // Rotating the stamp signs the staff member out everywhere so the new password takes hold immediately.
        _store.RotateSecurityStamp(id);
        return NoContent();
    }

    // Owner rescue: turn OFF a staff member's 2FA without their code — for when an admin loses their
    // authenticator and would otherwise be locked out. Clears the secret too, so on their next login the
    // mandatory-setup gate makes them enrol afresh. An admin can't reset their own this way (use the normal
    // self-service disable on the security page) to keep the action deliberate.
    [HttpPost("{id:int}/2fa/disable")]
    public IActionResult DisableTwoFactor(int id)
    {
        var target = _store.GetUser(id);
        if (target is null || target.Role == UserRole.Customer) return NotFound();
        if (this.CurrentUserId() == id) return BadRequest("برای حساب خودتان از صفحه‌ی امنیت استفاده کنید.");
        _store.SetTwoFactorEnabled(id, false);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id)
    {
        var target = _store.GetUser(id);
        if (target is null || target.Role == UserRole.Customer) return NotFound();
        if (this.CurrentUserId() == id) return BadRequest("نمی‌توانید حساب خودتان را حذف کنید.");
        return _store.DeleteUser(id) ? NoContent() : NotFound();
    }
}

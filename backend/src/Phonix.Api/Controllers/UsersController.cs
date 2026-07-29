using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Dtos;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Roles = AuthExtensions.StaffRoles)]
// No class-level [AdminPermission] — this controller mixes ordinary user-management actions ("users") with
// AdjustWallet, which moves real money and belongs to the same "transactions" trust tier as every other
// money-touching endpoint (TransactionsController.Approve/Reject, WithdrawAsync, …). A class-level attribute
// here previously applied to AdjustWallet too, letting anyone holding the routine "users" permission — a
// Support account that only ever manages profiles — mint or drain wallet balance with no receipt, approval,
// or audit trail. Each action below now states its own required permission explicitly.
public class UsersController : ControllerBase
{
    private readonly IDataStore _store;
    private readonly Services.IFileStorageService _files;

    public UsersController(IDataStore store, Services.IFileStorageService files)
    {
        _store = store;
        _files = files;
    }

    // This controller edits ACCOUNTS, and an account's email is the reset-password channel — so writing to one
    // is equivalent to being able to log in as it. That makes "who may be written to here" a privilege
    // boundary, not a data-scope question:
    //   • A Support member holding the routine "users" section could otherwise repoint an Admin's email at an
    //     inbox they control, press "forgot password", and come back as that Admin. Blocking or deleting the
    //     Admin outright was equally available. So staff accounts are Admin-only territory.
    //   • The owner is above Admin (OwnerAccount): payment infrastructure and the V2Ray panel credentials are
    //     gated to it alone. A second Admin editing the owner's row is the same takeover one rung higher, so
    //     nobody but the owner may write to the owner.
    // Returns a deny result, or null when the caller may proceed.
    private ActionResult? GuardTarget(AppUser target)
    {
        if (target.Role != UserRole.Customer && this.CurrentRole() != UserRole.Admin)
            return StatusCode(403, "ویرایش حساب‌های کارکنان فقط توسط مدیر امکان‌پذیر است.");
        if (OwnerAccount.IsOwner(target.Username) && this.CurrentUserId() != target.Id)
            return StatusCode(403, "حساب مالک مجموعه فقط توسط خود او قابل ویرایش است.");
        return null;
    }

    [AdminPermission("users")]
    [HttpGet]
    public IEnumerable<UserDto> Get([FromQuery] string? search, [FromQuery] UserRole? role, [FromQuery] bool? blocked) =>
        _store.GetUsers(search, role, blocked).Select(u => u.ToDto());

    [AdminPermission("users")]
    [HttpGet("page")]
    public PagedResult<UserDto> GetPage([FromQuery] string? search, [FromQuery] UserRole? role,
        [FromQuery] bool? blocked, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        PagedResult<UserDto>.From(_store.GetUsers(search, role, blocked).Select(u => u.ToDto()).ToList(), page, pageSize);

    [AdminPermission("users")]
    [HttpGet("{id:int}")]
    public ActionResult<UserDto> Get(int id)
    {
        var user = _store.GetUser(id);
        return user is null ? NotFound() : user.ToDto();
    }

    [AdminPermission("users")]
    [HttpPut("{id:int}")]
    public ActionResult<UserDto> Update(int id, UserUpdateInput input)
    {
        if (_store.GetUser(id) is not { } target) return NotFound();
        if (GuardTarget(target) is { } denied) return denied;
        // Roles are the panel's privilege boundary and belong to the Admin-only StaffController. Without this
        // guard a Support member holding "users" could hand themselves Admin from here — the role is re-read
        // from the store on every request, so the promotion would take effect on their very next call.
        if (input.Role is not null && this.CurrentRole() != UserRole.Admin)
            return StatusCode(403, "تغییر نقش کاربران فقط توسط مدیر امکان‌پذیر است.");
        // email is a unique identity handle — guard it before the rest of the mutation. Same format rule as
        // signup: it is the user's contact/verification channel, so it can never be blanked or malformed.
        if (input.Email is not null && !InputValidation.IsEmail(input.Email.Trim()))
            return BadRequest("ایمیل واردشده معتبر نیست.");
        if (input.Email is not null && _store.SetEmail(id, input.Email) is string emailError)
            return BadRequest(emailError);
        var ok = _store.UpdateUser(id, u =>
        {
            if (input.Name is not null) u.Name = input.Name;
            if (input.Phone is not null) u.Phone = input.Phone;
            if (input.Role is UserRole role) u.Role = role;
            if (input.Verified is bool verified) u.Verified = verified;
            if (input.Blocked is bool blocked) u.Blocked = blocked;
            if (input.Note is not null) u.Note = input.Note;
        });
        if (!ok) return NotFound();
        // identity tier goes through the dedicated path so a downgrade also revokes the backing card/KYC.
        if (input.VerificationLevel is int level) _store.SetVerificationLevel(id, level);
        return _store.GetUser(id)!.ToDto();
    }

    // Requires "transactions", not "users" — see the class comment. A reason is mandatory and persisted as a
    // Transaction record, so every manual adjustment is attributable (who, when, why), the same audit trail
    // every other wallet-crediting path in this codebase already leaves.
    [AdminPermission("transactions")]
    [HttpPost("{id:int}/wallet")]
    public ActionResult<UserDto> AdjustWallet(int id, WalletInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Reason))
            return BadRequest("دلیل اصلاح موجودی الزامی است.");
        var before = _store.GetUser(id);
        if (before is null) return NotFound();
        var ok = _store.UpdateUser(id, u =>
        {
            u.Wallet += input.Amount;
            if (u.Wallet < 0) u.Wallet = 0;
        });
        if (!ok) return NotFound();
        var after = _store.GetUser(id)!;
        var actor = this.CurrentUserId() is int staffId ? _store.GetUser(staffId)?.Username : null;
        _store.AddTransaction(new Transaction
        {
            UserId = id, UserName = string.IsNullOrWhiteSpace(after.Name) ? after.Username : after.Name,
            Type = TxTypes.AdminAdjustment, Amount = after.Wallet - before.Wallet, Status = TxStatus.Approved,
            Method = "اصلاح دستی", ApprovedVia = actor ?? "admin",
            Description = input.Reason,
        });
        return after.ToDto();
    }

    [AdminPermission("users")]
    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id)
    {
        if (_store.GetUser(id) is not { } target) return NotFound();
        if (GuardTarget(target) is { } denied) return denied;
        // The owner is the account every owner-only section is keyed to, so deleting it is not a recoverable
        // mistake — it frees the configured username for whoever asks next. Refused for everyone, self
        // included; an owner handover is a redeploy with a new PHONIX_OWNER_USERNAME, not a delete button.
        if (OwnerAccount.IsOwner(target.Username))
            return StatusCode(403, "حساب مالک مجموعه قابل حذف نیست.");
        // Read the avatar before removing the account so the orphaned file can be cleaned up afterwards.
        var avatar = target.Avatar;
        if (!_store.DeleteUser(id)) return NotFound();
        // fire-and-forget, owner-guarded, best-effort: account deletion must not wait on (or fail from) disk I/O.
        if (!string.IsNullOrEmpty(avatar))
            _ = Task.Run(() => _files.DeletePublicImageByUrl(avatar, requireOwner: id));
        return NoContent();
    }
}

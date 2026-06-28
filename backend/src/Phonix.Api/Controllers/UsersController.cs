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
[AdminPermission("users")]
public class UsersController : ControllerBase
{
    private readonly IDataStore _store;
    private readonly Services.IFileStorageService _files;

    public UsersController(IDataStore store, Services.IFileStorageService files)
    {
        _store = store;
        _files = files;
    }

    [HttpGet]
    public IEnumerable<UserDto> Get([FromQuery] string? search, [FromQuery] UserRole? role, [FromQuery] bool? blocked) =>
        _store.GetUsers(search, role, blocked).Select(u => u.ToDto());

    [HttpGet("page")]
    public PagedResult<UserDto> GetPage([FromQuery] string? search, [FromQuery] UserRole? role,
        [FromQuery] bool? blocked, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        PagedResult<UserDto>.From(_store.GetUsers(search, role, blocked).Select(u => u.ToDto()).ToList(), page, pageSize);

    [HttpGet("{id:int}")]
    public ActionResult<UserDto> Get(int id)
    {
        var user = _store.GetUser(id);
        return user is null ? NotFound() : user.ToDto();
    }

    [HttpPut("{id:int}")]
    public ActionResult<UserDto> Update(int id, UserUpdateInput input)
    {
        // email is a unique identity handle — guard it before the rest of the mutation.
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

    [HttpPost("{id:int}/wallet")]
    public ActionResult<UserDto> AdjustWallet(int id, WalletInput input)
    {
        var ok = _store.UpdateUser(id, u =>
        {
            u.Wallet += input.Amount;
            if (u.Wallet < 0) u.Wallet = 0;
        });
        return ok ? _store.GetUser(id)!.ToDto() : NotFound();
    }

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id)
    {
        // Read the avatar before removing the account so the orphaned file can be cleaned up afterwards.
        var avatar = _store.GetUser(id)?.Avatar;
        if (!_store.DeleteUser(id)) return NotFound();
        // fire-and-forget, owner-guarded, best-effort: account deletion must not wait on (or fail from) disk I/O.
        if (!string.IsNullOrEmpty(avatar))
            _ = Task.Run(() => _files.DeletePublicImageByUrl(avatar, requireOwner: id));
        return NoContent();
    }
}

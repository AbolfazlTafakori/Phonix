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
public class UsersController : ControllerBase
{
    private readonly StoreData _store;

    public UsersController(StoreData store) => _store = store;

    [HttpGet]
    public IEnumerable<UserDto> Get([FromQuery] string? search, [FromQuery] UserRole? role, [FromQuery] bool? blocked) =>
        _store.GetUsers(search, role, blocked).Select(u => u.ToDto());

    [HttpGet("{id:int}")]
    public ActionResult<UserDto> Get(int id)
    {
        var user = _store.GetUser(id);
        return user is null ? NotFound() : user.ToDto();
    }

    [HttpPut("{id:int}")]
    public ActionResult<UserDto> Update(int id, UserUpdateInput input)
    {
        var ok = _store.UpdateUser(id, u =>
        {
            if (input.Name is not null) u.Name = input.Name;
            if (input.Email is not null) u.Email = input.Email;
            if (input.Phone is not null) u.Phone = input.Phone;
            if (input.Role is UserRole role) u.Role = role;
            if (input.Verified is bool verified) u.Verified = verified;
            if (input.Blocked is bool blocked) u.Blocked = blocked;
            if (input.Note is not null) u.Note = input.Note;
        });
        return ok ? _store.GetUser(id)!.ToDto() : NotFound();
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
    public IActionResult Delete(int id) => _store.DeleteUser(id) ? NoContent() : NotFound();
}

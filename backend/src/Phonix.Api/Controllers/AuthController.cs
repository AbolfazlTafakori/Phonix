using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Dtos;
using Phonix.Api.Models;

namespace Phonix.Api.Controllers;

public record RegisterInput(string Name, string Username, string Email, string Phone, string Password);
public record LoginInput(string Identifier, string Password);

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly StoreData _store;
    public AuthController(StoreData store) => _store = store;

    [HttpPost("register")]
    public ActionResult<UserDto> Register(RegisterInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Username) || string.IsNullOrWhiteSpace(input.Password))
            return BadRequest("نام کاربری و گذرواژه الزامی است.");
        if (string.IsNullOrWhiteSpace(input.Email))
            return BadRequest("ایمیل الزامی است.");
        if (_store.UsernameExists(input.Username.Trim()))
            return Conflict("این نام کاربری قبلاً استفاده شده است.");
        if (_store.EmailExists(input.Email.Trim()))
            return Conflict("این ایمیل قبلاً ثبت شده است.");

        var user = _store.RegisterUser(new AppUser
        {
            Name = string.IsNullOrWhiteSpace(input.Name) ? input.Username.Trim() : input.Name.Trim(),
            Username = input.Username.Trim(),
            Password = input.Password,
            Email = input.Email?.Trim() ?? "",
            Phone = input.Phone?.Trim() ?? "",
        });
        return user.ToDto();
    }

    [HttpPost("login")]
    public ActionResult<UserDto> Login(LoginInput input)
    {
        var user = _store.FindByLogin(input.Identifier?.Trim() ?? "");
        if (user is null || user.Password != input.Password)
            return Unauthorized("نام کاربری یا گذرواژه نادرست است.");
        if (user.Blocked)
            return StatusCode(403, "حساب شما مسدود شده است.");
        return user.ToDto();
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Phonix.Api.Data;
using Phonix.Api.Dtos;
using Phonix.Api.Models;
using Phonix.Api.Security;
using Phonix.Api.Services;

namespace Phonix.Api.Controllers;

public record RegisterInput(string Name, string Username, string Email, string Phone, string Password, string? ReferralCode);
public record LoginInput(string Identifier, string Password);
public record ForgotInput(string Email);
public record TokenInput(string Token);
public record ResetPasswordInput(string Token, string NewPassword);

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController : ControllerBase
{
    private readonly StoreData _store;
    private readonly IEmailSender _email;
    private readonly ISessionProtector _sessions;
    private readonly ILogger<AuthController> _logger;
    public AuthController(StoreData store, IEmailSender email, ISessionProtector sessions, ILogger<AuthController> logger)
    {
        _store = store;
        _email = email;
        _sessions = sessions;
        _logger = logger;
    }

    private string ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static string FrontendUrl => Environment.GetEnvironmentVariable("PHONIX_FRONTEND_URL") ?? "http://localhost:3000";

    private Task SendVerification(AppUser user)
    {
        var token = _store.CreateToken(user.Id, "verify", TimeSpan.FromDays(2));
        var link = $"{FrontendUrl}/verify-email?token={token}";
        var (text, html) = EmailTemplates.VerifyEmail(link);
        return _email.SendAsync(user.Email, "تأیید ایمیل حساب فونیکس", text, html);
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResultDto>> Register(RegisterInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Username) || string.IsNullOrWhiteSpace(input.Password))
            return BadRequest("نام کاربری و گذرواژه الزامی است.");
        if (string.IsNullOrWhiteSpace(input.Email))
            return BadRequest("ایمیل الزامی است.");
        if (PasswordPolicy.Validate(input.Password) is string passwordError)
            return BadRequest(passwordError);
        if (_store.UsernameExists(input.Username.Trim()))
            return Conflict("این نام کاربری قبلاً استفاده شده است.");
        if (_store.EmailExists(input.Email.Trim()))
            return Conflict("این ایمیل قبلاً ثبت شده است.");

        // an optional referral code is a referrer's username; link them so commission can be paid.
        int? referredBy = null;
        if (!string.IsNullOrWhiteSpace(input.ReferralCode))
            referredBy = _store.GetUserByUsername(input.ReferralCode.Trim())?.Id;

        var user = _store.RegisterUser(new AppUser
        {
            Name = string.IsNullOrWhiteSpace(input.Name) ? input.Username.Trim() : input.Name.Trim(),
            Username = input.Username.Trim(),
            Password = PasswordHasher.Hash(input.Password),
            Email = input.Email?.Trim() ?? "",
            Phone = input.Phone?.Trim() ?? "",
            ReferredBy = referredBy,
        });
        await SendVerification(user);
        _logger.LogInformation("New account registered: {Username} (#{UserId}) from {ClientIp}",
            user.Username, user.Id, ClientIp);
        var token = _sessions.Protect(user);
        AuthCookies.Issue(Response, token, Request.IsHttps);
        return new AuthResultDto(token, user.ToDto());
    }

    [HttpPost("forgot")]
    public async Task<IActionResult> Forgot(ForgotInput input)
    {
        // generic response either way: never reveal whether an email exists.
        var user = _store.FindByLogin(input.Email?.Trim() ?? "");
        if (user is not null && !string.IsNullOrWhiteSpace(user.Email))
        {
            var token = _store.CreateToken(user.Id, "reset", TimeSpan.FromHours(1));
            var link = $"{FrontendUrl}/reset-password?token={token}";
            var (text, html) = EmailTemplates.ResetPassword(link);
            await _email.SendAsync(user.Email, "بازنشانی گذرواژه فونیکس", text, html);
        }
        return Ok(new { ok = true });
    }

    [AllowAnonymous]
    [HttpPost("verify-email")]
    public IActionResult VerifyEmail(TokenInput input)
    {
        if (_store.ConsumeToken(input.Token, "verify") is not int userId)
            return BadRequest("لینک تأیید نامعتبر یا منقضی شده است.");
        _store.UpdateUser(userId, u => u.EmailVerified = true);
        return Ok(new { ok = true });
    }

    [Authorize]
    [HttpPost("resend-verification")]
    public async Task<IActionResult> ResendVerification()
    {
        var user = this.CurrentUserId() is int id ? _store.GetUser(id) : null;
        if (user is null) return Unauthorized();
        if (user.EmailVerified) return Ok(new { ok = true });
        await SendVerification(user);
        return Ok(new { ok = true });
    }

    [AllowAnonymous]
    [HttpPost("reset-password")]
    public IActionResult ResetPassword(ResetPasswordInput input)
    {
        if (PasswordPolicy.Validate(input.NewPassword) is string error)
            return BadRequest(error);
        if (_store.ConsumeToken(input.Token, "reset") is not int userId)
            return BadRequest("لینک بازنشانی نامعتبر یا منقضی شده است.");
        var hash = PasswordHasher.Hash(input.NewPassword);
        _store.UpdateUser(userId, u => u.Password = hash);
        // invalidate every existing session for this account — a reset must lock out anyone holding an old token.
        _store.RotateSecurityStamp(userId);
        _logger.LogInformation("Password reset completed for user #{UserId} from {ClientIp}", userId, ClientIp);
        return Ok(new { ok = true });
    }

    [HttpPost("login")]
    public ActionResult<AuthResultDto> Login(LoginInput input)
    {
        var user = _store.FindByLogin(input.Identifier?.Trim() ?? "");
        if (user is null || !PasswordHasher.Verify(input.Password ?? "", user.Password))
        {
            _logger.LogWarning("Failed login for {Identifier} from {ClientIp}",
                input.Identifier?.Trim(), ClientIp);
            return Unauthorized("نام کاربری یا گذرواژه نادرست است.");
        }
        if (user.Blocked)
        {
            _logger.LogWarning("Blocked account login attempt: {Username} (#{UserId}) from {ClientIp}",
                user.Username, user.Id, ClientIp);
            return StatusCode(403, "حساب شما مسدود شده است.");
        }
        var token = _sessions.Protect(user);
        AuthCookies.Issue(Response, token, Request.IsHttps);
        _logger.LogInformation("Login: {Username} (#{UserId}) from {ClientIp}",
            user.Username, user.Id, ClientIp);
        return new AuthResultDto(token, user.ToDto());
    }

    // Sessions are stateless, so logout simply clears the cookie on this device. To force-revoke a token
    // everywhere (e.g. a stolen cookie), change the password — that rotates the security stamp.
    [Authorize]
    [HttpPost("logout")]
    public IActionResult Logout()
    {
        AuthCookies.Clear(Response);
        return NoContent();
    }
}

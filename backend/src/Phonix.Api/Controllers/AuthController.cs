using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using Phonix.Api.Data;
using Phonix.Api.Dtos;
using Phonix.Api.Models;
using Phonix.Api.Security;
using Phonix.Api.Services;

namespace Phonix.Api.Controllers;

public record RegisterInput(string Name, string Username, string Email, string Phone, string Password, string? ReferralCode, string? CaptchaId, string? CaptchaText);
public record LoginInput(string Identifier, string Password, string? CaptchaId, string? CaptchaText, bool? Admin);
// What the admin shell reads to confirm the current session may use the panel (admin-scoped staff).
public record AdminContextDto(int Id, string Name, string Username, UserRole Role);
public record ForgotInput(string Email);
public record TokenInput(string Token);
public record ResetPasswordInput(string Token, string NewPassword);
public record TwoFactorVerifyInput(string Token, string Code);
// A login either completes (Token + User) or stops at the second factor (RequiresTwoFactor + ChallengeToken).
public record LoginResultDto(bool RequiresTwoFactor, string? ChallengeToken, string? Token, UserDto? User);

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController : ControllerBase
{
    private readonly StoreData _store;
    private readonly IEmailSender _email;
    private readonly ISessionProtector _sessions;
    private readonly ITwoFactorChallenge _twoFactor;
    private readonly ITelegramAlertSender _alerts;
    private readonly ICaptchaService _captcha;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AuthController> _logger;
    public AuthController(StoreData store, IEmailSender email, ISessionProtector sessions,
        ITwoFactorChallenge twoFactor, ITelegramAlertSender alerts, ICaptchaService captcha,
        IMemoryCache cache, ILogger<AuthController> logger)
    {
        _store = store;
        _email = email;
        _sessions = sessions;
        _twoFactor = twoFactor;
        _alerts = alerts;
        _captcha = captcha;
        _cache = cache;
        _logger = logger;
    }

    // Per-IP failed-attempt counter (sliding 10-minute window). Once a client crosses the threshold a single
    // warning is emitted each subsequent failure, so a brute-force / credential-stuffing run is visible in
    // the logs before the rate limiter or a lockout would otherwise hide it.
    private const int AuthFailureWarnThreshold = 5;

    private void NoteAuthFailure(string stage, string? identifier)
    {
        var key = $"auth-fail:{stage}:{ClientIp}";
        var count = (_cache.TryGetValue<int>(key, out var existing) ? existing : 0) + 1;
        _cache.Set(key, count, new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(10) });
        if (count >= AuthFailureWarnThreshold)
            _logger.LogWarning("Repeated {Stage} failures from {ClientIp}: {Count} attempts in 10m (last identifier: {Identifier})",
                stage, ClientIp, count, identifier ?? "");
    }

    // The image CAPTCHA is on by default; set PHONIX_REQUIRE_CAPTCHA=false to disable (e.g. in tests).
    private static bool CaptchaRequired => Environment.GetEnvironmentVariable("PHONIX_REQUIRE_CAPTCHA") != "false";

    private string ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static bool IsStaff(AppUser user) => user.Role is UserRole.Admin or UserRole.Support;

    private static string RoleFa(UserRole role) => role == UserRole.Admin ? "مدیر" : "پشتیبان";

    // Burns several seconds before a failed-credential response so high-throughput credential-stuffing
    // tools stall. Disabled in the test host so the suite stays fast.
    private static async Task TarpitAsync()
    {
        if (Environment.GetEnvironmentVariable("PHONIX_DISABLE_TARPIT") == "true") return;
        await Task.Delay(Random.Shared.Next(3000, 5001));
    }

    // Issues the session cookie. adminScope=true marks a panel session (password + 2FA satisfied) and is the
    // only kind that may act as staff; a main-site login passes false. The Telegram "staff entered the panel"
    // alert fires only for an admin-scoped login.
    private LoginResultDto IssueSession(AppUser user, bool adminScope)
    {
        var token = _sessions.Protect(user, adminScope);
        // Admin-scoped sessions get a session-only cookie (persistent: false) so closing the browser ends the
        // panel session and forces a fresh login + 2FA next time; customer sessions stay persistent.
        AuthCookies.Issue(Response, token, Request.IsHttps, persistent: !adminScope);
        _logger.LogInformation("Login: {Username} (#{UserId}) adminScope={AdminScope} from {ClientIp}",
            user.Username, user.Id, adminScope, ClientIp);
        if (adminScope && IsStaff(user))
            _ = _alerts.SendAlertAsync(
                $"🔐 ورود {RoleFa(user.Role)} به پنل\nکاربر: {user.Username}\nIP: {ClientIp}\nزمان: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        return new LoginResultDto(false, null, token, user.ToDto());
    }

    private static string FrontendUrl => Environment.GetEnvironmentVariable("PHONIX_FRONTEND_URL") ?? "http://localhost:3000";

    private Task SendVerification(AppUser user)
    {
        var token = _store.CreateToken(user.Id, "verify", TimeSpan.FromHours(1));
        var link = $"{FrontendUrl}/verify-email?token={token}";
        var (text, html) = EmailTemplates.VerifyEmail(link);
        return _email.SendAsync(user.Email, "تأیید ایمیل حساب فونیکس", text, html);
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResultDto>> Register(RegisterInput input)
    {
        if (CaptchaRequired && !_captcha.Validate(input.CaptchaId, input.CaptchaText))
            return BadRequest("کد امنیتی تصویر نادرست است. دوباره تلاش کنید.");
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
        // Registration is a main-site action → a plain customer session, never admin-scoped.
        var token = _sessions.Protect(user, adminScope: false);
        AuthCookies.Issue(Response, token, Request.IsHttps);
        return new AuthResultDto(token, user.ToDto());
    }

    // The admin shell calls this to confirm the live session is genuinely admin-scoped staff. A main-site
    // session (even an admin's) carries the Customer role claim, so this returns 403 and the shell bounces
    // the user to the panel login. Exempt from the mandatory-2FA gate (it's under /api/auth), so a not-yet-
    // enrolled admin can still load the shell and be sent to the setup page.
    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [HttpGet("admin-context")]
    public ActionResult<AdminContextDto> AdminContext()
    {
        if (this.CurrentUserId() is not int id || _store.GetUser(id) is not { } user) return Unauthorized();
        return new AdminContextDto(user.Id, user.Name, user.Username, user.Role);
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
    public async Task<ActionResult<LoginResultDto>> Login(LoginInput input)
    {
        // Image CAPTCHA first — blocks automated credential-stuffing before any password work happens.
        if (CaptchaRequired && !_captcha.Validate(input.CaptchaId, input.CaptchaText))
            return BadRequest("کد امنیتی تصویر نادرست است. دوباره تلاش کنید.");

        var user = _store.FindByLogin(input.Identifier?.Trim() ?? "");
        if (user is null || !PasswordHasher.Verify(input.Password ?? "", user.Password))
        {
            _logger.LogWarning("Failed login for {Identifier} from {ClientIp}",
                input.Identifier?.Trim(), ClientIp);
            NoteAuthFailure("login", input.Identifier?.Trim());
            await TarpitAsync();
            return Unauthorized("نام کاربری یا گذرواژه نادرست است.");
        }
        if (user.Blocked)
        {
            _logger.LogWarning("Blocked account login attempt: {Username} (#{UserId}) from {ClientIp}",
                user.Username, user.Id, ClientIp);
            return StatusCode(403, "حساب شما مسدود شده است.");
        }

        var adminLogin = input.Admin == true;
        if (!adminLogin)
            // Main-site login: never a second factor, never an admin-scoped session — even for an admin.
            return IssueSession(user, adminScope: false);

        // ── Admin-panel login from here on ──
        if (!IsStaff(user))
            return StatusCode(403, "این حساب به پنل مدیریت دسترسی ندارد.");
        // 2FA is required to ENTER THE PANEL: complete the second step before any admin-scoped session exists.
        // The challenge token proves the password passed without re-sending credentials.
        if (user.TwoFactorEnabled)
            return new LoginResultDto(true, _twoFactor.Issue(user.Id), null, null);
        // Staff who haven't enrolled yet get an admin-scoped session so they can reach the mandatory setup.
        return IssueSession(user, adminScope: true);
    }

    [HttpPost("2fa/verify")]
    public async Task<ActionResult<LoginResultDto>> VerifyTwoFactor(TwoFactorVerifyInput input)
    {
        if (_twoFactor.Resolve(input.Token) is not int userId)
            return Unauthorized("نشست تأیید دو‌مرحله‌ای نامعتبر یا منقضی شده است. دوباره وارد شوید.");
        var user = _store.GetUser(userId);
        if (user is null || user.Blocked) return Unauthorized("نشست نامعتبر است.");
        if (!user.TwoFactorEnabled || !TotpService.Verify(user.TwoFactorSecret, input.Code ?? ""))
        {
            _logger.LogWarning("Failed 2FA for {Username} (#{UserId}) from {ClientIp}",
                user.Username, user.Id, ClientIp);
            NoteAuthFailure("2fa", user.Username);
            await TarpitAsync();
            return Unauthorized("کد تأیید نادرست است.");
        }
        // The second factor is only ever requested during an admin-panel login, so a verified challenge
        // always yields an admin-scoped session.
        return IssueSession(user, adminScope: true);
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

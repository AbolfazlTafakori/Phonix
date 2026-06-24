using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;
using Phonix.Api.Services;

namespace Phonix.Api.Controllers;

[ApiController]
[Route("api/backup")]
[Authorize(Roles = nameof(UserRole.Admin))] // restore is destructive — admins only, not support staff
public class BackupController : ControllerBase
{
    private readonly StoreData _store;
    private readonly ITelegramBackupSender _telegram;
    private readonly ITelegramAlertSender _alerts;
    private readonly ILogger<BackupController> _logger;

    public BackupController(StoreData store, ITelegramBackupSender telegram, ITelegramAlertSender alerts,
        ILogger<BackupController> logger)
    {
        _store = store;
        _telegram = telegram;
        _alerts = alerts;
        _logger = logger;
    }

    // download the full store as a timestamped file. When PHONIX_BACKUP_KEY is set the payload is encrypted
    // (AES-256-GCM, .phxbak); otherwise it is plain store.json.
    [HttpGet("export")]
    public IActionResult Export()
    {
        var stamp = DateTime.Now.ToString("yyyy-MM-dd-HHmm");
        var json = _store.SerializeSnapshot();
        if (BackupCrypto.IsEnabled)
            return File(Encoding.UTF8.GetBytes(BackupCrypto.Encrypt(json)), "application/octet-stream", $"phonix-backup-{stamp}.phxbak");
        return File(Encoding.UTF8.GetBytes(json), "application/json", $"phonix-backup-{stamp}.json");
    }

    // Replace the entire store with an uploaded backup. This is the single most destructive action in the
    // system, so it is gated behind THREE independent factors that must all pass before a byte is written:
    //   1. a fresh 6-digit 2FA (TOTP) code from the signed-in admin (re-authentication),
    //   2. manual re-entry of the server's PHONIX_BACKUP_KEY (proves possession of the offline key),
    //   3. a structurally valid backup file (decryptable + parseable + non-empty).
    // Every outcome is written to the audit log; the operation fails closed on any missing or invalid factor.
    [HttpPost("restore")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Restore(
        [FromForm] IFormFile? file,
        [FromForm] string? backupKey,
        [FromForm] string? twoFactorCode)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var user = this.CurrentUserId() is int uid ? _store.GetUser(uid) : null;
        var username = user?.Username ?? "unknown";

        IActionResult Deny(string reason, IActionResult result)
        {
            _logger.LogWarning(
                "[SRV] DATABASE RESTORE DENIED ({Reason}). Attempted by Admin: {Username}, IP: {IP}, Timestamp: {UtcTime}.",
                reason, username, ip, DateTime.UtcNow.ToString("o"));
            return result;
        }

        if (user is null || user.Role != UserRole.Admin)
            return Deny("not an admin", Unauthorized("نشست نامعتبر است."));

        // Factor 1 — fresh second factor. Fail closed if the admin has no 2FA enrolled.
        if (!user.TwoFactorEnabled
            || string.IsNullOrWhiteSpace(twoFactorCode)
            || !TotpService.Verify(user.TwoFactorSecret, twoFactorCode))
            return Deny("invalid 2FA", Unauthorized("کد تأیید دو‌مرحله‌ای نادرست است."));

        // Factor 2 — the entered key must match the server's configured PHONIX_BACKUP_KEY (constant-time).
        var configuredKey = Environment.GetEnvironmentVariable("PHONIX_BACKUP_KEY");
        if (string.IsNullOrWhiteSpace(configuredKey)
            || string.IsNullOrWhiteSpace(backupKey)
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(backupKey), Encoding.UTF8.GetBytes(configuredKey)))
            return Deny("invalid backup key", Unauthorized("کلید پشتیبان (PHONIX_BACKUP_KEY) نادرست است."));

        // Factor 3 — the file itself.
        if (file is null || file.Length == 0)
            return Deny("missing file", BadRequest("فایل پشتیبان نامعتبر است."));

        string raw;
        using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8))
            raw = (await reader.ReadToEndAsync()).Trim();

        var json = raw;
        if (BackupCrypto.LooksEncrypted(raw))
        {
            var decrypted = BackupCrypto.Decrypt(raw);
            if (decrypted is null)
                return Deny("decryption failed", BadRequest("رمزگشایی فایل پشتیبان ناموفق بود. کلید پشتیبان را بررسی کنید."));
            json = decrypted;
        }

        StoreSnapshot? snapshot;
        try
        {
            snapshot = _store.DeserializeSnapshot(json);
        }
        catch
        {
            return Deny("invalid backup content", BadRequest("فایل پشتیبان نامعتبر است."));
        }

        if (snapshot is null || snapshot.Users.Count == 0)
            return Deny("empty snapshot", BadRequest("فایل پشتیبان معتبر به‌نظر نمی‌رسد (هیچ کاربری ندارد)."));

        _store.LoadSnapshot(snapshot);
        _store.Save();

        _logger.LogWarning(
            "[SRV] DATABASE RESTORE SUCCESSFUL. Executed by Admin: {Username}, IP: {IP}, Timestamp: {UtcTime}.",
            username, ip, DateTime.UtcNow.ToString("o"));

        return Ok(new { ok = true });
    }

    [HttpGet("telegram")]
    public TelegramSettings GetTelegram() => _store.GetTelegramSettings();

    [HttpPut("telegram")]
    public TelegramSettings UpdateTelegram(TelegramSettings settings)
    {
        _store.UpdateTelegramSettings(settings);
        return _store.GetTelegramSettings();
    }

    // send a backup to Telegram immediately, using the saved settings.
    [HttpPost("telegram/test")]
    public async Task<IActionResult> TestTelegram()
    {
        var (ok, error) = await _telegram.SendAsync("پشتیبان آزمایشی فونیکس", HttpContext.RequestAborted);
        return ok ? Ok(new { ok = true }) : BadRequest(error);
    }

    // send a sample alert message immediately (bypasses the alerts toggle) to verify wiring.
    [HttpPost("telegram/test-alert")]
    public async Task<IActionResult> TestAlert()
    {
        var ok = await _alerts.SendAlertAsync("🔔 هشدار آزمایشی فونیکس — اتصال هشدارها برقرار است.",
            force: true, ct: HttpContext.RequestAborted);
        return ok ? Ok(new { ok = true })
                  : BadRequest("ارسال نشد. توکن بات و شناسه چت را بررسی کنید.");
    }
}

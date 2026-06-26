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

    // ── Per-section backup (each domain exported/restored independently; small Telegram-friendly files) ──

    [HttpGet("sections")]
    public IActionResult Sections() => Ok(new
    {
        sections = StoreData.BackupSections.Select(x => new { key = x.Section.ToString(), label = x.Label }),
        history = _store.GetBackupLog().Select(h => new { h.Section, h.Target, h.Ok, h.Error, h.AtUtc }),
        encrypted = BackupCrypto.IsEnabled,
    });

    [HttpGet("export/{section}")]
    public IActionResult ExportSection(string section)
    {
        if (!Enum.TryParse<BackupSection>(section, ignoreCase: true, out var sec)) return NotFound();
        var json = _store.SerializeSection(sec);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd-HHmm");
        var name = section.ToLowerInvariant();
        _store.RecordBackup(sec.ToString(), "دانلود", true, "");
        if (BackupCrypto.IsEnabled)
            return File(Encoding.UTF8.GetBytes(BackupCrypto.Encrypt(json)), "application/octet-stream", $"phonix-{name}-{stamp}.phxbak");
        return File(Encoding.UTF8.GetBytes(json), "application/json", $"phonix-{name}-{stamp}.json");
    }

    // Restore a single section — same three-factor gate as the full restore.
    [HttpPost("restore/{section}")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> RestoreSection(
        string section, [FromForm] IFormFile? file, [FromForm] string? backupKey, [FromForm] string? twoFactorCode)
    {
        if (!Enum.TryParse<BackupSection>(section, ignoreCase: true, out var sec)) return NotFound();

        var deny = CheckRestoreAuth(backupKey, twoFactorCode, out var username, out var ip);
        if (deny is not null) return deny;

        if (file is null || file.Length == 0) return BadRequest("فایل پشتیبان نامعتبر است.");
        string raw;
        using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8))
            raw = (await reader.ReadToEndAsync()).Trim();

        var json = raw;
        if (BackupCrypto.LooksEncrypted(raw))
        {
            var decrypted = BackupCrypto.Decrypt(raw);
            if (decrypted is null) return BadRequest("رمزگشایی فایل پشتیبان ناموفق بود. کلید پشتیبان را بررسی کنید.");
            json = decrypted;
        }

        StoreSnapshot? snapshot;
        try { snapshot = _store.DeserializeSnapshot(json); }
        catch { return BadRequest("فایل پشتیبان نامعتبر است."); }
        if (snapshot is null) return BadRequest("فایل پشتیبان نامعتبر است.");
        if (!string.Equals(snapshot.Section, sec.ToString(), StringComparison.OrdinalIgnoreCase))
            return BadRequest($"این فایل برای بخش انتخاب‌شده نیست (مربوط به «{snapshot.Section ?? "کامل"}»).");

        _store.RestoreSection(sec, snapshot);
        _store.RecordBackup(sec.ToString(), "ریستور", true, $"by {username}");
        _logger.LogWarning("[SRV] SECTION RESTORE '{Section}' by Admin {User}, IP {IP}, {Time}", sec, username, ip, DateTime.UtcNow.ToString("o"));
        return Ok(new { ok = true });
    }

    // Manually send one section to the configured Telegram chat right now.
    [HttpPost("telegram/send/{section}")]
    public async Task<IActionResult> SendSection(string section)
    {
        if (!Enum.TryParse<BackupSection>(section, ignoreCase: true, out var sec)) return NotFound();
        var label = StoreData.BackupSections.First(x => x.Section == sec).Label;
        var (ok, err) = await _telegram.SendSectionAsync(sec, $"پشتیبان دستی فونیکس — {label}", HttpContext.RequestAborted);
        return ok ? Ok(new { ok = true }) : BadRequest(err);
    }

    // Instant full backup: send every section now (for critical moments before a risky change).
    [HttpPost("telegram/send-all")]
    public async Task<IActionResult> SendAll()
    {
        var errors = new List<string>();
        foreach (var (sec, label) in StoreData.BackupSections)
        {
            var (ok, err) = await _telegram.SendSectionAsync(sec, $"پشتیبان لحظه‌ای فونیکس — {label}", HttpContext.RequestAborted);
            if (!ok) errors.Add($"{label}: {err}");
        }
        return errors.Count == 0 ? Ok(new { ok = true }) : BadRequest(string.Join(" / ", errors));
    }

    // Shared three-factor gate (admin + fresh 2FA + PHONIX_BACKUP_KEY). Returns a deny result, or null when ok.
    private IActionResult? CheckRestoreAuth(string? backupKey, string? twoFactorCode, out string username, out string ip)
    {
        ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var user = this.CurrentUserId() is int uid ? _store.GetUser(uid) : null;
        username = user?.Username ?? "unknown";
        var who = username;
        var where = ip;
        IActionResult Deny(string reason, IActionResult result)
        {
            _logger.LogWarning("[SRV] RESTORE DENIED ({Reason}). Admin: {User}, IP: {IP}, {Time}.", reason, who, where, DateTime.UtcNow.ToString("o"));
            return result;
        }

        if (user is null || user.Role != UserRole.Admin)
            return Deny("not an admin", Unauthorized("نشست نامعتبر است."));
        if (!user.TwoFactorEnabled || string.IsNullOrWhiteSpace(twoFactorCode) || !TotpService.Verify(user.TwoFactorSecret, twoFactorCode))
            return Deny("invalid 2FA", Unauthorized("کد تأیید دو‌مرحله‌ای نادرست است."));
        var configuredKey = Environment.GetEnvironmentVariable("PHONIX_BACKUP_KEY");
        if (string.IsNullOrWhiteSpace(configuredKey) || string.IsNullOrWhiteSpace(backupKey)
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(backupKey), Encoding.UTF8.GetBytes(configuredKey)))
            return Deny("invalid backup key", Unauthorized("کلید پشتیبان (PHONIX_BACKUP_KEY) نادرست است."));
        return null;
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

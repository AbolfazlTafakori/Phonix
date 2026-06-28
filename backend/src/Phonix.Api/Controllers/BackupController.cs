using System.IO.Compression;
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
    private readonly IDataStore _store;
    private readonly ITelegramBackupSender _telegram;
    private readonly ITelegramAlertSender _alerts;
    private readonly IFileStorageService _files;
    private readonly ILogger<BackupController> _logger;

    public BackupController(IDataStore store, ITelegramBackupSender telegram, ITelegramAlertSender alerts,
        IFileStorageService files, ILogger<BackupController> logger)
    {
        _store = store;
        _telegram = telegram;
        _alerts = alerts;
        _files = files;
        _logger = logger;
    }

    // Manual media backup (download only — not sent to Telegram). Public images are returned as a plain zip;
    // the sensitive documents archive is encrypted when PHONIX_BACKUP_KEY is set.
    [HttpGet("media/public")]
    public IActionResult MediaPublic()
    {
        var zip = _files.ArchivePublicMedia();
        var stamp = DateTime.Now.ToString("yyyy-MM-dd-HHmm");
        _store.RecordBackup("رسانهٔ عمومی", "دانلود", true, "");
        return File(zip, "application/zip", $"phonix-media-public-{stamp}.zip");
    }

    [HttpGet("media/sensitive")]
    public IActionResult MediaSensitive()
    {
        var zip = _files.ArchiveSensitiveMedia();
        var stamp = DateTime.Now.ToString("yyyy-MM-dd-HHmm");
        _store.RecordBackup("مدارک حساس", "دانلود", true, "");
        if (BackupCrypto.IsEnabled)
            return File(BackupCrypto.EncryptBytes(zip), "application/octet-stream", $"phonix-media-sensitive-{stamp}.phxbak");
        return File(zip, "application/zip", $"phonix-media-sensitive-{stamp}.zip");
    }

    // Restore a media archive (public or sensitive) back into the uploads folder — same three-factor gate.
    [HttpPost("media/restore/{kind}")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> RestoreMedia(
        string kind, [FromForm] IFormFile? file, [FromForm] string? backupKey, [FromForm] string? twoFactorCode)
    {
        if (kind is not ("public" or "sensitive")) return NotFound();
        var deny = CheckRestoreAuth(backupKey, twoFactorCode, out var username, out var ip);
        if (deny is not null) return deny;
        if (file is null || file.Length == 0) return BadRequest("فایل رسانه نامعتبر است.");

        var bytes = await ReadAllBytes(file);
        var zipBytes = BackupCrypto.LooksEncryptedBytes(bytes) ? BackupCrypto.DecryptBytes(bytes) : bytes;
        if (zipBytes is null) return BadRequest("رمزگشایی آرشیو رسانه ناموفق بود. کلید پشتیبان را بررسی کنید.");

        try
        {
            var n = _files.ExtractMediaArchive(zipBytes);
            _store.RecordBackup(kind == "public" ? "رسانهٔ عمومی" : "مدارک حساس", "ریستور", true, $"{n} فایل");
            _logger.LogWarning("[SRV] MEDIA RESTORE '{Kind}' ({N} files) by Admin {User}, IP {IP}", kind, n, username, ip);
            return Ok(new { ok = true, mediaFiles = n });
        }
        catch { return BadRequest("آرشیو رسانه نامعتبر است."); }
    }

    // One-shot disk reclamation: deletes uploaded public images (avatars, product/banner/showcase imagery,
    // blog covers, plan tutorial media…) that are no longer referenced anywhere in the store — the historical
    // orphans left behind before replace-time cleanup existed. Files newer than `minAgeMinutes` are skipped so
    // an upload still being wired into a draft is never swept. Admin-only (class-level auth); idempotent.
    [HttpPost("media/cleanup-orphans")]
    public IActionResult CleanupOrphanMedia([FromQuery] int minAgeMinutes = 60)
    {
        var minAge = TimeSpan.FromMinutes(Math.Clamp(minAgeMinutes, 0, 7 * 24 * 60));
        var snapshot = _store.SerializeSnapshot();
        var deleted = _files.SweepPublicOrphans(snapshot, minAge);
        _store.RecordBackup("پاک‌سازی رسانهٔ یتیم", "اجرا", true, $"{deleted} فایل حذف شد");
        _logger.LogWarning("[SRV] ORPHAN MEDIA SWEEP removed {Count} unreferenced public file(s)", deleted);
        return Ok(new { ok = true, deleted });
    }

    // ── Full manual backup: everything (data + all media) in one encrypted file ──

    [HttpGet("full")]
    public IActionResult ExportFull()
    {
        var json = _store.SerializeSnapshot();
        var zip = _files.ArchiveFull(json);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd-HHmm");
        _store.RecordBackup("کامل (داده + رسانه)", "دانلود", true, "");
        if (BackupCrypto.IsEnabled)
            return File(BackupCrypto.EncryptBytes(zip), "application/octet-stream", $"phonix-full-{stamp}.phxbak");
        return File(zip, "application/zip", $"phonix-full-{stamp}.zip");
    }

    [HttpPost("full/restore")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> RestoreFull(
        [FromForm] IFormFile? file, [FromForm] string? backupKey, [FromForm] string? twoFactorCode)
    {
        var deny = CheckRestoreAuth(backupKey, twoFactorCode, out var username, out var ip);
        if (deny is not null) return deny;
        if (file is null || file.Length == 0) return BadRequest("فایل پشتیبان نامعتبر است.");

        var bytes = await ReadAllBytes(file);
        var zipBytes = BackupCrypto.LooksEncryptedBytes(bytes) ? BackupCrypto.DecryptBytes(bytes) : bytes;
        if (zipBytes is null) return BadRequest("رمزگشایی فایل پشتیبان ناموفق بود. کلید پشتیبان را بررسی کنید.");

        StoreSnapshot? snapshot;
        try
        {
            using var ms = new MemoryStream(zipBytes);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            var entry = zip.GetEntry("store.json");
            if (entry is null) return BadRequest("فایل پشتیبان کامل نامعتبر است (store.json یافت نشد).");
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            snapshot = _store.DeserializeSnapshot(await reader.ReadToEndAsync());
        }
        catch { return BadRequest("فایل پشتیبان نامعتبر است."); }

        if (snapshot is null || snapshot.Users.Count == 0)
            return BadRequest("فایل پشتیبان معتبر به‌نظر نمی‌رسد.");

        _store.LoadSnapshot(snapshot);
        _store.Save();
        var mediaFiles = _files.ExtractMediaArchive(zipBytes);

        _store.RecordBackup("کامل (داده + رسانه)", "ریستور", true, $"{mediaFiles} فایل رسانه");
        _logger.LogWarning("[SRV] FULL RESTORE (data + {N} media) by Admin {User}, IP {IP}, {Time}", mediaFiles, username, ip, DateTime.UtcNow.ToString("o"));
        return Ok(new { ok = true, mediaFiles });
    }

    private static async Task<byte[]> ReadAllBytes(IFormFile file)
    {
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        return ms.ToArray();
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

    // Send the uploaded files to Telegram now, kept separate: kind=site → public images (plain zip),
    // kind=documents → users' cards/KYC/receipts (encrypted). Auto-split into parts for large archives.
    // Gated behind the same three factors as a restore (admin + fresh 2FA + PHONIX_BACKUP_KEY): pushing media
    // off the server — especially users' documents — is a sensitive export, so it re-authenticates first.
    [HttpPost("telegram/media/{kind}")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> SendMedia(string kind, [FromForm] string? backupKey, [FromForm] string? twoFactorCode)
    {
        if (kind is not ("site" or "documents")) return NotFound();
        var deny = CheckRestoreAuth(backupKey, twoFactorCode, out var username, out var ip);
        if (deny is not null) return deny;
        var sensitive = kind == "documents";
        var caption = sensitive ? "پشتیبان دستی فونیکس — مدارک کاربران" : "پشتیبان دستی فونیکس — رسانهٔ سایت";
        var (ok, err) = await _telegram.SendMediaAsync(sensitive, caption, HttpContext.RequestAborted);
        if (ok) _logger.LogWarning("[SRV] MEDIA TELEGRAM SEND '{Kind}' by Admin {User}, IP {IP}, {Time}", kind, username, ip, DateTime.UtcNow.ToString("o"));
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

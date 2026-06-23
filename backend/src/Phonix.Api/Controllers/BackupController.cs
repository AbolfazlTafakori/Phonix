using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Models;
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

    public BackupController(StoreData store, ITelegramBackupSender telegram, ITelegramAlertSender alerts)
    {
        _store = store;
        _telegram = telegram;
        _alerts = alerts;
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

    // replace the entire store with an uploaded backup. Accepts both the encrypted container and plain JSON
    // (so older plain backups still import); reads the raw body to support either.
    [HttpPost("restore")]
    public async Task<IActionResult> Restore()
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var raw = (await reader.ReadToEndAsync()).Trim();
        if (string.IsNullOrEmpty(raw))
            return BadRequest("فایل پشتیبان نامعتبر است.");

        string? json = raw;
        if (BackupCrypto.LooksEncrypted(raw))
        {
            json = BackupCrypto.Decrypt(raw);
            if (json is null)
                return BadRequest("رمزگشایی فایل پشتیبان ناموفق بود. کلید پشتیبان (PHONIX_BACKUP_KEY) را بررسی کنید.");
        }

        StoreSnapshot? snapshot;
        try
        {
            snapshot = _store.DeserializeSnapshot(json);
        }
        catch
        {
            return BadRequest("فایل پشتیبان نامعتبر است.");
        }
        if (snapshot is null)
            return BadRequest("فایل پشتیبان نامعتبر است.");
        if (snapshot.Users.Count == 0)
            return BadRequest("فایل پشتیبان معتبر به‌نظر نمی‌رسد (هیچ کاربری ندارد).");

        _store.LoadSnapshot(snapshot);
        _store.Save();
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

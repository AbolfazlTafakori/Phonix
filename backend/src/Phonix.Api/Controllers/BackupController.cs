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

    // download the full store as a timestamped file (identical to store.json on disk).
    [HttpGet("export")]
    public IActionResult Export()
    {
        var bytes = Encoding.UTF8.GetBytes(_store.SerializeSnapshot());
        var name = $"phonix-backup-{DateTime.Now:yyyy-MM-dd-HHmm}.json";
        return File(bytes, "application/json", name);
    }

    // replace the entire store with an uploaded backup.
    [HttpPost("restore")]
    public IActionResult Restore([FromBody] StoreSnapshot? snapshot)
    {
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

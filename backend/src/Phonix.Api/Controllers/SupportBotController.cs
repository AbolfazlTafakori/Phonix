using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;
using Phonix.Api.Services;

namespace Phonix.Api.Controllers;

// ── Panel: the support bot's own settings (tickets and live chat answered from a Telegram group) ──────────

public record SupportBotStatusDto(bool Enabled, bool HasToken, string TokenHint, string Username, string ChatId, bool Polling);
// Token: null keeps the saved one; a value replaces it (checked with Telegram first).
public record SupportBotInput(bool Enabled, string? Token, string? ChatId);

[ApiController]
[Route("api/admin/support-bot")]
[Authorize(Roles = AuthExtensions.StaffRoles)]
[AdminPermission("support-bot")]
public class SupportBotController : ControllerBase
{
    private readonly IDataStore _store;
    private readonly ITelegramSupportBot _bot;
    private readonly IClusterSyncService? _cluster;

    public SupportBotController(IDataStore store, ITelegramSupportBot bot, IClusterSyncService? cluster = null)
    {
        _store = store;
        _bot = bot;
        _cluster = cluster;
    }

    // The token itself never goes back to the browser: the page only needs to know one is saved, and which.
    private static string Hint(string token)
    {
        var colon = token.IndexOf(':');
        return token.Length < 10 || colon < 0 ? "" : $"{token[..colon]}:…{token[^4..]}";
    }

    private SupportBotStatusDto Status()
    {
        var s = _store.GetTelegramSettings();
        var token = (s.SupportBotToken ?? "").Trim();
        var polling = s.SupportBotEnabled && token.Length > 0 && _cluster?.Role is not (ClusterRole.Standby or ClusterRole.Recovering);
        return new SupportBotStatusDto(s.SupportBotEnabled, token.Length > 0, Hint(token), s.SupportBotUsername ?? "",
            s.SupportChatId ?? "", polling);
    }

    [HttpGet]
    public SupportBotStatusDto Get() => Status();

    [HttpPut]
    public async Task<ActionResult<SupportBotStatusDto>> Save(SupportBotInput input, CancellationToken ct)
    {
        var chatId = (input.ChatId ?? "").Trim();
        if (chatId.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(chatId, @"^-?\d{1,32}$"))
            return BadRequest("شناسه‌ی گروه باید عدد باشد (مثل -1001234567890). آن را با دستور /id داخل گروه بگیرید.");

        string? token = null, username = null;
        if (!string.IsNullOrWhiteSpace(input.Token))
        {
            token = input.Token.Trim();
            // Telegram gives a bot's updates to one reader only: reusing another bot's token would silently steal
            // that bot's button taps and replies.
            var s = _store.GetTelegramSettings();
            if (new[] { s.BotToken, s.ReceiptBotToken, s.OrderBotToken, s.CustomerBotToken }
                .Any(t => string.Equals((t ?? "").Trim(), token, StringComparison.Ordinal)))
                return BadRequest("این توکن مال یکی از ربات‌های دیگر سایت است. برای پشتیبانی یک ربات جدا در BotFather بسازید.");
            var check = await _bot.CheckTokenAsync(token, ct);
            if (!check.Ok) return BadRequest(check.Error);
            username = check.Username;
        }
        var hasToken = token is not null || !string.IsNullOrWhiteSpace(_store.GetTelegramSettings().SupportBotToken);
        if (input.Enabled && !hasToken) return BadRequest("برای روشن کردن ربات، ابتدا توکن آن را وارد کنید.");
        _store.SetSupportBot(input.Enabled, token, username, chatId);
        return Status();
    }

    // Removes the token: the bot stops, and nothing more is posted to the group.
    [HttpDelete("token")]
    public SupportBotStatusDto RemoveToken()
    {
        _store.SetSupportBot(false, "", null, _store.GetTelegramSettings().SupportChatId ?? "");
        return Status();
    }

    // Posts a message to the group, reporting Telegram's own error when it can't.
    [HttpPost("test")]
    public async Task<IActionResult> Test(CancellationToken ct)
    {
        var (ok, error) = await _bot.SendTestAsync(ct);
        return ok ? Ok(new { ok = true }) : BadRequest(error);
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;
using Phonix.Api.Services;

namespace Phonix.Api.Controllers;

// ── Panel: the customer bot's own settings ─────────────────────────────────────────────────────────────

public record CustomerBotStatusDto(bool Enabled, bool Public, bool HasToken, string TokenHint, string Username,
    int LinkedCount, bool Polling);
// Token: null keeps the saved one; a value replaces it (checked with Telegram first).
public record CustomerBotInput(bool Enabled, bool Public, string? Token);

[ApiController]
[Route("api/admin/customer-bot")]
[Authorize(Roles = AuthExtensions.StaffRoles)]
[AdminPermission("customer-bot")]
public class CustomerBotController : ControllerBase
{
    private readonly IDataStore _store;
    private readonly ITelegramCustomerBot _bot;
    private readonly IClusterSyncService? _cluster;

    public CustomerBotController(IDataStore store, ITelegramCustomerBot bot, IClusterSyncService? cluster = null)
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

    private CustomerBotStatusDto Status()
    {
        var s = _store.GetTelegramSettings();
        var token = (s.CustomerBotToken ?? "").Trim();
        var linked = _store.GetUsers().Count(u => u.TelegramChatId is not null);
        var polling = s.CustomerBotEnabled && token.Length > 0 && _cluster?.Role is not (ClusterRole.Standby or ClusterRole.Recovering);
        return new CustomerBotStatusDto(s.CustomerBotEnabled, s.CustomerBotPublic, token.Length > 0, Hint(token),
            s.CustomerBotUsername ?? "", linked, polling);
    }

    [HttpGet]
    public CustomerBotStatusDto Get() => Status();

    [HttpPut]
    public async Task<ActionResult<CustomerBotStatusDto>> Save(CustomerBotInput input, CancellationToken ct)
    {
        string? token = null, username = null;
        if (!string.IsNullOrWhiteSpace(input.Token))
        {
            // A token that Telegram doesn't recognise is refused here, so the bot is never "on" with a dead key.
            var check = await _bot.CheckTokenAsync(input.Token, ct);
            if (!check.Ok) return BadRequest(check.Error);
            token = input.Token.Trim();
            username = check.Username;
        }
        var hasToken = token is not null || !string.IsNullOrWhiteSpace(_store.GetTelegramSettings().CustomerBotToken);
        if (input.Enabled && !hasToken) return BadRequest("برای روشن کردن ربات، ابتدا توکن آن را وارد کنید.");
        _store.SetCustomerBot(input.Enabled, input.Public, token, username);
        return Status();
    }

    // Removes the token: the bot stops, and the «اتصال به تلگرام» button disappears for customers. Links already
    // made stay on the accounts, so putting a token back reconnects everyone without them relinking.
    [HttpDelete("token")]
    public CustomerBotStatusDto RemoveToken()
    {
        _store.SetCustomerBot(false, false, "", null);
        return Status();
    }

    // Checks the saved token with Telegram and refreshes the bot's @username.
    [HttpPost("test")]
    public async Task<IActionResult> Test(CancellationToken ct)
    {
        var s = _store.GetTelegramSettings();
        if (string.IsNullOrWhiteSpace(s.CustomerBotToken)) return BadRequest("توکنی ذخیره نشده است.");
        var check = await _bot.CheckTokenAsync(s.CustomerBotToken, ct);
        if (!check.Ok) return BadRequest(check.Error);
        if (!string.Equals(check.Username, s.CustomerBotUsername, StringComparison.Ordinal))
            _store.SetCustomerBot(s.CustomerBotEnabled, s.CustomerBotPublic, s.CustomerBotToken, check.Username);
        return Ok(new { ok = true, username = check.Username });
    }
}

// ── Customer: linking their own account ────────────────────────────────────────────────────────────────

public record TelegramLinkStatusDto(bool Available, string? BotUsername, bool Linked, string? TelegramUsername, bool Notify);
public record TelegramNotifyInput(bool Notify);

[ApiController]
[Route("api/account/telegram")]
[Authorize]
public class AccountTelegramController : ControllerBase
{
    private readonly IDataStore _store;
    private readonly ITelegramCustomerBot _bot;

    public AccountTelegramController(IDataStore store, ITelegramCustomerBot bot)
    {
        _store = store;
        _bot = bot;
    }

    // Offered only when staff have switched it on for customers AND the bot is configured — until then the
    // account page shows nothing at all.
    private (bool Available, string? Username) Offer()
    {
        var s = _store.GetTelegramSettings();
        var ok = s.CustomerBotEnabled && s.CustomerBotPublic && !string.IsNullOrWhiteSpace(s.CustomerBotToken)
                 && !string.IsNullOrWhiteSpace(s.CustomerBotUsername);
        return (ok, ok ? s.CustomerBotUsername : null);
    }

    [HttpGet]
    public ActionResult<TelegramLinkStatusDto> Get()
    {
        if (this.CurrentUserId() is not int id || _store.GetUser(id) is not { } user) return Unauthorized();
        var (available, username) = Offer();
        return new TelegramLinkStatusDto(available, username, user.TelegramChatId is not null, user.TelegramUsername, user.TelegramNotify);
    }

    // A one-time t.me link for this account: opening it sends "/start <token>" to the bot, which links the chat.
    [HttpPost("link")]
    public IActionResult Link()
    {
        if (this.CurrentUserId() is not int id) return Unauthorized();
        var (available, username) = Offer();
        if (!available) return BadRequest("اتصال به تلگرام در حال حاضر فعال نیست.");
        var token = _store.CreateToken(id, TelegramCustomerBot.LinkPurpose, TelegramCustomerBot.LinkLifetime);
        return Ok(new { url = $"https://t.me/{username}?start={token}" });
    }

    [HttpDelete]
    public async Task<IActionResult> Unlink(CancellationToken ct)
    {
        if (this.CurrentUserId() is not int id || _store.GetUser(id) is not { } user) return Unauthorized();
        var chat = user.TelegramChatId;
        _store.UnlinkTelegram(id);
        if (chat is long c)
            await _bot.SendAsync(c, "اتصال این تلگرام به حساب شما در فونیکس وریفای از طرف سایت قطع شد.", ct);
        return NoContent();
    }

    [HttpPut("notify")]
    public IActionResult SetNotify(TelegramNotifyInput input)
    {
        if (this.CurrentUserId() is not int id) return Unauthorized();
        _store.UpdateUser(id, u => u.TelegramNotify = input.Notify);
        return NoContent();
    }
}

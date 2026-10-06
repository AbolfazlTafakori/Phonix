using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;
using Phonix.Api.Services;

namespace Phonix.Api.Controllers;

// ── Panel: the customer bot's own settings ─────────────────────────────────────────────────────────────

// ShopUrl: where the shop opens in Telegram — null when the site isn't on https, so there is none to offer.
// Warning: a saved change Telegram didn't fully take (the menu button), for staff to see.
public record CustomerBotStatusDto(bool Enabled, bool Public, bool HasToken, string TokenHint, string Username,
    int LinkedCount, bool Polling, bool Shop = false, string? ShopUrl = null, string? Warning = null);
// Token: null keeps the saved one; a value replaces it (checked with Telegram first). Shop: null keeps it.
public record CustomerBotInput(bool Enabled, bool Public, string? Token, bool? Shop = null);

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
            s.CustomerBotUsername ?? "", linked, polling, s.CustomerBotShop, TelegramCustomerBot.ShopUrl);
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
        if (input.Shop == true && TelegramCustomerBot.ShopUrl is null)
            return BadRequest("فروشگاه داخل تلگرام فقط روی آدرس https کار می‌کند؛ PHONIX_FRONTEND_URL سایت https نیست.");
        _store.SetCustomerBot(input.Enabled, input.Public, token, username, input.Shop);
        // The menu button lives on Telegram's side, so it follows the switch here. The settings are saved
        // either way; a refusal is shown to staff, and «تست اتصال» tries again.
        var menu = await _bot.SyncMenuButtonAsync(ct);
        return menu.Ok ? Status() : Status() with { Warning = menu.Error };
    }

    // Removes the token: the bot stops, and the «اتصال به تلگرام» button disappears for customers. Links already
    // made stay on the accounts, so putting a token back reconnects everyone without them relinking.
    [HttpDelete("token")]
    public async Task<CustomerBotStatusDto> RemoveToken(CancellationToken ct = default)
    {
        // Put the menu button back while the token still works — after this nothing can reach the bot.
        var s = _store.GetTelegramSettings();
        if (s.CustomerBotShop)
        {
            _store.SetCustomerBot(s.CustomerBotEnabled, s.CustomerBotPublic, null, null, shop: false);
            await _bot.SyncMenuButtonAsync(ct);
        }
        _store.SetCustomerBot(false, false, "", null, shop: false);
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
        // Also re-applies the menu button, which is how a failed earlier attempt gets fixed.
        var menu = await _bot.SyncMenuButtonAsync(ct);
        if (!menu.Ok) return BadRequest(menu.Error);
        return Ok(new { ok = true, username = check.Username });
    }
}

// ── Customer: linking their own account ────────────────────────────────────────────────────────────────

// Shop: the shop inside Telegram is on — inside it, the account page offers to link that same Telegram.
public record TelegramLinkStatusDto(bool Available, string? BotUsername, bool Linked, string? TelegramUsername, bool Notify,
    bool Shop = false);
public record TelegramNotifyInput(bool Notify);
// The Mini App's launch data (Telegram.WebApp.initData), as Telegram signed it.
public record TelegramInitDataInput(string InitData);

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
        return new TelegramLinkStatusDto(available, username, user.TelegramChatId is not null, user.TelegramUsername, user.TelegramNotify,
            TelegramCustomerBot.ShopToken(_store) is not null);
    }

    // Linking from INSIDE the shop in Telegram: the signed-in session proves the account, Telegram's signed
    // launch data proves the Telegram account — both at once, so no t.me round trip is needed. It is a button
    // the customer presses, never automatic: signing in on someone else's phone must not hand that phone's
    // Telegram a way back into the account.
    [HttpPost("webapp")]
    public async Task<IActionResult> LinkFromShop(TelegramInitDataInput input, CancellationToken ct)
    {
        if (this.CurrentUserId() is not int id || _store.GetUser(id) is not { } user) return Unauthorized();
        if (TelegramCustomerBot.ShopToken(_store) is not { } token) return BadRequest("فروشگاه داخل تلگرام در حال حاضر فعال نیست.");
        if (TelegramInitData.Validate(input.InitData, token, DateTime.UtcNow) is not { } tg)
            return BadRequest("اطلاعات تلگرام معتبر نیست یا منقضی شده. فروشگاه را از ربات دوباره باز کنید.");
        if (!_store.LinkTelegram(id, tg.Id, tg.Username)) return BadRequest("اتصال انجام نشد؛ لطفاً دوباره تلاش کنید.");
        var name = string.IsNullOrWhiteSpace(user.Name) ? user.Username : user.Name;
        await _bot.SendAsync(tg.Id,
            $"✅ حساب «{name}» در فونیکس وریفای به این تلگرام وصل شد.\n\nاز این پس فروشگاه داخل تلگرام بدون ورود دوباره باز می‌شود و اطلاعات سفارش‌ها هم اینجا برایتان ارسال می‌شود.\n\nبرای قطع اتصال: /stop", ct);
        return NoContent();
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

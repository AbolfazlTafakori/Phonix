using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;
using Phonix.Api.Services;

namespace Phonix.Api.Controllers;

// ── Panel: the customer bot's own settings ─────────────────────────────────────────────────────────────

// ShopUrl: where the shop opens in Telegram — null when the site isn't on https, so there is none to offer.
// Warning: a saved change Telegram didn't fully take (the menu button), for staff to see.
// CodeMinutes: how long the code mailed for linking stays valid.
// Sales: buying inside the bot is on. SalesCard: the card the bot asks buyers to pay to (null: none active, so
// nothing can be bought there). SalesProducts: how many products a buyer can actually pick there right now.
public record CustomerBotStatusDto(bool Enabled, bool Public, bool HasToken, string TokenHint, string Username,
    int LinkedCount, bool Polling, bool Shop = false, string? ShopUrl = null, string? Warning = null, int CodeMinutes = 15,
    bool Sales = false, string? SalesCard = null, int SalesProducts = 0, bool Admin = false, int StaffLinked = 0);
// Token: null keeps the saved one; a value replaces it (checked with Telegram first). Shop: null keeps it.
public record CustomerBotInput(bool Enabled, bool Public, string? Token, bool? Shop = null, int? CodeMinutes = null,
    bool? Sales = null, bool? Admin = null);

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
        var users = _store.GetUsers();
        var linked = users.Count(u => u.TelegramChatId is not null);
        var staffLinked = users.Count(u => u.TelegramChatId is not null && !u.Blocked && u.Role is UserRole.Admin or UserRole.Support);
        var polling = s.CustomerBotEnabled && token.Length > 0 && _cluster?.Role is not (ClusterRole.Standby or ClusterRole.Recovering);
        var card = _store.GetPaymentMethods().Where(m => m.IsActive && m.Type == PaymentType.Card && !string.IsNullOrWhiteSpace(m.Value))
            .OrderBy(m => m.SortOrder).FirstOrDefault();
        var cardLabel = card is null ? null
            : $"{card.Title} — {card.Holder} (…{(card.Value.Trim().Length > 4 ? card.Value.Trim()[^4..] : card.Value.Trim())})";
        var sellable = _store.GetProducts().Count(TelegramCustomerBot.GuestSellable);
        return new CustomerBotStatusDto(s.CustomerBotEnabled, s.CustomerBotPublic, token.Length > 0, Hint(token),
            s.CustomerBotUsername ?? "", linked, polling, s.CustomerBotShop, TelegramCustomerBot.ShopUrl, null, s.CustomerBotCodeMinutes,
            s.CustomerBotSales, cardLabel, sellable, s.CustomerBotAdmin, staffLinked);
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
        if (input.CodeMinutes is < 1 or > 1440)
            return BadRequest("اعتبار کد اتصال باید بین ۱ تا ۱۴۴۰ دقیقه باشد.");
        _store.SetCustomerBot(input.Enabled, input.Public, token, username, input.Shop, input.CodeMinutes, input.Sales, input.Admin);
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

// Available: shown in a normal browser (staff switched it on for customers). Shop: the shop inside Telegram is
// on — inside it, the same page is shown. Email / EmailVerified: where the code goes, and whether it can.
public record TelegramLinkStatusDto(bool Available, string? BotUsername, bool Linked, string? TelegramUsername, bool Notify,
    bool Shop = false, string Email = "", bool EmailVerified = false);
public record TelegramNotifyInput(bool Notify);
// The Mini App's launch data (Telegram.WebApp.initData), as Telegram signed it.
public record TelegramInitDataInput(string InitData);
// Linking from inside the shop: the launch data names the Telegram, the mailed code proves the inbox.
public record TelegramShopLinkInput(string InitData, string Code);
public record TelegramCodeSentDto(string BotUrl, string Email, int Minutes);

[ApiController]
[Route("api/account/telegram")]
[Authorize]
public class AccountTelegramController : ControllerBase
{
    private readonly IDataStore _store;
    private readonly ITelegramCustomerBot _bot;
    private readonly IUserMailer _mailer;
    private readonly IMemoryCache _cache;

    public AccountTelegramController(IDataStore store, ITelegramCustomerBot bot, IUserMailer mailer, IMemoryCache cache)
    {
        _store = store;
        _bot = bot;
        _mailer = mailer;
        _cache = cache;
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
        var shop = TelegramCustomerBot.ShopToken(_store) is not null;
        return new TelegramLinkStatusDto(available, username ?? (shop ? _store.GetTelegramSettings().CustomerBotUsername : null),
            user.TelegramChatId is not null, user.TelegramUsername, user.TelegramNotify, shop,
            string.IsNullOrWhiteSpace(user.Email) ? "" : AccountController.MaskEmail(user.Email), user.EmailVerified);
    }

    // How often a customer may have a code mailed: once a minute, five times an hour. Enough to recover from a
    // lost email; not enough to turn the button into a way of flooding an inbox.
    private static readonly TimeSpan CodeCooldown = TimeSpan.FromMinutes(1);
    private const int CodesPerHour = 5;
    private sealed record CodeSends(DateTime WindowStart, int Count, DateTime Last);

    // Step one of linking: a one-time code goes to the account's own verified inbox. The customer then sends it
    // to the bot (or, inside the shop, types it on this page). Asking again replaces the previous code.
    [HttpPost("code")]
    public async Task<ActionResult<TelegramCodeSentDto>> SendCode()
    {
        if (this.CurrentUserId() is not int id || _store.GetUser(id) is not { } user) return Unauthorized();
        var settings = _store.GetTelegramSettings();
        var botUsername = (settings.CustomerBotUsername ?? "").Trim();
        if (!(Offer().Available || TelegramCustomerBot.ShopToken(_store) is not null) || botUsername.Length == 0)
            return BadRequest("اتصال به تلگرام در حال حاضر فعال نیست.");
        // The code proves the inbox, so the inbox must be proven to be the owner's: an unverified address could
        // be anyone's, and its owner could then attach their own Telegram to this account.
        if (string.IsNullOrWhiteSpace(user.Email) || !user.EmailVerified)
            return BadRequest("برای اتصال به تلگرام، ابتدا ایمیل حساب خود را تأیید کنید.");

        var key = $"tg-code-sends:{id}";
        var now = DateTime.UtcNow;
        var sends = _cache.Get<CodeSends>(key);
        if (sends is not null && now - sends.WindowStart >= TimeSpan.FromHours(1)) sends = null;
        if (sends is not null && now - sends.Last < CodeCooldown)
            return StatusCode(429, "کد همین الان ارسال شد. یک دقیقه صبر کنید و ایمیل خود (و پوشه‌ی اسپم) را بررسی کنید.");
        if (sends is not null && sends.Count >= CodesPerHour)
            return StatusCode(429, JalaliDate.ToPersianDigits($"در هر ساعت حداکثر {CodesPerHour} بار می‌توانید کد بگیرید. کمی بعد دوباره تلاش کنید."));
        _cache.Set(key, new CodeSends(sends?.WindowStart ?? now, (sends?.Count ?? 0) + 1, now), TimeSpan.FromHours(1));

        var minutes = Math.Clamp(settings.CustomerBotCodeMinutes, 1, 1440);
        var code = _store.CreateTelegramLinkCode(id, TimeSpan.FromMinutes(minutes));
        if (!await _mailer.TelegramLinkCodeAsync(user, code, minutes, botUsername))
            return StatusCode(502, "ارسال ایمیل انجام نشد. چند دقیقه‌ی دیگر دوباره تلاش کنید.");
        return new TelegramCodeSentDto($"https://t.me/{botUsername}?start=connect", AccountController.MaskEmail(user.Email), minutes);
    }

    // Linking from INSIDE the shop in Telegram: Telegram's signed launch data names the Telegram account, and the
    // code from this account's inbox proves the rest — the same proof the bot asks for, typed here instead.
    [HttpPost("webapp")]
    public async Task<IActionResult> LinkFromShop(TelegramShopLinkInput input, CancellationToken ct)
    {
        if (this.CurrentUserId() is not int id || _store.GetUser(id) is not { } user) return Unauthorized();
        if (TelegramCustomerBot.ShopToken(_store) is not { } token) return BadRequest("فروشگاه داخل تلگرام در حال حاضر فعال نیست.");
        if (TelegramInitData.Validate(input.InitData, token, DateTime.UtcNow) is not { } tg)
            return BadRequest("اطلاعات تلگرام معتبر نیست یا منقضی شده. فروشگاه را از ربات دوباره باز کنید.");
        // The code has to be THIS account's: a code mailed to someone else links nothing here (and is spent).
        if (TelegramCustomerBot.NormalizeCode(input.Code) is not { } code || _store.ConsumeToken(code, TelegramCustomerBot.CodePurpose) != id)
            return BadRequest("این کد درست نیست یا منقضی شده است. کد تازه بگیرید.");
        if (!_store.LinkTelegram(id, tg.Id, tg.Username)) return BadRequest("اتصال انجام نشد؛ لطفاً دوباره تلاش کنید.");
        var name = string.IsNullOrWhiteSpace(user.Name) ? user.Username : user.Name;
        await _bot.SendAsync(tg.Id,
            $"✅ حساب «{name}» ({user.Username}) در فونیکس وریفای به این تلگرام وصل شد.\n\nاز این پس فروشگاه داخل تلگرام بدون ورود دوباره باز می‌شود و اطلاعات سفارش‌ها هم اینجا برایتان ارسال می‌شود.\n\nاگر این حساب شما نیست: /stop", ct);
        return NoContent();
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

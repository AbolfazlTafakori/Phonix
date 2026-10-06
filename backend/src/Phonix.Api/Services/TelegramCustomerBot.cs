using System.Text.Json;
using Phonix.Api.Data;
using Phonix.Api.Models;

namespace Phonix.Api.Services;

// The customer-facing Telegram bot. A customer links their site account from their account page: the site
// hands them a one-time t.me link (?start=<token>), and opening it makes Telegram send "/start <token>" to
// the bot, which ties that chat to the account. From then on the account's mail (deliveries, payment
// decisions, rejections, staff messages) is also sent to the chat — see UserMailer.
//
// The token is the whole proof: it was minted for the signed-in account, lives 15 minutes and works once,
// so a chat can only ever be linked by someone who was logged into that account a moment ago.
public interface ITelegramCustomerBot
{
    // Configured and switched on — sending is possible. (Showing the link to customers is a separate switch.)
    bool IsActive { get; }

    // Long-polls one getUpdates cycle and handles /start, /stop and anything else typed to the bot. Returns
    // the next offset to poll from.
    Task<long> ProcessUpdatesAsync(long offset, CancellationToken ct = default);

    // One message to one linked chat, as plain text. False when Telegram refused it; a chat that blocked the
    // bot is unlinked on the spot, so nobody keeps writing into a closed door.
    Task<bool> SendAsync(long chatId, string text, CancellationToken ct = default);

    // Asks Telegram who a token belongs to (getMe): the bot's @username, or Telegram's own error.
    Task<(bool Ok, string? Username, string? Error)> CheckTokenAsync(string token, CancellationToken ct = default);

    // Points the bot's menu button at the shop while the shop is on, and back to Telegram's default when it is
    // off. Error is what to tell staff when Telegram refused.
    Task<(bool Ok, string? Error)> SyncMenuButtonAsync(CancellationToken ct = default);
}

public sealed class TelegramCustomerBot : ITelegramCustomerBot
{
    public const string LinkPurpose = "tg-link";
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(15);
    // Telegram's own ceiling for a message.
    private const int MaxMessage = 4000;

    private readonly IDataStore _store;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<TelegramCustomerBot> _logger;

    public TelegramCustomerBot(IDataStore store, IHttpClientFactory httpFactory, ILogger<TelegramCustomerBot> logger)
    {
        _store = store;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    private static string FrontendUrl => Environment.GetEnvironmentVariable("PHONIX_FRONTEND_URL") ?? "http://localhost:3000";

    // Where the shop opens inside Telegram: the site itself. Telegram only opens Mini Apps over HTTPS, so on a
    // plain-http address (a dev machine) there is no shop to offer.
    public static string? ShopUrl =>
        Uri.TryCreate(FrontendUrl.TrimEnd('/') + "/", UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? uri.ToString()
            : null;

    private string? ActiveToken()
    {
        var s = _store.GetTelegramSettings();
        var token = (s.CustomerBotToken ?? "").Trim();
        return s.CustomerBotEnabled && token.Length > 0 ? token : null;
    }

    public bool IsActive => ActiveToken() is not null;

    // The bot token while the shop inside Telegram is on and can actually open — what a Mini App's launch
    // data is checked against. Null otherwise, which is also "no signing in from Telegram".
    public static string? ShopToken(IDataStore store)
    {
        var s = store.GetTelegramSettings();
        var token = (s.CustomerBotToken ?? "").Trim();
        return s.CustomerBotEnabled && s.CustomerBotShop && token.Length > 0 && ShopUrl is not null ? token : null;
    }

    private bool ShopOn => ShopToken(_store) is not null;

    // An inline button that opens the shop inside Telegram, or nothing while the shop is off.
    private string? ShopKeyboard(string label) => ShopOn
        ? JsonSerializer.Serialize(new { inline_keyboard = new[] { new[] { new { text = label, web_app = new { url = ShopUrl } } } } })
        : null;

    public async Task<long> ProcessUpdatesAsync(long offset, CancellationToken ct = default)
    {
        if (ActiveToken() is not { } token) return offset;

        var url = $"https://api.telegram.org/bot{token}/getUpdates?offset={offset}&timeout=25&allowed_updates=%5B%22message%22%5D";
        using var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(35);
        using var resp = await http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("Customer bot getUpdates failed: {Status}", (int)resp.StatusCode);
            return offset;
        }

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("result", out var results) || results.ValueKind != JsonValueKind.Array)
            return offset;

        var next = offset;
        foreach (var update in results.EnumerateArray())
        {
            if (update.TryGetProperty("update_id", out var uid) && uid.TryGetInt64(out var id) && id >= next)
                next = id + 1;
            if (update.TryGetProperty("message", out var msg))
            {
                try { await HandleMessageAsync(token, msg, ct); }
                catch (Exception ex) { _logger.LogWarning(ex, "Customer bot message handling failed"); }
            }
        }
        return next;
    }

    private async Task HandleMessageAsync(string token, JsonElement msg, CancellationToken ct)
    {
        // Private chats only: an account's mail must never land in a group a bot was added to.
        if (!msg.TryGetProperty("chat", out var chat) || !chat.TryGetProperty("id", out var cid) || !cid.TryGetInt64(out var chatId))
            return;
        if (!chat.TryGetProperty("type", out var type) || type.GetString() != "private") return;

        var text = (msg.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "").Trim();
        var username = msg.TryGetProperty("from", out var from) && from.TryGetProperty("username", out var un) ? un.GetString() : null;
        var command = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var head = command.Length > 0 ? command[0].Split('@')[0].ToLowerInvariant() : "";

        if (head == "/start" && command.Length > 1)
        {
            await LinkAsync(token, chatId, command[1].Trim(), username, ct);
            return;
        }
        if (head is "/stop" or "/unlink")
        {
            var linked = _store.FindUserByTelegramChat(chatId);
            _store.UnlinkTelegramChat(chatId);
            await ReplyAsync(token, chatId, linked is null
                ? "این تلگرام به حسابی وصل نیست."
                : "اتصال حساب شما به این تلگرام قطع شد. هر زمان خواستید می‌توانید از حساب کاربری سایت دوباره وصل کنید.", ct);
            return;
        }

        var user = _store.FindUserByTelegramChat(chatId);
        var shop = ShopKeyboard("🛒 ورود به فروشگاه");
        if (head == "/shop" && shop is not null)
        {
            await ReplyAsync(token, chatId, "فروشگاه فونیکس وریفای، داخل همین تلگرام:", ct, shop);
            return;
        }
        string reply;
        if (user is not null)
            reply = $"حساب «{DisplayName(user)}» به این تلگرام وصل است ✅\nاطلاعات سفارش‌ها و پیام‌های حساب شما اینجا ارسال می‌شود."
                    + (shop is not null ? "\nبا دکمه‌ی زیر، فروشگاه بدون ورود دوباره باز می‌شود." : "")
                    + $"\n\nبرای قطع اتصال: /stop\nحساب کاربری: {FrontendUrl}/account";
        else if (shop is not null)
            reply = "سلام! 👋\nاین ربات فونیکس وریفای است.\n\nبا دکمه‌ی زیر فروشگاه داخل تلگرام باز می‌شود. یک بار وارد حساب خود شوید و در «حساب کاربری» گزینه‌ی «اتصال همین تلگرام» را بزنید؛ از آن به بعد خودکار وارد می‌شوید و اطلاعات سفارش‌ها هم اینجا برایتان می‌آید.";
        else
            reply = $"سلام! 👋\nاین ربات فونیکس وریفای است.\n\nبرای دریافت اطلاعات سفارش‌ها و پیام‌ها در تلگرام، وارد حساب کاربری سایت شوید و «اتصال به تلگرام» را بزنید:\n{FrontendUrl}/account";
        await ReplyAsync(token, chatId, reply, ct, shop);
    }

    private async Task LinkAsync(string token, long chatId, string linkToken, string? username, CancellationToken ct)
    {
        // The start payload is the one-time token itself. Anything else — an old link, a guessed value — is
        // simply not a token and links nothing.
        if (_store.ConsumeToken(linkToken, LinkPurpose) is not int userId || _store.GetUser(userId) is not { } user)
        {
            await ReplyAsync(token, chatId,
                $"این لینک اتصال نامعتبر است یا منقضی شده.\nاز حساب کاربری سایت دوباره «اتصال به تلگرام» را بزنید:\n{FrontendUrl}/account", ct);
            return;
        }
        if (!_store.LinkTelegram(userId, chatId, username))
        {
            await ReplyAsync(token, chatId, "اتصال انجام نشد؛ لطفاً دوباره تلاش کنید.", ct);
            return;
        }
        _logger.LogInformation("Customer bot: user {UserId} linked a Telegram chat", userId);
        await ReplyAsync(token, chatId,
            $"✅ حساب «{DisplayName(user)}» در فونیکس وریفای به این تلگرام وصل شد.\n\nاز این پس اطلاعات سفارش‌ها، تأیید پرداخت‌ها و پیام‌های پشتیبانی اینجا هم برایتان ارسال می‌شود.\n\nبرای قطع اتصال: /stop",
            ct, ShopKeyboard("🛒 ورود به فروشگاه"));
    }

    private static string DisplayName(AppUser u) => string.IsNullOrWhiteSpace(u.Name) ? u.Username : u.Name;

    public async Task<bool> SendAsync(long chatId, string text, CancellationToken ct = default)
    {
        if (ActiveToken() is not { } token) return false;
        return await ReplyAsync(token, chatId, text, ct);
    }

    private async Task<bool> ReplyAsync(string token, long chatId, string text, CancellationToken ct, string? replyMarkup = null)
    {
        if (text.Length > MaxMessage) text = text[..MaxMessage] + "…";
        try
        {
            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(20);
            var fields = new Dictionary<string, string>
            {
                ["chat_id"] = chatId.ToString(),
                ["text"] = text,
                ["disable_web_page_preview"] = "true",
            };
            if (replyMarkup is not null) fields["reply_markup"] = replyMarkup;
            using var form = new FormUrlEncodedContent(fields);
            using var resp = await http.PostAsync($"https://api.telegram.org/bot{token}/sendMessage", form, ct);
            if (resp.IsSuccessStatusCode) return true;
            // 403 when the customer blocked the bot or their account is gone: stop writing into a closed door.
            // Not for "can't initiate conversation" — someone who linked from inside the shop may simply never
            // have pressed Start, and their link still signs them in there.
            if ((int)resp.StatusCode == 403)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                if (body.Contains("blocked", StringComparison.OrdinalIgnoreCase)
                    || body.Contains("deactivated", StringComparison.OrdinalIgnoreCase))
                    _store.UnlinkTelegramChat(chatId);
            }
            _logger.LogWarning("Customer bot sendMessage failed: {Status}", (int)resp.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Customer bot sendMessage call failed");
            return false;
        }
    }

    public async Task<(bool Ok, string? Username, string? Error)> CheckTokenAsync(string token, CancellationToken ct = default)
    {
        token = (token ?? "").Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(token, @"^\d{5,15}:[A-Za-z0-9_-]{30,64}$"))
            return (false, null, "قالب توکن درست نیست. توکن را کامل از BotFather کپی کنید (مثل 123456789:ABC...).");
        try
        {
            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(15);
            using var resp = await http.GetAsync($"https://api.telegram.org/bot{token}/getMe", ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            if (resp.IsSuccessStatusCode && doc.RootElement.TryGetProperty("result", out var r)
                && r.TryGetProperty("username", out var u) && u.GetString() is { Length: > 0 } name)
                return (true, name, null);
            var description = doc.RootElement.TryGetProperty("description", out var d) ? d.GetString() : null;
            return (false, null, (int)resp.StatusCode == 401
                ? "تلگرام این توکن را نپذیرفت (Unauthorized). توکن را دوباره از BotFather بگیرید."
                : $"تلگرام خطا داد: {description ?? ((int)resp.StatusCode).ToString()}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Customer bot getMe failed");
            return (false, null, "ارتباط با تلگرام برقرار نشد. کمی بعد دوباره امتحان کنید.");
        }
    }

    public async Task<(bool Ok, string? Error)> SyncMenuButtonAsync(CancellationToken ct = default)
    {
        var s = _store.GetTelegramSettings();
        var token = (s.CustomerBotToken ?? "").Trim();
        if (token.Length == 0) return (true, null);
        if (s.CustomerBotShop && ShopUrl is null)
            return (false, "آدرس سایت (PHONIX_FRONTEND_URL) باید https باشد؛ تلگرام فروشگاه را فقط روی https باز می‌کند.");

        // No chat_id: this sets the button every private chat with the bot gets.
        var button = s.CustomerBotShop
            ? JsonSerializer.Serialize(new { type = "web_app", text = "🛒 فروشگاه", web_app = new { url = ShopUrl } })
            : JsonSerializer.Serialize(new { type = "default" });
        try
        {
            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(15);
            using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["menu_button"] = button });
            using var resp = await http.PostAsync($"https://api.telegram.org/bot{token}/setChatMenuButton", form, ct);
            if (resp.IsSuccessStatusCode) return (true, null);
            var body = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("Customer bot setChatMenuButton failed: {Status} {Body}", (int)resp.StatusCode, body);
            return (false, $"تلگرام دکمه‌ی منوی ربات را نپذیرفت ({(int)resp.StatusCode}).");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Customer bot setChatMenuButton call failed");
            return (false, "ارتباط با تلگرام برای تنظیم دکمه‌ی فروشگاه برقرار نشد. «تست اتصال» را دوباره بزنید.");
        }
    }
}

// Polls the customer bot while it is switched on — and only on the node that serves traffic. A Standby has
// the same settings by sync; if it polled too, Telegram would hand each update to whichever node asked first,
// and a link made on the read-only Standby could not even be saved.
public sealed class TelegramCustomerBotWorker : BackgroundService
{
    private readonly ITelegramCustomerBot _bot;
    private readonly IClusterSyncService? _cluster;
    private readonly ILogger<TelegramCustomerBotWorker> _logger;
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(10);
    private long _offset;

    public TelegramCustomerBotWorker(ITelegramCustomerBot bot, ILogger<TelegramCustomerBotWorker> logger, IClusterSyncService? cluster = null)
    {
        _bot = bot;
        _cluster = cluster;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_cluster?.Role is ClusterRole.Standby or ClusterRole.Recovering || !_bot.IsActive)
                {
                    await Task.Delay(IdleDelay, stoppingToken);
                    continue;
                }
                var next = await _bot.ProcessUpdatesAsync(_offset, stoppingToken);
                if (next == _offset) await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                else _offset = next;
            }
            catch (TaskCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Customer bot poll cycle failed");
                try { await Task.Delay(ErrorDelay, stoppingToken); }
                catch (TaskCanceledException) { break; }
            }
        }
    }
}

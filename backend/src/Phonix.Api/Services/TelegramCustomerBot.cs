using System.Text.Json;
using Phonix.Api.Data;
using Phonix.Api.Models;

namespace Phonix.Api.Services;

// The customer-facing Telegram bot. A customer links their site account from «اتصال به تلگرام» in their account
// menu: the site mails a one-time 24-digit code to the account's verified address and opens the bot, and the
// customer sends that code to the bot, which ties the chat to the account. From then on the account's mail
// (deliveries, payment decisions, rejections, staff messages) is also sent to the chat — see UserMailer.
//
// The code is the whole proof: only the signed-in owner can have one mailed, it reaches only the account's own
// inbox, it lives as long as staff set and it works once.
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

    // A notice about one of a customer's orders — a delivery, a decision, a service running out — to their chat
    // (linked, or the one they bought with in the bot), with the buttons to act on it: the order itself, and a
    // renewal when that is what it is about (one service renewed here, a page of the site, or buying the product
    // again). False when it was not sent: no chat, notices turned off, or Telegram refused.
    Task<bool> NotifyOrderAsync(AppUser user, string text, int orderId, string? renewSitePath = null, int? renewProductId = null,
        int? renewUnitId = null, CancellationToken ct = default);
}

public sealed partial class TelegramCustomerBot : ITelegramCustomerBot
{
    public const string CodePurpose = "tg-code";
    public const int CodeLength = 24;
    // Telegram's own ceiling for a message.
    private const int MaxMessage = 4000;

    private readonly IDataStore _store;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<TelegramCustomerBot> _logger;
    private readonly IFileStorageService? _files;
    // The receipt bot, the support bot and the mailer all reach back to this bot (the mailer sends through it), so
    // they are looked up when needed rather than injected — injecting them would make a construction cycle.
    private readonly IServiceProvider? _services;

    public TelegramCustomerBot(IDataStore store, IHttpClientFactory httpFactory, ILogger<TelegramCustomerBot> logger,
        IFileStorageService? files = null, IServiceProvider? services = null)
    {
        _store = store;
        _httpFactory = httpFactory;
        _logger = logger;
        _files = files;
        _services = services;
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
    private string? ShopKeyboard(string label) => ShopOn ? Inline(new[] { new[] { new B(label, WebApp: ShopUrl, Style: Blue) } }) : null;

    public async Task<long> ProcessUpdatesAsync(long offset, CancellationToken ct = default)
    {
        if (ActiveToken() is not { } token) return offset;

        var url = $"https://api.telegram.org/bot{token}/getUpdates?offset={offset}&timeout=25&allowed_updates=%5B%22message%22%2C%22callback_query%22%5D";
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
            try
            {
                if (update.TryGetProperty("callback_query", out var cq)) await HandleShopCallbackAsync(token, cq, ct);
                else if (update.TryGetProperty("message", out var msg)) await HandleMessageAsync(token, msg, ct);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Customer bot update handling failed"); }
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
        msg.TryGetProperty("from", out var from);
        var username = from.ValueKind == JsonValueKind.Object && from.TryGetProperty("username", out var un) ? un.GetString() : null;
        var command = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var head = command.Length > 0 ? command[0].Split('@')[0].ToLowerInvariant() : "";

        if (NormalizeCode(text) is { } code)
        {
            await LinkAsync(token, chatId, code, username, ct);
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
        // The menu, a receipt photo for a purchase in progress, something the bot asked to be typed.
        if (await TryHandleShopMessageAsync(token, chatId, msg, text, from, ct)) return;

        var user = _store.FindUserByTelegramChat(chatId);
        var shop = ShopKeyboard("🛒 ورود به فروشگاه");
        if (head == "/shop" && shop is not null)
        {
            await ReplyAsync(token, chatId, "فروشگاه فونیکس وریفای، داخل همین تلگرام:", ct, shop);
            return;
        }
        if (head == "/orders")
        {
            await ShowOrdersAsync(token, new Screen(chatId), 1, ct);
            return;
        }
        // Digits that aren't a whole code: most likely a code cut short while copying.
        if (LooksLikeAPartialCode(text))
        {
            await ReplyAsync(token, chatId, $"کد اتصال باید {CodeLengthFa} رقم باشد. کد را کامل از ایمیل کپی کنید و دوباره بفرستید.", ct);
            return;
        }
        // Arriving from «اتصال به تلگرام» on the site: the code is already on its way, so just ask for it.
        if (head == "/start" && command.Length > 1 && command[1].Trim() == "connect")
        {
            await ReplyAsync(token, chatId,
                $"🔐 کد {CodeLengthFa} رقمی‌ای را که به ایمیل حسابتان ارسال شد، همین‌جا بفرستید.\n\nاگر ایمیل را نمی‌بینید، پوشه‌ی اسپم را هم نگاه کنید."
                + (user is not null ? $"\n\n(این تلگرام الان به حساب «{DisplayName(user)}» وصل است؛ با کد تازه به حساب جدید منتقل می‌شود.)" : ""), ct);
            return;
        }
        // /start, or anything the bot wasn't waiting for: the main menu. /start also lays the keyboard under the
        // message box — a message carries either that keyboard or the menu's buttons, never both.
        if (head == "/start")
            await ReplyAsync(token, chatId, $"✨ ربات فونیکس وریفای آماده است. منوی اصلی همیشه با دکمه‌ی «{MenuHome}» پایین صفحه باز می‌شود.",
                ct, HomeKeyboard());
        await ShowHomeAsync(token, new Screen(chatId), FirstName(from), ct);
    }

    private async Task LinkAsync(string token, long chatId, string code, string? username, CancellationToken ct)
    {
        if (LockedOut(chatId))
        {
            await ReplyAsync(token, chatId, "تعداد کدهای نادرست زیاد بود. چند دقیقه‌ی دیگر دوباره امتحان کنید.", ct);
            return;
        }
        // The code is the whole proof: it was mailed to the account's own verified address at the signed-in
        // owner's request, lives as long as staff set, and works once.
        if (_store.ConsumeToken(code, CodePurpose) is not int userId || _store.GetUser(userId) is not { } user)
        {
            NoteFailure(chatId);
            await ReplyAsync(token, chatId,
                "این کد درست نیست یا منقضی شده است.\nاز منوی حساب کاربری سایت، «اتصال به تلگرام» را دوباره بزنید تا کد تازه‌ای به ایمیلتان ارسال شود.", ct);
            return;
        }
        if (!_store.LinkTelegram(userId, chatId, username))
        {
            await ReplyAsync(token, chatId, "اتصال انجام نشد؛ لطفاً دوباره تلاش کنید.", ct);
            return;
        }
        Failures.TryRemove(chatId, out _);
        _logger.LogInformation("Customer bot: user {UserId} linked a Telegram chat", userId);
        var next = new List<object[]>();
        if (ShopOn) next.Add(new[] { new B("🛒 ورود به فروشگاه", WebApp: ShopUrl, Style: Blue) });
        next.Add(HomeRow());
        await ReplyAsync(token, chatId,
            $"✅ حساب «{DisplayName(user)}» ({user.Username}) در فونیکس وریفای به این تلگرام وصل شد.\n\nاز این پس اطلاعات سفارش‌ها، تأیید پرداخت‌ها و پیام‌های پشتیبانی اینجا هم برایتان ارسال می‌شود.\n\nاگر این حساب شما نیست: /stop",
            ct, Inline(next));
    }

    // ── the link code ──

    private static string CodeLengthFa => JalaliDate.ToPersianDigits(CodeLength.ToString());

    // A code arrives copied from an email or typed on a Persian keyboard: grouped with spaces or dashes, maybe
    // in Persian or Arabic digits. All of that is the same code. Anything else is not a code.
    public static string? NormalizeCode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 80) return null;
        var digits = new System.Text.StringBuilder(CodeLength);
        foreach (var c in text)
        {
            if (c is >= '0' and <= '9') digits.Append(c);
            else if (c is >= '\u06F0' and <= '\u06F9') digits.Append((char)('0' + (c - '\u06F0')));
            else if (c is >= '\u0660' and <= '\u0669') digits.Append((char)('0' + (c - '\u0660')));
            else if (!IsSeparator(c)) return null;
        }
        return digits.Length == CodeLength ? digits.ToString() : null;
    }

    private static bool IsSeparator(char c) =>
        char.IsWhiteSpace(c) || c is '-' or '_' or '.' or '\u200C' or '\u200E' or '\u200F';

    private static bool LooksLikeAPartialCode(string text) =>
        text.Count(char.IsDigit) >= 8 && text.All(c => char.IsDigit(c) || IsSeparator(c));

    // Wrong codes per chat. With 10^24 possible codes guessing is hopeless anyway; this only stops one chat
    // from turning the bot into a code-checking loop.
    private const int MaxFailures = 5;
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, (int Count, DateTime Since)> Failures = new();

    private static bool LockedOut(long chatId) =>
        Failures.TryGetValue(chatId, out var f) && f.Count >= MaxFailures && DateTime.UtcNow - f.Since < FailureWindow;

    private static void NoteFailure(long chatId)
    {
        Failures.AddOrUpdate(chatId, _ => (1, DateTime.UtcNow),
            (_, f) => DateTime.UtcNow - f.Since >= FailureWindow ? (1, DateTime.UtcNow) : (f.Count + 1, f.Since));
        // Every chat that ever sent a wrong code would otherwise stay here for the life of the process.
        if (Failures.Count > 1000)
            foreach (var (chat, f) in Failures)
                if (DateTime.UtcNow - f.Since >= FailureWindow) Failures.TryRemove(chat, out _);
    }

    private static string DisplayName(AppUser u) => string.IsNullOrWhiteSpace(u.Name) ? u.Username : u.Name;

    public async Task<bool> SendAsync(long chatId, string text, CancellationToken ct = default)
    {
        if (ActiveToken() is not { } token) return false;
        return await ReplyAsync(token, chatId, text, ct);
    }

    private async Task<bool> ReplyAsync(string token, long chatId, string text, CancellationToken ct, string? replyMarkup = null, bool html = false)
    {
        // Cut short, a formatted message could end inside a tag, which Telegram refuses whole: it goes plain.
        if (text.Length > MaxMessage && html)
        {
            text = PlainOf(text);
            html = false;
        }
        if (text.Length > MaxMessage) text = text[..MaxMessage] + "…";
        var fields = new Dictionary<string, string>
        {
            ["chat_id"] = chatId.ToString(),
            ["text"] = text,
            ["disable_web_page_preview"] = "true",
        };
        if (replyMarkup is not null) fields["reply_markup"] = replyMarkup;
        if (html) fields["parse_mode"] = "HTML";
        var (ok, status, body) = await PostWithKeyboardAsync(token, "sendMessage", fields, ct);
        if (ok) return true;
        // 403 when the customer blocked the bot or their account is gone: stop writing into a closed door.
        // Not for "can't initiate conversation" — someone who linked from inside the shop may simply never
        // have pressed Start, and their link still signs them in there.
        if (status == 403 && (body.Contains("blocked", StringComparison.OrdinalIgnoreCase)
                              || body.Contains("deactivated", StringComparison.OrdinalIgnoreCase)))
            _store.UnlinkTelegramChat(chatId);
        return false;
    }

    public Task<(bool Ok, string? Username, string? Error)> CheckTokenAsync(string token, CancellationToken ct = default) =>
        TelegramTokenCheck.CheckAsync(_httpFactory, token, _logger, ct);

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

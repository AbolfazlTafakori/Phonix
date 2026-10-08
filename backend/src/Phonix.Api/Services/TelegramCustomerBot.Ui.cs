using System.Text.Json;
using Phonix.Api.Models;

namespace Phonix.Api.Services;

// How the customer bot looks and moves. The message a button was pressed on is the screen: every button edits it
// in place rather than stacking a new message under it, so the chat stays one live menu plus the things worth
// keeping (receipts, deliveries, notices). Details are laid out as two-column [value | label] button tables that
// read like a card on a phone, every screen ends with the same way back, and the keyboard under the message box
// carries just «منوی اصلی», so the menu is one tap away after any notification.
public sealed partial class TelegramCustomerBot
{
    private const string MenuHome = "🏠 منوی اصلی";
    // Telegram's ceiling for a picture's caption, with room to spare.
    private const int MaxCaption = 1000;

    // Where a screen goes: the message a button was pressed on (edited in place), or a new message.
    private readonly record struct Screen(long ChatId, int? MessageId = null, bool IsPhoto = false)
    {
        public Screen Fresh => new(ChatId);
    }

    private static string Site => FrontendUrl.TrimEnd('/');

    // ── Buttons ─────────────────────────────────────────────────────────────────────────────────────────────

    private static object Btn(string text, string data) => new { text, callback_data = data };

    private static object Link(string text, string url) => new { text, url };

    // A page of the site: inside Telegram while the shop is on (signed in there), in the browser otherwise.
    private object SiteButton(string text, string path) => ShopOn
        ? new { text, web_app = new { url = ShopUrl!.TrimEnd('/') + path } }
        : Link(text, Site + path);

    // One row of a details table: the value on the left, its label on the right, as a Persian reader expects.
    private static object[] Cell(string value, string label) => new[] { Btn(value, "noop"), Btn(label, "noop") };

    private static object[] HomeRow() => new[] { Btn(MenuHome, "home") };

    // The way back from a screen: one step back when there is one, and always the main menu.
    private static object[] NavRow(string? back, string backText = "⬅️ بازگشت") =>
        back is null ? HomeRow() : new[] { Btn(backText, back), Btn(MenuHome, "home") };

    private static IEnumerable<object[]> Pairs(IEnumerable<object> buttons) => buttons.Chunk(2);

    private static string Inline(IEnumerable<object[]> rows) => JsonSerializer.Serialize(new { inline_keyboard = rows });

    private static string HomeOnly() => Inline(new[] { HomeRow() });

    // The keyboard under the message box. Replaces the four-button one earlier versions left in people's chats.
    private static string HomeKeyboard() => JsonSerializer.Serialize(new
    {
        keyboard = new[] { new[] { new { text = MenuHome } } },
        resize_keyboard = true,
        is_persistent = true,
    });

    // ── Text ────────────────────────────────────────────────────────────────────────────────────────────────

    // What Telegram's HTML needs escaped, and nothing else: Persian text passes through as it is.
    private static string H(string? text) => (text ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string Fa(long n) => JalaliDate.ToPersianDigits(n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)).Replace(",", "٬");

    private static string Toman(long n) => $"{Fa(n)} تومان";

    // A button shows one line, and a long name pushes the price off its edge.
    private static string Short(string? text, int max = 28)
    {
        var t = (text ?? "").Trim();
        return t.Length <= max ? t : t[..(max - 1)].TrimEnd() + "…";
    }

    private static string PlainOf(string html) =>
        System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", "").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&");

    // ── The main menu ───────────────────────────────────────────────────────────────────────────────────────

    private string HomeText(long chatId, string? firstName)
    {
        var user = _store.FindUserByTelegramChat(chatId);
        var name = user is not null ? DisplayName(user) : (firstName ?? "").Trim();
        var lines = new List<string>
        {
            $"سلام{(name.Length > 0 ? " " + name : "")} 👋",
            "به ربات فونیکس وریفای خوش آمدید.",
            "",
            "🛍 همه‌ی محصولات با پلن و قیمت، دسته‌بندی‌شده",
        };
        if (SalesOn && _store.GetProducts().Any(GuestSellable)) lines.Add("🔓 خرید محصولات 🔓 همین‌جا و بدون ثبت‌نام");
        lines.Add("📦 پیگیری سفارش و تحویل اکانت در همین چت");
        lines.Add("💬 پشتیبانی مستقیم از داخل ربات");
        lines.Add("");
        lines.Add(user is not null
            ? $"✅ حساب سایت «{DisplayName(user)}» به این تلگرام وصل است."
            : "🔗 حساب سایت به این تلگرام وصل نیست؛ از «👤 حساب من» وصلش کنید.");
        lines.Add("");
        lines.Add("یکی از گزینه‌های زیر را انتخاب کنید 👇");
        return string.Join("\n", lines);
    }

    private string MainMenu(long chatId)
    {
        var user = _store.FindUserByTelegramChat(chatId);
        var rows = new List<object[]>
        {
            new[] { Btn("🛍 خرید محصول", "shop:cats"), Btn("📦 سفارش‌های من", "ord:l:1") },
        };
        if (ShopOn) rows.Add(new object[] { new { text = "🛒 فروشگاه کامل سایت", web_app = new { url = ShopUrl } } });
        rows.Add(new[] { Btn("👤 حساب من", "acc"), Btn("💬 پشتیبانی", "sup") });
        var more = new List<object> { Btn("📚 آموزش‌ها", "tut") };
        if (user is not null && _store.GetSettings().ReferralCommissionPercent > 0) more.Add(Btn("🎁 دعوت دوستان", "acc:ref"));
        rows.Add(more.ToArray());
        if (StaffFor(chatId) is not null) rows.Add(new[] { Btn("⚙️ مدیریت فروشگاه", "adm") });
        return Inline(rows);
    }

    private Task ShowHomeAsync(string token, Screen at, string? firstName, CancellationToken ct)
    {
        Sessions.TryRemove(at.ChatId, out _);
        return ShowAsync(token, at, HomeText(at.ChatId, firstName), MainMenu(at.ChatId), ct);
    }

    // ── Showing a screen ────────────────────────────────────────────────────────────────────────────────────

    // Edits the message the button was on, or sends the screen as a new one when there is none, or when it can't
    // be edited into this (a picture can't become text, an old message can't be edited at all) — in which case the
    // old one is removed, so there is only ever one live menu.
    private async Task ShowAsync(string token, Screen at, string text, string markup, CancellationToken ct,
        bool html = false, string? photo = null)
    {
        if (photo is not null && text.Length > MaxCaption) photo = null;
        if (at.MessageId is int id)
        {
            if (photo is null && !at.IsPhoto && await EditTextAsync(token, at.ChatId, id, text, markup, html, ct)) return;
            if (photo is not null && at.IsPhoto && await EditPhotoAsync(token, at.ChatId, id, photo, text, markup, html, ct)) return;
        }
        var sent = (photo is not null && await SendPhotoAsync(token, at.ChatId, photo, text, markup, html, ct))
                   || await ReplyAsync(token, at.ChatId, text, ct, markup, html);
        if (sent && at.MessageId is int old) await CallAsync(token, "deleteMessage", new() { ["chat_id"] = at.ChatId.ToString(), ["message_id"] = old.ToString() }, ct);
    }

    private async Task<bool> EditTextAsync(string token, long chatId, int messageId, string text, string markup, bool html, CancellationToken ct)
    {
        if (text.Length > MaxMessage) return false;
        var fields = new Dictionary<string, string>
        {
            ["chat_id"] = chatId.ToString(), ["message_id"] = messageId.ToString(), ["text"] = text,
            ["reply_markup"] = markup, ["disable_web_page_preview"] = "true",
        };
        if (html) fields["parse_mode"] = "HTML";
        var (ok, body) = await CallAsync(token, "editMessageText", fields, ct);
        // Pressing the button of the screen already showing is not a failure.
        return ok || body.Contains("not modified", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> EditPhotoAsync(string token, long chatId, int messageId, string photo, string caption, string markup, bool html, CancellationToken ct)
    {
        var media = JsonSerializer.Serialize(html
            ? new { type = "photo", media = photo, caption, parse_mode = "HTML" }
            : (object)new { type = "photo", media = photo, caption });
        var (ok, body) = await CallAsync(token, "editMessageMedia", new()
        {
            ["chat_id"] = chatId.ToString(), ["message_id"] = messageId.ToString(), ["media"] = media, ["reply_markup"] = markup,
        }, ct);
        return ok || body.Contains("not modified", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> SendPhotoAsync(string token, long chatId, string photo, string caption, string markup, bool html, CancellationToken ct)
    {
        var fields = new Dictionary<string, string>
        {
            ["chat_id"] = chatId.ToString(), ["photo"] = photo, ["caption"] = caption, ["reply_markup"] = markup,
        };
        if (html) fields["parse_mode"] = "HTML";
        return (await CallAsync(token, "sendPhoto", fields, ct)).Ok;
    }

    // One Bot API call that may fail without consequence: false and Telegram's answer when it did.
    private async Task<(bool Ok, string Body)> CallAsync(string token, string method, Dictionary<string, string> fields, CancellationToken ct)
    {
        try
        {
            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(20);
            using var form = new FormUrlEncodedContent(fields);
            using var resp = await http.PostAsync($"https://api.telegram.org/bot{token}/{method}", form, ct);
            if (resp.IsSuccessStatusCode) return (true, "");
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!body.Contains("not modified", StringComparison.OrdinalIgnoreCase))
                _logger.LogInformation("Customer bot {Method} refused: {Status}", method, (int)resp.StatusCode);
            return (false, body);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Customer bot {Method} call failed", method);
            return (false, "");
        }
    }

    // The picture of a product, when Telegram can fetch it: a public https address on the site.
    private static string? PictureOf(Product product)
    {
        var image = string.IsNullOrWhiteSpace(product.ListImage) ? product.Image : product.ListImage;
        if (ShopUrl is not { } site || string.IsNullOrWhiteSpace(image)) return null;
        var url = image.StartsWith('/') ? site.TrimEnd('/') + image : image;
        return url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? url : null;
    }
}

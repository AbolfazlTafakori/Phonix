using System.Collections.Concurrent;
using System.Text.Json;
using Phonix.Api.Models;

namespace Phonix.Api.Services;

// How the customer bot looks and moves. The message a button was pressed on is the screen: every button edits it
// in place rather than stacking a new message under it, so the chat stays one live menu plus the things worth
// keeping (receipts, deliveries, notices). Details are laid out as two-column [value | label] button tables that
// read like a card on a phone, every screen ends with the same way back, and the keyboard under the message box
// carries just «منوی اصلی», so the menu is one tap away after any notification.
//
// Buttons are coloured by what they do (Telegram's own styles: green to go ahead, blue to move around, red to
// stop or undo), and the plain emoji a button starts with is swapped for the premium one staff set for it —
// which Telegram shows only when the bot's owner has Telegram Premium. If Telegram refuses a premium keyboard,
// the same keyboard goes out with the plain emoji and premium is left alone for a while.
public sealed partial class TelegramCustomerBot
{
    private const string MenuHome = "🏠 منوی اصلی";
    // Telegram's ceiling for a picture's caption, with room to spare.
    private const int MaxCaption = 1000;

    private const string Green = "success";
    private const string Blue = "primary";
    private const string Red = "danger";

    // Where a screen goes: the message a button was pressed on (edited in place), or a new message.
    private readonly record struct Screen(long ChatId, int? MessageId = null, bool IsPhoto = false)
    {
        public Screen Fresh => new(ChatId);
    }

    private static string Site => FrontendUrl.TrimEnd('/');

    // ── Buttons ─────────────────────────────────────────────────────────────────────────────────────────────

    // One button. Its colour and premium emoji are applied when the keyboard is built.
    private sealed record B(string Text, string? Data = null, string? Url = null, string? WebApp = null, string? Style = null);

    private static B Btn(string text, string data, string? style = null) => new(text, Data: data, Style: style);

    private static B Link(string text, string url, string? style = null) => new(text, Url: url, Style: style);

    // A page of the site: inside Telegram while the shop is on (signed in there), in the browser otherwise.
    private B SiteButton(string text, string path, string? style = null) => ShopOn
        ? new B(text, WebApp: ShopUrl!.TrimEnd('/') + path, Style: style)
        : Link(text, Site + path, style);

    // One row of a details table: the value on the left, its label on the right, as a Persian reader expects.
    private static B[] Cell(string value, string label) => new[] { Btn(value, "noop"), Btn(label, "noop") };

    private static B[] HomeRow() => new[] { Btn(MenuHome, "home") };

    // The way back from a screen: one step back when there is one, and always the main menu.
    private static B[] NavRow(string? back, string backText = "⬅️ بازگشت") =>
        back is null ? HomeRow() : new[] { Btn(backText, back), Btn(MenuHome, "home") };

    private static IEnumerable<object[]> Pairs(IEnumerable<object> buttons) => buttons.Chunk(2);

    private string HomeOnly() => Inline(new[] { HomeRow() });

    // ── Premium emoji ───────────────────────────────────────────────────────────────────────────────────────

    // The premium emoji set for each plain one, while premium is on and Telegram hasn't just refused it.
    private IReadOnlyDictionary<string, string> Look()
    {
        var s = _store.GetTelegramSettings();
        return s.CustomerBotPremium && s.CustomerBotEmoji.Count > 0 && DateTime.UtcNow - _premiumRefusedAt > PremiumPause
            ? s.CustomerBotEmoji
            : EmptyLook;
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyLook = new Dictionary<string, string>();
    private static readonly TimeSpan PremiumPause = TimeSpan.FromHours(1);
    private DateTime _premiumRefusedAt = DateTime.MinValue;
    // A premium keyboard → the same keyboard with plain emoji, for when Telegram refuses the first.
    private static readonly ConcurrentDictionary<string, string> PlainKeyboards = new();

    // The emoji a text starts with: everything before the first space, when none of it is a letter or a digit.
    private static string? LeadEmoji(string text)
    {
        var space = text.IndexOf(' ');
        if (space <= 0 || space > 10) return null;
        var lead = text[..space];
        return lead.Any(char.IsLetterOrDigit) ? null : lead;
    }

    // ⚙️ and ⚙ are the same emoji; the variation selector is how a keyboard happened to type it.
    public static string EmojiKey(string emoji) => emoji.Replace("️", "").Trim();

    private static Dictionary<string, object> ButtonJson(B b, IReadOnlyDictionary<string, string> look)
    {
        var json = new Dictionary<string, object>();
        if (LeadEmoji(b.Text) is { } lead && look.TryGetValue(EmojiKey(lead), out var id) && b.Text[lead.Length..].Trim() is { Length: > 0 } rest)
        {
            json["text"] = rest;
            json["icon_custom_emoji_id"] = id;
        }
        else json["text"] = b.Text;
        if (b.Data is not null) json["callback_data"] = b.Data;
        if (b.Url is not null) json["url"] = b.Url;
        if (b.WebApp is not null) json["web_app"] = new { url = b.WebApp };
        if (b.Style is not null) json["style"] = b.Style;
        return json;
    }

    private string Inline(IEnumerable<object[]> rows)
    {
        var list = rows.ToList();
        string Render(IReadOnlyDictionary<string, string> look) => JsonSerializer.Serialize(new
        {
            inline_keyboard = list.Select(r => r.Select(b => b is B button ? ButtonJson(button, look) : b)),
        });
        var look = Look();
        var plain = Render(EmptyLook);
        if (look.Count == 0) return plain;
        var premium = Render(look);
        if (premium != plain) Remember(premium, plain);
        return premium;
    }

    private static void Remember(string premium, string plain)
    {
        if (PlainKeyboards.Count > 400) PlainKeyboards.Clear();
        PlainKeyboards[premium] = plain;
    }

    // The keyboard under the message box. Replaces the four-button one earlier versions left in people's chats.
    private string HomeKeyboard()
    {
        string Render(IReadOnlyDictionary<string, string> look) => JsonSerializer.Serialize(new
        {
            keyboard = new[] { new[] { ButtonJson(new B(MenuHome, Style: Blue), look) } },
            resize_keyboard = true,
            is_persistent = true,
        });
        var look = Look();
        var plain = Render(EmptyLook);
        if (look.Count == 0) return plain;
        var premium = Render(look);
        if (premium != plain) Remember(premium, plain);
        return premium;
    }

    // Sends a call that carries a keyboard; when Telegram refuses the premium emoji in it, sends it again plain.
    private async Task<(bool Ok, int Status, string Body)> PostWithKeyboardAsync(string token, string method,
        Dictionary<string, string> fields, CancellationToken ct)
    {
        var first = await PostAsync(token, method, fields, ct);
        if (first.Ok || first.Status != 400 || first.Body.Contains("not modified", StringComparison.OrdinalIgnoreCase)
            || !fields.TryGetValue("reply_markup", out var markup) || !PlainKeyboards.TryGetValue(markup, out var plain))
            return first;
        var retry = new Dictionary<string, string>(fields) { ["reply_markup"] = plain };
        var second = await PostAsync(token, method, retry, ct);
        if (second.Ok)
        {
            _premiumRefusedAt = DateTime.UtcNow;
            _logger.LogWarning("Telegram refused the bot's premium emoji ({Body}); sending plain emoji for a while", first.Body);
        }
        return second;
    }

    private async Task<(bool Ok, int Status, string Body)> PostAsync(string token, string method, Dictionary<string, string> fields, CancellationToken ct)
    {
        try
        {
            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(20);
            using var form = new FormUrlEncodedContent(fields);
            using var resp = await http.PostAsync($"https://api.telegram.org/bot{token}/{method}", form, ct);
            if (resp.IsSuccessStatusCode) return (true, 200, "");
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!body.Contains("not modified", StringComparison.OrdinalIgnoreCase))
                _logger.LogInformation("Customer bot {Method} refused: {Status}", method, (int)resp.StatusCode);
            return (false, (int)resp.StatusCode, body);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Customer bot {Method} call failed", method);
            return (false, 0, "");
        }
    }

    // ── Text ────────────────────────────────────────────────────────────────────────────────────────────────

    // What Telegram's HTML needs escaped, and nothing else: Persian text passes through as it is.
    private static string H(string? text) => (text ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string Fa(long n) => JalaliDate.ToPersianDigits(n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)).Replace(",", "٬");

    private static string Toman(long n) => $"{Fa(n)} تومان";

    // A button shows one line, and a long name pushes the rest off its edge.
    private static string Short(string? text, int max = 28)
    {
        var t = (text ?? "").Trim();
        return t.Length <= max ? t : t[..(max - 1)].TrimEnd() + "…";
    }

    private static string PlainOf(string html) =>
        System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", "").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&");

    private const string Rule = "━━━━━━━━━━━━━━";

    // ── The main menu ───────────────────────────────────────────────────────────────────────────────────────

    private string HomeText(long chatId, string? firstName)
    {
        var user = _store.FindUserByTelegramChat(chatId);
        var name = user is not null ? DisplayName(user) : (firstName ?? "").Trim();
        var products = _store.GetProducts().Where(p => p.IsActive).ToList();
        var features = new List<string> { "🛍 اکانت‌ها و سرویس‌ها با تحویل خودکار" };
        if (products.Any(p => p.IsPanelProvisioned)) features.Add("🚀 کانفیگ پرسرعت با چند لوکیشن");
        if (SalesOn && products.Any(GuestSellable)) features.Add("🔓 خرید محصولات 🔓 بدون ثبت‌نام");
        features.Add("📦 پیگیری سفارش و تحویل در همین چت");
        features.Add("💬 پشتیبانی مستقیم از داخل ربات");
        return $"<b>✨ فونیکس وریفای</b>\n{Rule}\n"
               + $"سلام{(name.Length > 0 ? " " + H(name) : "")} عزیز، خوش آمدید 🌹\n\n"
               + $"<blockquote>{string.Join("\n", features)}</blockquote>\n\n"
               + (user is not null
                   ? $"✅ حساب «{H(DisplayName(user))}» به این تلگرام وصل است."
                   : "🔗 حساب سایت وصل نیست؛ از «👤 حساب من» وصلش کنید.")
               + "\n\n👇 یکی از گزینه‌ها را انتخاب کنید";
    }

    private string MainMenu(long chatId)
    {
        var user = _store.FindUserByTelegramChat(chatId);
        var configs = _store.GetProducts().Any(p => p.IsActive && p.IsPanelProvisioned);
        var rows = new List<object[]>
        {
            configs
                ? new[] { Btn("🛍 خرید محصول", "shop:cats", Green), Btn("🚀 خرید کانفیگ", "cfg", Green) }
                : new[] { Btn("🛍 خرید محصول", "shop:cats", Green) },
            MyServices(chatId).Count > 0
                ? new[] { Btn("📦 سفارش‌های من", "ord:l:1", Blue), Btn("🔐 سرویس‌های من", "svc:l:1", Blue) }
                : new[] { Btn("📦 سفارش‌های من", "ord:l:1", Blue) },
        };
        if (ShopOn) rows.Add(new[] { new B("🛒 فروشگاه کامل سایت", WebApp: ShopUrl, Style: Blue) });
        rows.Add(user is not null && _store.GetSettings().ReferralCommissionPercent > 0
            ? new[] { Btn("👤 حساب من", "acc"), Btn("🎁 دعوت دوستان", "acc:ref") }
            : new[] { Btn("👤 حساب من", "acc") });
        rows.Add(new[] { Btn("📚 آموزش‌ها", "tut"), Btn("💬 پشتیبانی", "sup") });
        if (StaffFor(chatId) is not null) rows.Add(new[] { Btn("⚙️ مدیریت فروشگاه", "adm", Red) });
        return Inline(rows);
    }

    private Task ShowHomeAsync(string token, Screen at, string? firstName, CancellationToken ct)
    {
        Sessions.TryRemove(at.ChatId, out _);
        return ShowAsync(token, at, HomeText(at.ChatId, firstName), MainMenu(at.ChatId), ct, html: true);
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
        if (sent && at.MessageId is int old)
            await PostAsync(token, "deleteMessage", new() { ["chat_id"] = at.ChatId.ToString(), ["message_id"] = old.ToString() }, ct);
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
        var (ok, _, body) = await PostWithKeyboardAsync(token, "editMessageText", fields, ct);
        // Pressing the button of the screen already showing is not a failure.
        return ok || body.Contains("not modified", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> EditPhotoAsync(string token, long chatId, int messageId, string photo, string caption, string markup, bool html, CancellationToken ct)
    {
        var media = JsonSerializer.Serialize(html
            ? new { type = "photo", media = photo, caption, parse_mode = "HTML" }
            : (object)new { type = "photo", media = photo, caption });
        var (ok, _, body) = await PostWithKeyboardAsync(token, "editMessageMedia", new()
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
        return (await PostWithKeyboardAsync(token, "sendPhoto", fields, ct)).Ok;
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

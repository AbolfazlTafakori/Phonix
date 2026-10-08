using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Phonix.Api.Data;
using Phonix.Api.Models;

namespace Phonix.Api.Services;

// The catalogue inside the customer bot. Anyone who starts the bot can browse every product by category, with
// its plans and prices — looking needs no account, exactly as on the site. Buying follows the site's rules, with
// one exception staff choose per product (Product.TelegramGuestSale): those can be bought right here by anyone,
// card-to-card with a receipt photo, no site account and no identity steps. For everything else the payment step
// asks for a site account: someone without one is told to register, verify and link; a linked customer whose
// level is too low is told which step is missing; a linked customer who qualifies continues in the site's own
// checkout, opened inside Telegram already signed in.
//
// Buying is three numbered steps — category, product, plan — each with the same way back, then the payment.
//
// A purchase here is an ordinary order: the same PlaceOrder, the same receipt review in the receipt bot, the
// same automatic delivery — the bought account arrives in this chat, as everything for this account does.
// Someone without a linked site account buys with a Telegram-only account (AppUser.TelegramGuestChatId).
//
// The guards that make an open door safe: one unreviewed purchase at a time and a few a day per person, the
// price held for 30 minutes so what was paid is what is charged, and only plans that ask the buyer for nothing.
public sealed partial class TelegramCustomerBot
{
    // The buttons of the keyboard earlier versions left under people's message box. They keep working; the first
    // press swaps that keyboard for the current one.
    private const string LegacyBuy = "🛍 محصولات";
    private const string LegacyOrders = "📦 سفارش‌های من";
    private const string LegacySupport = "💬 پشتیبانی";
    private const string LegacyAccount = "🔗 حساب سایت";

    public const string PlacedViaBot = "telegram-bot";
    private const int MaxBotOrdersPerDay = 5;
    private const long MaxReceiptBytes = 6 * 1024 * 1024;
    private const int MaxSupportMessage = 2000;
    private static readonly TimeSpan PriceHold = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan TypingWait = TimeSpan.FromMinutes(10);

    // What a chat is in the middle of: paying for a chosen plan (the price is held), or typing something the bot
    // asked for — a message for support, an order code, and for staff a search or a broadcast.
    // A renewal also carries the service it extends and the account it belongs to: the renewal is that account's.
    private sealed record Session(string Kind, int ProductId, int? PlanId, long UnitPrice, long Total, int MethodId, DateTime Until,
        string? Text = null, string? RenewToken = null, int? BuyerId = null)
    {
        public static Session Typing(string kind, string? text = null) => new(kind, 0, null, 0, 0, 0, DateTime.UtcNow + TypingWait, text);
    }

    private static readonly ConcurrentDictionary<long, Session> Sessions = new();

    private bool SalesOn => _store.GetTelegramSettings().CustomerBotSales;

    private T? Resolve<T>() where T : class => _services?.GetService(typeof(T)) as T;

    // ── What can be bought here ─────────────────────────────────────────────────────────────────────────────

    // The plans a buyer can take here: active, and asking them for nothing — there is no form in a chat.
    public static IReadOnlyList<ProductPlan> GuestPlans(Product p) =>
        p.Plans.Where(x => x.IsActive && !x.CollectsInfo && !x.CollectSeatInfo).ToList();

    // Opened for the bot by staff, needs no identity documents, in stock, and — when it is sold by plan — has a
    // plan that asks for nothing.
    public static bool GuestSellable(Product p) =>
        p.IsActive && p.TelegramGuestSale && p.RequiredLevel <= 1
        && (p.IsPanelProvisioned || p.Stock > 0)
        && (!p.Plans.Any(x => x.IsActive) || GuestPlans(p).Count > 0);

    private PaymentMethod? PaymentCard() =>
        _store.GetPaymentMethods().Where(m => m.IsActive && m.Type == PaymentType.Card && !string.IsNullOrWhiteSpace(m.Value))
            .OrderBy(m => m.SortOrder).FirstOrDefault();

    // VAT and the card's fee exactly as PlaceOrder adds them, so the amount asked for is the amount the order takes.
    private (long Vat, long Fee, long Total) Quote(long price, PaymentMethod method)
    {
        var settings = _store.GetSettings();
        var vat = settings.VatPercent > 0 ? (long)Math.Round(price * (double)settings.VatPercent / 100.0, MidpointRounding.AwayFromZero) : 0;
        var feePercent = method.FeePercent > 0 ? method.FeePercent : settings.GatewayFeePercent;
        var fee = feePercent > 0 ? (long)Math.Round((price + vat) * (double)feePercent / 100.0, MidpointRounding.AwayFromZero) : 0;
        return (vat, fee, price + vat + fee);
    }

    // Everyone this chat buys as: its linked site account, and the Telegram-only account it may have bought with
    // before it was linked.
    private List<AppUser> Buyers(long chatId) =>
        new[] { _store.FindUserByTelegramChat(chatId), _store.FindTelegramGuest(chatId) }.OfType<AppUser>().ToList();

    // A linked customer buys on their site account; anyone else on their Telegram-only one, made on first use.
    private AppUser BuyerFor(long chatId, JsonElement from) =>
        _store.FindUserByTelegramChat(chatId) ?? _store.EnsureTelegramGuest(chatId, TelegramFullName(from));

    private static string TelegramFullName(JsonElement from)
    {
        if (from.ValueKind != JsonValueKind.Object) return "";
        var first = from.TryGetProperty("first_name", out var f) ? f.GetString() ?? "" : "";
        var last = from.TryGetProperty("last_name", out var l) ? l.GetString() ?? "" : "";
        return $"{first} {last}".Trim();
    }

    private static string? FirstName(JsonElement from) =>
        from.ValueKind == JsonValueKind.Object && from.TryGetProperty("first_name", out var f) ? f.GetString() : null;

    // How the payer shows on the receipt for staff: there is no registered card behind a bot purchase.
    private static string PayerName(JsonElement from)
    {
        var name = TelegramFullName(from);
        var handle = DecisionVia.TelegramName(from);
        var label = handle is not null && handle.StartsWith('@') ? $"{name} ({handle})".Trim() : name;
        return $"تلگرام: {(label.Length > 0 ? label : "بدون نام")}";
    }

    // Why this chat can't start another purchase now, or null.
    private string? PurchaseBlocked(IReadOnlyList<AppUser> buyers)
    {
        if (buyers.Any(b => b.Blocked)) return "امکان خرید برای حساب شما وجود ندارد. با پشتیبانی در تماس باشید.";
        var placed = buyers.SelectMany(b => _store.GetUserOrders(b.Id)).Where(o => o.PlacedVia == PlacedViaBot).ToList();
        if (placed.Any(o => o.Status == OrderStatus.PendingApproval))
            return "سفارش قبلی شما هنوز در انتظار بررسی رسید است. پس از بررسی آن می‌توانید دوباره خرید کنید.";
        if (placed.Count(o => o.PlacedAtUtc > DateTime.UtcNow.AddDays(-1)) >= MaxBotOrdersPerDay)
            return "به سقف خرید روزانه در ربات رسیده‌اید. فردا دوباره امتحان کنید، یا از سایت خرید کنید.";
        return null;
    }

    private string RegisterKeyboard(string? back) => Inline(new[]
    {
        new[] { Link("📝 ثبت‌نام در سایت", $"{Site}/signup", Green) },
        new[] { Link("🔗 اتصال حساب به تلگرام", $"{Site}/account/telegram", Blue) },
        NavRow(back),
    });

    // ── Messages ────────────────────────────────────────────────────────────────────────────────────────────

    // The menu buttons, a receipt photo for a purchase in progress, something the bot asked to be typed. False:
    // not ours.
    private async Task<bool> TryHandleShopMessageAsync(string token, long chatId, JsonElement msg, string text, JsonElement from, CancellationToken ct)
    {
        switch (text)
        {
            case MenuHome:
                await ShowHomeAsync(token, new Screen(chatId), FirstName(from), ct);
                return true;
            case LegacyBuy or LegacyOrders or LegacySupport or LegacyAccount:
                Sessions.TryRemove(chatId, out _);
                await ReplyAsync(token, chatId, $"✨ منوی ربات تازه شد؛ از این پس همه‌چیز از «{MenuHome}» در دسترس است.", ct, HomeKeyboard());
                var at = new Screen(chatId);
                await (text switch
                {
                    LegacyBuy => ShowCatalogAsync(token, at, ct),
                    LegacyOrders => ShowOrdersAsync(token, at, 1, ct),
                    LegacySupport => StartSupportAsync(token, at, ct),
                    _ => ShowAccountAsync(token, at, ct),
                });
                return true;
        }

        if (!Sessions.TryGetValue(chatId, out var session)) return false;
        if (session.Until < DateTime.UtcNow)
        {
            Sessions.TryRemove(chatId, out _);
            if (session.Kind == "receipt" && ImageOf(msg) is not null)
            {
                await ReplyAsync(token, chatId, "زمان این خرید تمام شده است. از «🛍 خرید محصول» دوباره انتخاب کنید تا قیمت به‌روز نشان داده شود.",
                    ct, HomeOnly());
                return true;
            }
            return false;
        }

        var typed = text.Length > 0 && !text.StartsWith('/');
        switch (session.Kind)
        {
            case "receipt":
                if (ImageOf(msg) is { } image)
                {
                    await ReceiveReceiptAsync(token, chatId, msg, image, from, session, ct);
                    return true;
                }
                if (!typed) return false;
                await ReplyAsync(token, chatId, "لطفاً عکس رسید پرداخت را بفرستید، یا «انصراف» را بزنید.", ct,
                    Inline(new[] { new[] { Btn("❌ انصراف", "shop:x", Red) } }));
                return true;
            case "support" when typed:
                Sessions.TryRemove(chatId, out _);
                await SendToSupportAsync(token, chatId, text, from, ct);
                return true;
            case "search" when typed:
                Sessions.TryRemove(chatId, out _);
                await FindMyOrderAsync(token, chatId, text, ct);
                return true;
            case "a-emoji":
                Sessions.TryRemove(chatId, out _);
                await HandleStaffEmojiAsync(token, chatId, session, msg, ct);
                return true;
            case "a-order" or "a-user" or "a-cast" when typed:
                Sessions.TryRemove(chatId, out _);
                await HandleStaffTextAsync(token, chatId, session.Kind, text, ct);
                return true;
        }
        return false;
    }

    // ── Buttons ─────────────────────────────────────────────────────────────────────────────────────────────

    private async Task HandleShopCallbackAsync(string token, JsonElement cq, CancellationToken ct)
    {
        var callbackId = cq.TryGetProperty("id", out var cid) ? cid.GetString() ?? "" : "";
        var data = cq.TryGetProperty("data", out var d) ? d.GetString() ?? "" : "";
        // Private chats only, like everything else this bot does.
        if (!cq.TryGetProperty("message", out var message) || !message.TryGetProperty("chat", out var chat)
            || !chat.TryGetProperty("id", out var chatEl) || !chatEl.TryGetInt64(out var chatId)
            || !chat.TryGetProperty("type", out var type) || type.GetString() != "private")
        {
            await AnswerAsync(token, callbackId, "", ct);
            return;
        }
        await AnswerAsync(token, callbackId, "", ct);
        cq.TryGetProperty("from", out var from);

        var at = new Screen(chatId,
            message.TryGetProperty("message_id", out var mid) && mid.TryGetInt32(out var messageId) ? messageId : null,
            message.TryGetProperty("photo", out _));
        var parts = data.Split(':');
        // A notice's buttons open their screen as a new message: the notice itself stays in the chat as it was.
        if (parts.Length > 1 && parts[^1] == "n")
        {
            at = at.Fresh;
            parts = parts[..^1];
        }
        if (parts[0] == "adm")
        {
            await HandleStaffCallbackAsync(token, at, parts, ct);
            return;
        }

        switch (parts)
        {
            case ["noop"]:
                break;
            case ["home"]:
                await ShowHomeAsync(token, at, FirstName(from), ct);
                break;
            case ["shop", "x"]:
                Sessions.TryRemove(chatId, out _);
                await ShowHomeAsync(token, at, FirstName(from), ct);
                break;
            case ["shop", "cats"]:
                await ShowCatalogAsync(token, at, ct);
                break;
            case ["shop", "c", var c] when int.TryParse(c, out var categoryId):
                await ShowCategoryAsync(token, at, categoryId, ct);
                break;
            case ["shop", "p", var p] when int.TryParse(p, out var productId):
                await ShowProductAsync(token, at, productId, quick: false, ct);
                break;
            case ["shop" or "cfg", "t", var p, var i] when int.TryParse(p, out var productId) && int.TryParse(i, out var kind):
                await ShowPlanKindAsync(token, at, productId, kind, quick: parts[0] == "cfg", ct);
                break;
            case ["cfg"]:
                await ShowConfigShopAsync(token, at, ct);
                break;
            case ["cfg", "p", var p] when int.TryParse(p, out var productId):
                await ShowProductAsync(token, at, productId, quick: true, ct);
                break;
            case ["svc", "l", var pg] when int.TryParse(pg, out var page):
                await ShowServicesAsync(token, at, page, ct);
                break;
            case ["svc", "v", var o, var u] when int.TryParse(o, out var orderId) && int.TryParse(u, out var unitId):
                await ShowServiceAsync(token, at, orderId, unitId, ct);
                break;
            case ["svc", "s", var o, var u] when int.TryParse(o, out var orderId) && int.TryParse(u, out var unitId):
                await SendSubscriptionLinkAsync(token, chatId, orderId, unitId, ct);
                break;
            case ["svc", "r", var o, var u] when int.TryParse(o, out var orderId) && int.TryParse(u, out var unitId):
                await ShowRenewalsAsync(token, at, orderId, unitId, ct);
                break;
            case ["svc", "rp", var o, var u, var pl] when int.TryParse(o, out var orderId) && int.TryParse(u, out var unitId) && int.TryParse(pl, out var planId):
                await StartRenewalAsync(token, at, orderId, unitId, planId, ct);
                break;
            case ["shop", "b", var p, var pl] when int.TryParse(p, out var productId) && int.TryParse(pl, out var planId):
                await StartPurchaseAsync(token, at, productId, planId == 0 ? null : planId, ct);
                break;
            case ["ord", "l", var pg] when int.TryParse(pg, out var page):
                await ShowOrdersAsync(token, at, page, ct);
                break;
            case ["ord", "v", var o] when int.TryParse(o, out var orderId):
                await ShowOrderAsync(token, at, orderId, ct);
                break;
            case ["ord", "s"]:
                Sessions[chatId] = Session.Typing("search");
                await ShowAsync(token, at, "🔎 جستجوی سفارش\n\nکد سفارش را بفرستید؛ مثلاً PX-100150 یا فقط 100150.",
                    Inline(new[] { NavRow("ord:l:1", "⬅️ سفارش‌ها") }), ct);
                break;
            case ["acc"]:
                await ShowAccountAsync(token, at, ct);
                break;
            case ["acc", "ref"]:
                await ShowReferralAsync(token, at, ct);
                break;
            case ["acc", "notify"]:
                await ToggleNotifyAsync(token, at, ct);
                break;
            case ["acc", "unlink"]:
                await ShowAsync(token, at,
                    "🔌 قطع اتصال حساب\n\nبا قطع اتصال، پیام‌های حساب سایت دیگر اینجا ارسال نمی‌شود. هر زمان بخواهید می‌توانید دوباره وصل کنید.\n\nمطمئنید؟",
                    Inline(new[] { new[] { Btn("✅ بله، قطع شود", "acc:unlink:y", Red), Btn("⬅️ نه، برگرد", "acc") } }), ct);
                break;
            case ["acc", "unlink", "y"]:
                _store.UnlinkTelegramChat(chatId);
                await ShowHomeAsync(token, at, FirstName(from), ct);
                break;
            case ["tut"]:
                await ShowTutorialsAsync(token, at, ct);
                break;
            case ["tut", var t] when int.TryParse(t, out var tutorialId):
                await ShowTutorialAsync(token, at, tutorialId, ct);
                break;
            case ["sup"]:
                await StartSupportAsync(token, at, ct);
                break;
        }
    }

    // ── Step 1: the category ────────────────────────────────────────────────────────────────────────────────

    private static string Step(int n) => n switch { 1 => "۱️⃣ مرحله اول", 2 => "۲️⃣ مرحله دوم", 3 => "۳️⃣ مرحله سوم", _ => "۴️⃣ مرحله چهارم" };

    private async Task ShowCatalogAsync(string token, Screen at, CancellationToken ct)
    {
        var products = _store.GetProducts().Where(p => p.IsActive).ToList();
        var used = products.Select(p => p.CategoryId).ToHashSet();
        var categories = _store.GetCategories().Where(c => c.IsActive && used.Contains(c.Id)).OrderBy(c => c.SortOrder).ToList();
        if (categories.Count == 0)
        {
            await ShowAsync(token, at, "فعلاً محصولی برای نمایش وجود ندارد.", HomeOnly(), ct);
            return;
        }
        var rows = categories.Select(c => new[] { Btn($"🗂 {Short(c.Name, 34)}", $"shop:c:{c.Id}", Blue) }).Append(HomeRow());
        var guest = SalesOn && products.Any(GuestSellable);
        await ShowAsync(token, at,
            $"<b>🛍 خرید محصول</b>\n{Rule}\n{Step(1)}: دسته‌بندی را انتخاب کنید 👇"
            + (guest ? "\n\n<i>🔓 یعنی همین‌جا و بدون ثبت‌نام خریده می‌شود.</i>" : ""),
            Inline(rows), ct, html: true);
    }

    // ── Step 2: the product ─────────────────────────────────────────────────────────────────────────────────

    private async Task ShowCategoryAsync(string token, Screen at, int categoryId, CancellationToken ct)
    {
        var category = _store.GetCategory(categoryId);
        var products = _store.GetProducts(categoryId).Where(p => p.IsActive).ToList();
        if (category is null || !category.IsActive || products.Count == 0)
        {
            await ShowCatalogAsync(token, at, ct);
            return;
        }
        var rows = products
            .Select(p => new[] { Btn(ProductButton(p), $"shop:p:{p.Id}", InStock(p) ? Blue : null) })
            .Append(NavRow("shop:cats", "⬅️ مرحله‌ی قبل"));
        await ShowAsync(token, at, $"<b>🗂 {H(category.Name)}</b>\n{Rule}\n{Step(2)}: محصول را انتخاب کنید 👇", Inline(rows), ct, html: true);
    }

    // Just the product: its price, plans and what it takes to buy it are on its own page.
    private string ProductButton(Product p) =>
        $"{(SalesOn && GuestSellable(p) ? "🔓" : "🔹")} {Short(p.Name, 30)}{(InStock(p) ? "" : " · ناموجود")}";

    // ── Step 3: the plan ────────────────────────────────────────────────────────────────────────────────────

    // The plan's own name, without the kind it was picked under: «۳ ماهه · ۲ کاربر», «۵ گیگ یک کاربر».
    private static string PlanName(ProductPlan plan)
    {
        var what = string.IsNullOrWhiteSpace(plan.Label) ? $"{JalaliDate.ToPersianDigits(plan.Months.ToString())} ماهه" : plan.Label.Trim();
        return plan.UserCount > 0 && string.IsNullOrWhiteSpace(plan.Label)
            ? $"{what} · {JalaliDate.ToPersianDigits(plan.UserCount.ToString())} کاربر"
            : what;
    }

    private static string PlanLabel(ProductPlan plan) =>
        string.IsNullOrWhiteSpace(plan.Type) ? PlanName(plan) : $"{plan.Type.Trim()} · {PlanName(plan)}";

    // The kinds a product's plans come in — for a V2Ray or WireGuard product each is a location — in catalogue order.
    private static List<string> PlanKinds(IEnumerable<ProductPlan> plans) =>
        plans.Select(p => (p.Type ?? "").Trim()).Where(t => t.Length > 0).Distinct().ToList();

    private static bool InStock(Product p) => p.IsPanelProvisioned || p.Stock > 0;

    // «🚀 خرید کانفیگ»: straight to the locations when the shop sells one kind of config, else which kind first.
    private async Task ShowConfigShopAsync(string token, Screen at, CancellationToken ct)
    {
        var configs = _store.GetProducts().Where(p => p.IsActive && p.IsPanelProvisioned).ToList();
        if (configs.Count == 1)
        {
            await ShowProductAsync(token, at, configs[0].Id, quick: true, ct);
            return;
        }
        if (configs.Count == 0)
        {
            await ShowCatalogAsync(token, at, ct);
            return;
        }
        var rows = configs.Select(p => new[] { Btn(ProductButton(p), $"cfg:p:{p.Id}", Blue) }).Append(HomeRow());
        await ShowAsync(token, at, $"<b>🚀 خرید کانفیگ</b>\n{Rule}\n{Step(1)}: نوع سرویس را انتخاب کنید 👇", Inline(rows), ct, html: true);
    }

    // Everything about a product is shown to anyone; what it takes to buy it is said up front. A product whose
    // plans come in kinds — the locations of a V2Ray product — asks for the kind first, then the plan.
    private async Task ShowProductAsync(string token, Screen at, int productId, bool quick, CancellationToken ct)
    {
        if (_store.GetProduct(productId) is not { IsActive: true } product)
        {
            await ShowAsync(token, at, "این محصول در دسترس نیست.", Inline(new[] { NavRow("shop:cats") }), ct);
            return;
        }
        var plans = product.Plans.Where(x => x.IsActive).ToList();
        var kinds = PlanKinds(plans);
        var byKind = kinds.Count > 1;
        var guest = SalesOn && GuestSellable(product);
        var guestPlans = GuestPlans(product).Select(p => p.Id).ToHashSet();
        var prefix = quick ? "cfg" : "shop";
        var step = quick ? (_store.GetProducts().Count(p => p.IsActive && p.IsPanelProvisioned) > 1 ? 2 : 1) : 3;

        var lines = new List<string> { $"<b>{(product.IsPanelProvisioned ? "🚀" : "🛍")} {H(product.Name)}</b>", Rule };
        if (Summary(product.Description) is { Length: > 0 } about) lines.Add(H(about));
        if (!string.IsNullOrWhiteSpace(product.Warning)) lines.Add($"\n⚠️ {H(product.Warning.Trim())}");
        lines.Add("");
        if (!InStock(product)) lines.Add("❌ ناموجود");
        else if (guest)
            lines.Add(GuestPlans(product).Count < plans.Count
                ? "🔓 گزینه‌هایی که با 🔓 مشخص شده‌اند، همین‌جا و بدون ثبت‌نام خریده می‌شوند؛ بقیه با حساب سایت."
                : "🔓 همین‌جا و بدون ثبت‌نام قابل خرید است.");
        else
            lines.Add(product.RequiredLevel > 1 ? "🔐 خرید با حساب سایت و احراز هویت سطح ۲" : "🔐 خرید با حساب سایت");
        if (InStock(product))
            lines.Add(byKind
                ? $"\n{Step(step)}: {(product.IsPanelProvisioned ? "لوکیشن" : "نوع سرویس")} را انتخاب کنید 👇"
                : $"\n{Step(step)}: {(plans.Count > 0 ? "پلن" : "خرید")} 👇");

        var rows = new List<object[]>();
        if (InStock(product))
        {
            if (byKind)
                rows.AddRange(Pairs(kinds.Select((k, i) => (object)Btn(
                    $"{(product.IsPanelProvisioned ? "🌍" : "🗂")} {Short(k, 26)}{(guest && plans.Any(p => (p.Type ?? "").Trim() == k && guestPlans.Contains(p.Id)) ? " 🔓" : "")}",
                    $"{prefix}:t:{product.Id}:{i}", Blue))));
            else if (plans.Count == 0)
                rows.Add(new[] { Btn($"{(guest ? "🔓" : "💎")} خرید — {Toman(product.FinalPrice)}", $"shop:b:{product.Id}:0", Green) });
            else
                rows.AddRange(plans.Select(pl => new[]
                {
                    Btn($"{(guest && guestPlans.Contains(pl.Id) ? "🔓" : "💎")} {PlanName(pl)} — {Toman(pl.FinalPrice)}", $"shop:b:{product.Id}:{pl.Id}", Green),
                }));
        }
        if (StaffFor(at.ChatId) is { } staff && Can(staff, "products"))
            rows.Add(new[] { Btn("⚙️ مدیریت این محصول", $"adm:pr:{product.Id}") });
        var back = quick ? (step == 2 ? "cfg" : null) : $"shop:c:{product.CategoryId}";
        rows.Add(NavRow(back, "⬅️ مرحله‌ی قبل"));
        await ShowAsync(token, at, string.Join("\n", lines), Inline(rows), ct, html: true, photo: PictureOf(product));
    }

    // The plans of one kind — one location of a V2Ray product — with their prices.
    private async Task ShowPlanKindAsync(string token, Screen at, int productId, int kindIndex, bool quick, CancellationToken ct)
    {
        var product = _store.GetProduct(productId);
        var plans = product?.Plans.Where(x => x.IsActive).ToList() ?? new List<ProductPlan>();
        var kinds = PlanKinds(plans);
        if (product is not { IsActive: true } || kindIndex < 0 || kindIndex >= kinds.Count)
        {
            await ShowCatalogAsync(token, at, ct);
            return;
        }
        var kind = kinds[kindIndex];
        var guest = SalesOn && GuestSellable(product);
        var guestPlans = GuestPlans(product).Select(p => p.Id).ToHashSet();
        var step = quick ? (_store.GetProducts().Count(p => p.IsActive && p.IsPanelProvisioned) > 1 ? 3 : 2) : 4;
        var rows = plans.Where(p => (p.Type ?? "").Trim() == kind)
            .Select(pl => new[]
            {
                Btn($"{(guest && guestPlans.Contains(pl.Id) ? "🔓" : "💎")} {PlanName(pl)} — {Toman(pl.FinalPrice)}", $"shop:b:{product.Id}:{pl.Id}", Green),
            })
            .Cast<object[]>().ToList();
        rows.Add(NavRow($"{(quick ? "cfg" : "shop")}:p:{product.Id}", "⬅️ مرحله‌ی قبل"));
        await ShowAsync(token, at,
            $"<b>{(product.IsPanelProvisioned ? "🌍" : "🗂")} {H(kind)}</b>\n{Rule}\n{H(product.Name)}\n\n{Step(step)}: پلن را انتخاب کنید 👇",
            Inline(rows), ct, html: true);
    }

    // A short plain-text look at a product description written in the site's Markdown.
    private static string Summary(string? markdown, int max = 450)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "";
        var text = System.Text.RegularExpressions.Regex.Replace(markdown, @"!\[[^\]]*\]\([^)]*\)", "");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\[([^\]]+)\]\([^)]*\)", "$1");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\{#[a-z0-9-]+\}", "");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"^\s*(#{1,6}|\|.*\||-{3,})\s*", "", System.Text.RegularExpressions.RegexOptions.Multiline);
        text = text.Replace("**", "").Replace("*", "");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n").Trim();
        return text.Length > max ? text[..max].TrimEnd() + "…" : text;
    }

    // ── Paying ──────────────────────────────────────────────────────────────────────────────────────────────

    // A product only a site account can buy: say exactly what is missing — or, when nothing is, continue in the
    // site's checkout inside Telegram, where every rule of the site applies as it is.
    private async Task AccountRequiredAsync(string token, Screen at, Product product, CancellationToken ct)
    {
        var back = $"shop:p:{product.Id}";
        if (_store.FindUserByTelegramChat(at.ChatId) is not { } user)
        {
            await ShowAsync(token, at,
                $"🔐 خرید «{product.Name}» فقط با حساب سایت ممکن است.\n\n"
                + "۱. در سایت ثبت‌نام کنید.\n"
                + (product.RequiredLevel > 1
                    ? "۲. در حساب کاربری، احراز هویت سطح ۲ (کارت ملی) را انجام دهید.\n"
                    : "۲. در حساب کاربری، کارت بانکی‌تان را ثبت کنید تا تأیید شود.\n")
                + $"۳. از منوی حساب کاربری «اتصال به تلگرام» را بزنید و کد {CodeLengthFa} رقمی‌ای را که به ایمیلتان می‌آید همین‌جا بفرستید.\n\n"
                + "بعد از اتصال، خرید را از همین ربات ادامه دهید.", RegisterKeyboard(back), ct);
            return;
        }
        if (user.VerificationLevel < product.RequiredLevel)
        {
            var (what, path) = product.RequiredLevel > 1 && user.VerificationLevel >= 1
                ? ("احراز هویت سطح ۲ (کارت ملی)", "/account/kyc")
                : ("ثبت و تأیید کارت بانکی (سطح ۱)", "/account/cards");
            await ShowAsync(token, at,
                $"سطح احراز هویت حساب شما برای «{product.Name}» کافی نیست. ابتدا در سایت {what} را انجام دهید.",
                Inline(new[] { new[] { SiteButton("🪪 احراز هویت", path, Blue) }, NavRow(back) }), ct);
            return;
        }
        await ShowAsync(token, at,
            $"پرداخت «{product.Name}» در فروشگاه سایت انجام می‌شود" + (ShopOn ? "؛ همین‌جا داخل تلگرام باز می‌شود و حسابتان وارد است." : "."),
            Inline(new[] { new[] { SiteButton("💳 ادامه‌ی خرید", $"/products/{product.Id}", Green) }, NavRow(back) }), ct);
    }

    private async Task StartPurchaseAsync(string token, Screen at, int productId, int? planId, CancellationToken ct,
        string? renewToken = null, int? buyerId = null, string? back = null)
    {
        var chatId = at.ChatId;
        if (_store.GetProduct(productId) is not { IsActive: true } product)
        {
            await ShowAsync(token, at, "این محصول در دسترس نیست.", Inline(new[] { NavRow("shop:cats") }), ct);
            return;
        }
        back ??= $"shop:p:{product.Id}";
        var plan = planId is int id ? product.Plans.FirstOrDefault(p => p.Id == id && p.IsActive) : null;
        if ((planId is not null && plan is null) || (planId is null && product.Plans.Any(p => p.IsActive)))
        {
            await ShowAsync(token, at, "این گزینه دیگر در دسترس نیست. دوباره از فهرست انتخاب کنید.", Inline(new[] { NavRow(back) }), ct);
            return;
        }
        if (!InStock(product))
        {
            await ShowAsync(token, at, "این محصول فعلاً ناموجود است.", Inline(new[] { NavRow(back) }), ct);
            return;
        }
        // Bought here without an account only when staff opened it and this option asks the buyer for nothing.
        var guest = SalesOn && GuestSellable(product) && (plan is null || GuestPlans(product).Any(p => p.Id == plan.Id));
        if (!guest)
        {
            await AccountRequiredAsync(token, at, product, ct);
            return;
        }
        if (PurchaseBlocked(Buyers(chatId)) is { } blocked)
        {
            await ShowAsync(token, at, blocked, Inline(new[] { new[] { Btn("📦 سفارش‌های من", "ord:l:1", Blue) }, HomeRow() }), ct);
            return;
        }
        if (PaymentCard() is not { } card)
        {
            await ShowAsync(token, at, "پرداخت در ربات فعلاً ممکن نیست. لطفاً بعداً امتحان کنید یا از سایت خرید کنید.", Inline(new[] { NavRow(back) }), ct);
            return;
        }

        var price = plan?.FinalPrice ?? product.FinalPrice;
        var (vat, fee, total) = Quote(price, card);
        Sessions[chatId] = new Session("receipt", product.Id, plan?.Id, price, total, card.Id, DateTime.UtcNow + PriceHold,
            RenewToken: renewToken, BuyerId: buyerId);

        // The card number in a code block: one tap copies it.
        var title = plan is null ? product.Name : $"{product.Name} — {PlanLabel(plan)}";
        var amounts = vat > 0 || fee > 0
            ? $"قیمت: {Toman(price)}" + (vat > 0 ? $"\nمالیات: {Toman(vat)}" : "") + (fee > 0 ? $"\nکارمزد: {Toman(fee)}" : "") + "\n"
            : "";
        var instructions = string.IsNullOrWhiteSpace(card.Instructions) ? "" : $"\n{H(card.Instructions.Trim())}\n";
        await ShowAsync(token, at,
            $"<b>🧾 {(renewToken is null ? "فاکتور خرید" : "فاکتور تمدید")}</b>\n{Rule}\n"
            + $"<b>{H(title)}</b>\n\n{H(amounts)}💰 مبلغ قابل پرداخت: <b>{Toman(total)}</b>\n\n"
            + $"💳 شماره کارت (برای کپی لمس کنید):\n<code>{H(card.Value.Trim())}</code>\n👤 به نام: {H(card.Holder)}\n{instructions}\n"
            + "<blockquote>📸 پس از واریز، عکس رسید را همین‌جا بفرستید. اگر شماره پیگیری دارید، آن را در توضیح عکس بنویسید.</blockquote>\n"
            + $"⏳ این قیمت تا {JalaliDate.ToPersianDigits(((int)PriceHold.TotalMinutes).ToString())} دقیقه برایتان ثابت است.",
            Inline(new[] { new[] { Btn("❌ انصراف", "shop:x", Red) } }), ct, html: true);
    }

    private async Task ReceiveReceiptAsync(string token, long chatId, JsonElement msg, (string FileId, string Ext) image,
        JsonElement from, Session session, CancellationToken ct)
    {
        if (_files is null) return;
        var product = _store.GetProduct(session.ProductId);
        var support = Inline(new[] { new[] { Btn("💬 پشتیبانی", "sup", Blue) }, HomeRow() });
        if (product is null || !GuestSellable(product) || !SalesOn)
        {
            Sessions.TryRemove(chatId, out _);
            await ReplyAsync(token, chatId, "این محصول دیگر در ربات قابل خرید نیست. اگر مبلغ را واریز کرده‌اید، از «💬 پشتیبانی» پیام دهید.", ct, support);
            return;
        }
        // A renewal is placed on the account the service belongs to, and only while it still can be renewed.
        var buyer = session.BuyerId is int ownerId ? Buyers(chatId).FirstOrDefault(b => b.Id == ownerId) : BuyerFor(chatId, from);
        if (buyer is null || (session.RenewToken is { } renewing && RenewalBlocked(chatId, renewing, session.ProductId, session.PlanId) is not null))
        {
            Sessions.TryRemove(chatId, out _);
            await ReplyAsync(token, chatId, "این سرویس دیگر قابل تمدید نیست. اگر مبلغ را واریز کرده‌اید، از «💬 پشتیبانی» پیام دهید.", ct, support);
            return;
        }
        if (PurchaseBlocked(Buyers(chatId)) is { } blocked)
        {
            Sessions.TryRemove(chatId, out _);
            await ReplyAsync(token, chatId, blocked, ct, HomeOnly());
            return;
        }

        var bytes = await DownloadAsync(token, image.FileId, ct);
        var cancel = Inline(new[] { new[] { Btn("❌ انصراف", "shop:x", Red) } });
        if (bytes is null)
        {
            await ReplyAsync(token, chatId, "دریافت عکس انجام نشد (حداکثر ۶ مگابایت). لطفاً دوباره بفرستید.", ct, cancel);
            return;
        }
        var upload = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "receipt" + image.Ext) { Headers = new HeaderDictionary() };
        var saved = await _files.SaveAsync(buyer.Id, "receipts", upload, ct);
        if (saved.Id is null)
        {
            await ReplyAsync(token, chatId, saved.Error ?? "این فایل تصویر معتبری نیست. عکس رسید را دوباره بفرستید.", ct, cancel);
            return;
        }

        var caption = msg.TryGetProperty("caption", out var c) ? c.GetString() : null;
        var result = _store.PlaceOrder(buyer, new[] { (session.ProductId, 1, session.PlanId) }, "کارت به کارت", fromWallet: false,
            paymentMethodId: session.MethodId,
            lineInfo: session.RenewToken is { } renew ? new[] { new OrderLineInfo(null, renew) } : null,
            lockedPrices: new Dictionary<(int productId, int? planId), long> { [(session.ProductId, session.PlanId)] = session.UnitPrice },
            bot: new BotCheckout(saved.Id, Digits(caption), PayerName(from)));
        Sessions.TryRemove(chatId, out _);
        if (result.Order is not { } order)
        {
            _logger.LogWarning("Customer bot purchase failed for chat {Chat}: {Error}", chatId, result.Error);
            await ReplyAsync(token, chatId,
                $"ثبت سفارش انجام نشد: {result.Error}\nاگر مبلغ را واریز کرده‌اید، از «💬 پشتیبانی» پیام دهید تا پیگیری شود.", ct, support);
            return;
        }

        // To staff, exactly like a receipt from the site's checkout.
        var payment = _store.GetUserTransactions(buyer.Id)
            .FirstOrDefault(t => t.OrderCode == order.Code && t.Type == TxTypes.OrderPayment && t.Status == TxStatus.Pending);
        if (payment is not null && Resolve<ITelegramReceiptService>() is { } receipts) _ = receipts.NotifyDepositAsync(payment);
        _logger.LogInformation("Customer bot: order {Code} placed by user {UserId}", order.Code, buyer.Id);

        var guest = buyer.TelegramGuestChatId is not null;
        await ReplyAsync(token, chatId,
            $"<b>✅ سفارش {H(order.Code)} ثبت شد</b>\n{Rule}\n"
            + (session.RenewToken is null
                ? "رسید شما بررسی می‌شود؛ پس از تأیید، اطلاعات اکانت همین‌جا برایتان ارسال می‌شود."
                : "رسید شما بررسی می‌شود؛ پس از تأیید، سرویس با همان لینک تمدید می‌شود.")
            + (guest ? "\n\n<i>برای نگه داشتن سوابق خرید در سایت، می‌توانید ثبت‌نام کنید و حسابتان را وصل کنید (👤 حساب من).</i>" : ""),
            ct, Inline(new[] { new[] { Btn("📄 مشاهده‌ی سفارش", $"ord:v:{order.Id}", Blue) }, HomeRow() }), html: true);
    }

    // ── Telegram helpers ────────────────────────────────────────────────────────────────────────────────────

    // A picture the customer sent: a photo (the largest size Telegram offers), or an image sent as a file.
    private static (string FileId, string Ext)? ImageOf(JsonElement msg)
    {
        if (msg.TryGetProperty("photo", out var photos) && photos.ValueKind == JsonValueKind.Array && photos.GetArrayLength() > 0
            && photos[photos.GetArrayLength() - 1].TryGetProperty("file_id", out var pid) && pid.GetString() is { Length: > 0 } photoId)
            return (photoId, ".jpg");
        if (msg.TryGetProperty("document", out var doc)
            && doc.TryGetProperty("mime_type", out var mime) && mime.GetString() is { } m && m.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            && doc.TryGetProperty("file_id", out var did) && did.GetString() is { Length: > 0 } docId)
            return (docId, m.Equals("image/png", StringComparison.OrdinalIgnoreCase) ? ".png" : m.Equals("image/webp", StringComparison.OrdinalIgnoreCase) ? ".webp" : ".jpg");
        return null;
    }

    private async Task<byte[]?> DownloadAsync(string token, string fileId, CancellationToken ct)
    {
        try
        {
            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(30);
            using var meta = await http.GetAsync($"https://api.telegram.org/bot{token}/getFile?file_id={Uri.EscapeDataString(fileId)}", ct);
            if (!meta.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await meta.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("result", out var r) || !r.TryGetProperty("file_path", out var fp) || fp.GetString() is not { Length: > 0 } path)
                return null;
            if (r.TryGetProperty("file_size", out var size) && size.TryGetInt64(out var bytes) && bytes > MaxReceiptBytes) return null;
            var content = await http.GetByteArrayAsync($"https://api.telegram.org/file/bot{token}/{path}", ct);
            return content.Length is > 0 and <= (int)MaxReceiptBytes ? content : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Customer bot: downloading a receipt photo failed");
            return null;
        }
    }

    private async Task AnswerAsync(string token, string callbackId, string text, CancellationToken ct)
    {
        if (callbackId.Length == 0) return;
        var fields = new Dictionary<string, string> { ["callback_query_id"] = callbackId };
        if (text.Length > 0) fields["text"] = text;
        await PostAsync(token, "answerCallbackQuery", fields, ct);
    }

    // The tracking number a customer may have written under the receipt: its digits, Persian ones included.
    private static string? Digits(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var digits = new string(text.Select(ch => ch is >= '۰' and <= '۹' ? (char)('0' + (ch - '۰'))
            : ch is >= '٠' and <= '٩' ? (char)('0' + (ch - '٠')) : ch).Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 0 ? null : digits.Length > 30 ? digits[..30] : digits;
    }
}

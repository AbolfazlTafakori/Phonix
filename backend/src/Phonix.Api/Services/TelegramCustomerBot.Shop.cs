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
// A purchase here is an ordinary order: the same PlaceOrder, the same receipt review in the receipt bot, the
// same automatic delivery — the bought account arrives in this chat, as everything for this account does.
// Someone without a linked site account buys with a Telegram-only account (AppUser.TelegramGuestChatId).
//
// The guards that make an open door safe: one unreviewed purchase at a time and a few a day per person, the
// price held for 30 minutes so what was paid is what is charged, and only plans that ask the buyer for nothing.
public sealed partial class TelegramCustomerBot
{
    private const string MenuBuy = "🛍 محصولات";
    private const string MenuOrders = "📦 سفارش‌های من";
    private const string MenuSupport = "💬 پشتیبانی";
    private const string MenuAccount = "🔗 حساب سایت";
    private const string MenuSiteShop = "🛒 فروشگاه سایت";

    public const string PlacedViaBot = "telegram-bot";
    private const int MaxBotOrdersPerDay = 5;
    private const long MaxReceiptBytes = 6 * 1024 * 1024;
    private const int MaxSupportMessage = 2000;
    private static readonly TimeSpan PriceHold = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan SupportWait = TimeSpan.FromMinutes(10);

    // What a chat is in the middle of: paying for a chosen plan (the price is held), or writing to support.
    private sealed record Session(string Kind, int ProductId, int? PlanId, long UnitPrice, long Total, int MethodId, DateTime Until);
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

    // ── Keyboards ───────────────────────────────────────────────────────────────────────────────────────────

    // The menu under the message box, for everyone who starts the bot.
    private string Menu(long chatId)
    {
        var rows = new List<object[]>
        {
            new object[] { new { text = MenuBuy }, new { text = MenuOrders } },
            new object[] { new { text = MenuSupport }, new { text = MenuAccount } },
        };
        if (ShopOn) rows.Add(new object[] { new { text = MenuSiteShop, web_app = new { url = ShopUrl } } });
        return JsonSerializer.Serialize(new { keyboard = rows, resize_keyboard = true, is_persistent = true });
    }

    private static string Inline(IEnumerable<object[]> rows) => JsonSerializer.Serialize(new { inline_keyboard = rows });

    private static string CancelKeyboard() => Inline(new[] { new object[] { new { text = "❌ انصراف", callback_data = "shop:x" } } });

    private string RegisterKeyboard() => Inline(new[]
    {
        new object[] { new { text = "📝 ثبت‌نام در سایت", url = $"{FrontendUrl.TrimEnd('/')}/signup" } },
        new object[] { new { text = "🔗 اتصال حساب به تلگرام", url = $"{FrontendUrl.TrimEnd('/')}/account/telegram" } },
    });

    // ── Messages ────────────────────────────────────────────────────────────────────────────────────────────

    // The menu buttons, a receipt photo for a purchase in progress, a message for support. False: not ours.
    private async Task<bool> TryHandleShopMessageAsync(string token, long chatId, JsonElement msg, string text, JsonElement from, CancellationToken ct)
    {
        switch (text)
        {
            case MenuBuy:
                Sessions.TryRemove(chatId, out _);
                await ShowCatalogAsync(token, chatId, ct);
                return true;
            case MenuOrders:
                Sessions.TryRemove(chatId, out _);
                await ShowOrdersAsync(token, chatId, ct);
                return true;
            case MenuSupport:
                Sessions[chatId] = new Session("support", 0, null, 0, 0, 0, DateTime.UtcNow + SupportWait);
                await ReplyAsync(token, chatId, "💬 پیامتان را برای پشتیبانی بنویسید و بفرستید؛ پاسخ همین‌جا برایتان می‌آید.", ct, CancelKeyboard());
                return true;
            case MenuAccount:
                await ShowAccountAsync(token, chatId, ct);
                return true;
        }

        if (!Sessions.TryGetValue(chatId, out var session)) return false;
        if (session.Until < DateTime.UtcNow)
        {
            Sessions.TryRemove(chatId, out _);
            if (session.Kind == "receipt" && ImageOf(msg) is not null)
            {
                await ReplyAsync(token, chatId, $"زمان این خرید تمام شده است. از «{MenuBuy}» دوباره انتخاب کنید تا قیمت به‌روز نشان داده شود.", ct, Menu(chatId));
                return true;
            }
            return false;
        }

        if (session.Kind == "receipt")
        {
            if (ImageOf(msg) is { } image)
            {
                await ReceiveReceiptAsync(token, chatId, msg, image, from, session, ct);
                return true;
            }
            if (text.Length > 0 && !text.StartsWith('/'))
            {
                await ReplyAsync(token, chatId, "لطفاً عکس رسید پرداخت را بفرستید، یا «انصراف» را بزنید.", ct, CancelKeyboard());
                return true;
            }
            return false;
        }

        if (session.Kind == "support" && text.Length > 0 && !text.StartsWith('/'))
        {
            Sessions.TryRemove(chatId, out _);
            await SendToSupportAsync(token, chatId, text, from, ct);
            return true;
        }
        return false;
    }

    private async Task ShowCatalogAsync(string token, long chatId, CancellationToken ct)
    {
        var withProducts = _store.GetProducts().Where(p => p.IsActive).Select(p => p.CategoryId).ToHashSet();
        var categories = _store.GetCategories().Where(c => c.IsActive && withProducts.Contains(c.Id)).OrderBy(c => c.SortOrder).ToList();
        if (categories.Count == 0)
        {
            await ReplyAsync(token, chatId, "فعلاً محصولی برای نمایش وجود ندارد.", ct, Menu(chatId));
            return;
        }
        var rows = categories.Select(c => new object[] { new { text = c.Name, callback_data = $"shop:c:{c.Id}" } });
        await ReplyAsync(token, chatId, "🛍 دسته‌بندی محصولات:" + (SalesOn ? "\n\nمحصولاتی که با 🔓 مشخص شده‌اند، همین‌جا و بدون ثبت‌نام خریده می‌شوند." : ""),
            ct, Inline(rows));
    }

    private async Task ShowCategoryAsync(string token, long chatId, int categoryId, CancellationToken ct)
    {
        var category = _store.GetCategory(categoryId);
        var products = _store.GetProducts(categoryId).Where(p => p.IsActive).ToList();
        if (category is null || !category.IsActive || products.Count == 0)
        {
            await ShowCatalogAsync(token, chatId, ct);
            return;
        }
        var rows = products
            .Select(p => new object[] { new { text = (SalesOn && GuestSellable(p) ? "🔓 " : "") + p.Name, callback_data = $"shop:p:{p.Id}" } })
            .Append(new object[] { new { text = "⬅️ دسته‌بندی‌ها", callback_data = "shop:cats" } });
        await ReplyAsync(token, chatId, $"🛍 {category.Name}:", ct, Inline(rows));
    }

    private async Task ShowAccountAsync(string token, long chatId, CancellationToken ct)
    {
        if (_store.FindUserByTelegramChat(chatId) is { } user)
        {
            await ReplyAsync(token, chatId,
                $"حساب «{DisplayName(user)}» ({user.Username}) به این تلگرام وصل است ✅\n\nبرای قطع اتصال: /stop", ct, Menu(chatId));
            return;
        }
        await ReplyAsync(token, chatId,
            "این تلگرام به حساب سایت وصل نیست.\n\n"
            + "برای خرید همه‌ی محصولات و دیدن سوابقتان در سایت:\n"
            + "۱. در سایت ثبت‌نام کنید (یا وارد شوید).\n"
            + "۲. از منوی حساب کاربری «اتصال به تلگرام» را بزنید.\n"
            + $"۳. کد {CodeLengthFa} رقمی‌ای را که به ایمیلتان می‌آید همین‌جا بفرستید.", ct, RegisterKeyboard());
    }

    private async Task ReceiveReceiptAsync(string token, long chatId, JsonElement msg, (string FileId, string Ext) image,
        JsonElement from, Session session, CancellationToken ct)
    {
        if (_files is null) return;
        var product = _store.GetProduct(session.ProductId);
        if (product is null || !GuestSellable(product) || !SalesOn)
        {
            Sessions.TryRemove(chatId, out _);
            await ReplyAsync(token, chatId, "این محصول دیگر در ربات قابل خرید نیست. اگر مبلغ را واریز کرده‌اید، از «💬 پشتیبانی» پیام دهید.", ct, Menu(chatId));
            return;
        }
        var buyer = BuyerFor(chatId, from);
        if (PurchaseBlocked(Buyers(chatId)) is { } blocked)
        {
            Sessions.TryRemove(chatId, out _);
            await ReplyAsync(token, chatId, blocked, ct, Menu(chatId));
            return;
        }

        var bytes = await DownloadAsync(token, image.FileId, ct);
        if (bytes is null)
        {
            await ReplyAsync(token, chatId, "دریافت عکس انجام نشد (حداکثر ۶ مگابایت). لطفاً دوباره بفرستید.", ct, CancelKeyboard());
            return;
        }
        var upload = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "receipt" + image.Ext) { Headers = new HeaderDictionary() };
        var saved = await _files.SaveAsync(buyer.Id, "receipts", upload, ct);
        if (saved.Id is null)
        {
            await ReplyAsync(token, chatId, saved.Error ?? "این فایل تصویر معتبری نیست. عکس رسید را دوباره بفرستید.", ct, CancelKeyboard());
            return;
        }

        var caption = msg.TryGetProperty("caption", out var c) ? c.GetString() : null;
        var result = _store.PlaceOrder(buyer, new[] { (session.ProductId, 1, session.PlanId) }, "کارت به کارت", fromWallet: false,
            paymentMethodId: session.MethodId,
            lockedPrices: new Dictionary<(int productId, int? planId), long> { [(session.ProductId, session.PlanId)] = session.UnitPrice },
            bot: new BotCheckout(saved.Id, Digits(caption), PayerName(from)));
        Sessions.TryRemove(chatId, out _);
        if (result.Order is not { } order)
        {
            _logger.LogWarning("Customer bot purchase failed for chat {Chat}: {Error}", chatId, result.Error);
            await ReplyAsync(token, chatId,
                $"ثبت سفارش انجام نشد: {result.Error}\nاگر مبلغ را واریز کرده‌اید، از «{MenuSupport}» پیام دهید تا پیگیری شود.", ct, Menu(chatId));
            return;
        }

        // To staff, exactly like a receipt from the site's checkout.
        var payment = _store.GetUserTransactions(buyer.Id)
            .FirstOrDefault(t => t.OrderCode == order.Code && t.Type == TxTypes.OrderPayment && t.Status == TxStatus.Pending);
        if (payment is not null && Resolve<ITelegramReceiptService>() is { } receipts) _ = receipts.NotifyDepositAsync(payment);
        _logger.LogInformation("Customer bot: order {Code} placed by user {UserId}", order.Code, buyer.Id);

        var guest = buyer.TelegramGuestChatId is not null;
        await ReplyAsync(token, chatId,
            $"✅ سفارش {order.Code} ثبت شد.\n\nرسید شما بررسی می‌شود؛ پس از تأیید، اطلاعات اکانت همین‌جا برایتان ارسال می‌شود."
            + (guest ? $"\n\nبرای نگه داشتن سوابق خرید در سایت، می‌توانید ثبت‌نام کنید و حسابتان را وصل کنید ({MenuAccount})." : ""),
            ct, Menu(chatId));
    }

    private async Task ShowOrdersAsync(string token, long chatId, CancellationToken ct)
    {
        var orders = Buyers(chatId).SelectMany(b => _store.GetUserOrders(b.Id)).OrderByDescending(o => o.Id).Take(5).ToList();
        if (orders.Count == 0)
        {
            await ReplyAsync(token, chatId, "هنوز سفارشی ندارید.", ct, Menu(chatId));
            return;
        }
        var lines = orders.Select(o => $"• {o.Code} — {o.Items.FirstOrDefault()?.Name ?? "-"} — {StatusFa(o.Status)}");
        var rows = orders.Select(o => new object[] { new { text = $"📄 {o.Code}", callback_data = $"ord:v:{o.Id}" } });
        await ReplyAsync(token, chatId, "📦 سفارش‌های اخیر شما:\n\n" + string.Join("\n", lines), ct, Inline(rows));
    }

    private static string StatusFa(OrderStatus s) => s switch
    {
        OrderStatus.PendingApproval => "در انتظار تأیید پرداخت",
        OrderStatus.Preparing => "در حال آماده‌سازی",
        OrderStatus.Completed => "تحویل شده",
        _ => "لغو شده",
    };

    private async Task SendToSupportAsync(string token, long chatId, string text, JsonElement from, CancellationToken ct)
    {
        var buyer = BuyerFor(chatId, from);
        if (text.Length > MaxSupportMessage) text = text[..MaxSupportMessage];
        var conversation = _store.SendUserMessage(buyer.Id, DisplayName(buyer), text, viaTelegram: true);
        if (Resolve<ITelegramSupportBot>() is { } support && conversation.Messages.LastOrDefault() is { } sent)
            _ = support.NotifyChatMessageAsync(conversation, sent);
        await ReplyAsync(token, chatId, "✅ پیامتان برای پشتیبانی ارسال شد؛ پاسخ همین‌جا برایتان می‌آید.", ct, Menu(chatId));
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
        var parts = data.Split(':');
        await AnswerAsync(token, callbackId, "", ct);

        switch (parts)
        {
            case ["shop", "x"]:
                Sessions.TryRemove(chatId, out _);
                await ReplyAsync(token, chatId, "لغو شد.", ct, Menu(chatId));
                break;
            case ["shop", "cats"]:
                await ShowCatalogAsync(token, chatId, ct);
                break;
            case ["shop", "c", var c] when int.TryParse(c, out var categoryId):
                await ShowCategoryAsync(token, chatId, categoryId, ct);
                break;
            case ["shop", "p", var p] when int.TryParse(p, out var productId):
                await ShowProductAsync(token, chatId, productId, ct);
                break;
            case ["shop", "b", var p, var pl] when int.TryParse(p, out var productId) && int.TryParse(pl, out var planId):
                await StartPurchaseAsync(token, chatId, productId, planId == 0 ? null : planId, ct);
                break;
            case ["ord", "v", var o] when int.TryParse(o, out var orderId):
                await ShowOrderAsync(token, chatId, orderId, ct);
                break;
        }
    }

    private static string PlanLabel(ProductPlan plan) => $"{plan.Type} · {plan.Months} ماهه";

    private static bool InStock(Product p) => p.IsPanelProvisioned || p.Stock > 0;

    // Everything about a product is shown to anyone; what it takes to buy it is said up front.
    private async Task ShowProductAsync(string token, long chatId, int productId, CancellationToken ct)
    {
        if (_store.GetProduct(productId) is not { IsActive: true } product)
        {
            await ReplyAsync(token, chatId, "این محصول در دسترس نیست.", ct, Menu(chatId));
            return;
        }
        var plans = product.Plans.Where(x => x.IsActive).ToList();
        var guest = SalesOn && GuestSellable(product);
        var lines = new List<string> { $"🛍 {product.Name}" };
        if (Summary(product.Description) is { Length: > 0 } about) lines.Add("\n" + about);
        if (!string.IsNullOrWhiteSpace(product.Warning)) lines.Add($"\n⚠️ {product.Warning.Trim()}");
        lines.Add("");
        if (!InStock(product)) lines.Add("❌ ناموجود");
        else if (guest)
            lines.Add(GuestPlans(product).Count < plans.Count
                ? "🔓 گزینه‌هایی که با 🔓 مشخص شده‌اند، همین‌جا و بدون ثبت‌نام خریده می‌شوند؛ بقیه با حساب سایت."
                : "🔓 همین‌جا و بدون ثبت‌نام قابل خرید است.");
        else
            lines.Add(product.RequiredLevel > 1 ? "🔐 خرید با حساب سایت و احراز هویت سطح ۲" : "🔐 خرید با حساب سایت");

        var guestPlans = GuestPlans(product).Select(p => p.Id).ToHashSet();
        var rows = new List<object[]>();
        if (InStock(product))
        {
            if (plans.Count == 0)
                rows.Add(new object[] { new { text = $"{(guest ? "🔓 " : "")}خرید — {Money(product.FinalPrice)} تومان", callback_data = $"shop:b:{product.Id}:0" } });
            else
                rows.AddRange(plans.Select(pl => new object[]
                {
                    new { text = $"{(guest && guestPlans.Contains(pl.Id) ? "🔓 " : "")}{PlanLabel(pl)} — {Money(pl.FinalPrice)} تومان", callback_data = $"shop:b:{product.Id}:{pl.Id}" },
                }));
        }
        rows.Add(new object[] { new { text = "⬅️ بازگشت", callback_data = $"shop:c:{product.CategoryId}" } });
        await SendProductAsync(token, chatId, product, string.Join("\n", lines), Inline(rows), ct);
    }

    // The product's picture with the text under it when Telegram can fetch the picture; the text alone otherwise.
    private async Task SendProductAsync(string token, long chatId, Product product, string text, string markup, CancellationToken ct)
    {
        var image = string.IsNullOrWhiteSpace(product.ListImage) ? product.Image : product.ListImage;
        if (ShopUrl is { } site && !string.IsNullOrWhiteSpace(image) && text.Length <= 1000)
        {
            var url = image.StartsWith('/') ? site.TrimEnd('/') + image : image;
            if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var http = _httpFactory.CreateClient();
                    http.Timeout = TimeSpan.FromSeconds(20);
                    using var form = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["chat_id"] = chatId.ToString(), ["photo"] = url, ["caption"] = text, ["reply_markup"] = markup,
                    });
                    using var resp = await http.PostAsync($"https://api.telegram.org/bot{token}/sendPhoto", form, ct);
                    if (resp.IsSuccessStatusCode) return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Customer bot: product picture failed, sending text");
                }
            }
        }
        await ReplyAsync(token, chatId, text, ct, markup);
    }

    // A short plain-text look at a product description written in the site's Markdown.
    private static string Summary(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "";
        var text = System.Text.RegularExpressions.Regex.Replace(markdown, @"!\[[^\]]*\]\([^)]*\)", "");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\[([^\]]+)\]\([^)]*\)", "$1");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\{#[a-z0-9-]+\}", "");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"^\s*(#{1,6}|\|.*\||-{3,})\s*", "", System.Text.RegularExpressions.RegexOptions.Multiline);
        text = text.Replace("**", "").Replace("*", "");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n").Trim();
        return text.Length > 450 ? text[..450].TrimEnd() + "…" : text;
    }

    // A product only a site account can buy: say exactly what is missing — or, when nothing is, continue in the
    // site's checkout inside Telegram, where every rule of the site applies as it is.
    private async Task AccountRequiredAsync(string token, long chatId, Product product, CancellationToken ct)
    {
        var site = FrontendUrl.TrimEnd('/');
        object Open(string text, string path) => ShopOn
            ? new { text, web_app = new { url = ShopUrl!.TrimEnd('/') + path } }
            : new { text, url = site + path };

        if (_store.FindUserByTelegramChat(chatId) is not { } user)
        {
            await ReplyAsync(token, chatId,
                $"🔐 خرید «{product.Name}» فقط با حساب سایت ممکن است.\n\n"
                + "۱. در سایت ثبت‌نام کنید.\n"
                + (product.RequiredLevel > 1
                    ? "۲. در حساب کاربری، احراز هویت سطح ۲ (کارت ملی) را انجام دهید.\n"
                    : "۲. در حساب کاربری، کارت بانکی‌تان را ثبت کنید تا تأیید شود.\n")
                + $"۳. از منوی حساب کاربری «اتصال به تلگرام» را بزنید و کد {CodeLengthFa} رقمی‌ای را که به ایمیلتان می‌آید همین‌جا بفرستید.\n\n"
                + "بعد از اتصال، خرید را از همین ربات ادامه دهید.", ct, RegisterKeyboard());
            return;
        }
        if (user.VerificationLevel < product.RequiredLevel)
        {
            var (what, path) = product.RequiredLevel > 1 && user.VerificationLevel >= 1
                ? ("احراز هویت سطح ۲ (کارت ملی)", "/account/kyc")
                : ("ثبت و تأیید کارت بانکی (سطح ۱)", "/account/cards");
            await ReplyAsync(token, chatId,
                $"سطح احراز هویت حساب شما برای «{product.Name}» کافی نیست. ابتدا در سایت {what} را انجام دهید.",
                ct, Inline(new[] { new object[] { Open("🪪 احراز هویت", path) } }));
            return;
        }
        await ReplyAsync(token, chatId,
            $"پرداخت «{product.Name}» در فروشگاه سایت انجام می‌شود" + (ShopOn ? "؛ همین‌جا داخل تلگرام باز می‌شود و حسابتان وارد است." : "."),
            ct, Inline(new[] { new object[] { Open("💳 ادامه‌ی خرید", $"/products/{product.Id}") } }));
    }

    private async Task StartPurchaseAsync(string token, long chatId, int productId, int? planId, CancellationToken ct)
    {
        if (_store.GetProduct(productId) is not { IsActive: true } product)
        {
            await ReplyAsync(token, chatId, "این محصول در دسترس نیست.", ct, Menu(chatId));
            return;
        }
        var plan = planId is int id ? product.Plans.FirstOrDefault(p => p.Id == id && p.IsActive) : null;
        if ((planId is not null && plan is null) || (planId is null && product.Plans.Any(p => p.IsActive)))
        {
            await ReplyAsync(token, chatId, "این گزینه دیگر در دسترس نیست. دوباره از فهرست انتخاب کنید.", ct, Menu(chatId));
            return;
        }
        if (!InStock(product))
        {
            await ReplyAsync(token, chatId, "این محصول فعلاً ناموجود است.", ct, Menu(chatId));
            return;
        }
        // Bought here without an account only when staff opened it and this option asks the buyer for nothing.
        var guest = SalesOn && GuestSellable(product) && (plan is null || GuestPlans(product).Any(p => p.Id == plan.Id));
        if (!guest)
        {
            await AccountRequiredAsync(token, chatId, product, ct);
            return;
        }
        if (PurchaseBlocked(Buyers(chatId)) is { } blocked)
        {
            await ReplyAsync(token, chatId, blocked, ct, Menu(chatId));
            return;
        }
        if (PaymentCard() is not { } card)
        {
            await ReplyAsync(token, chatId, "پرداخت در ربات فعلاً ممکن نیست. لطفاً بعداً امتحان کنید یا از سایت خرید کنید.", ct, Menu(chatId));
            return;
        }

        var price = plan?.FinalPrice ?? product.FinalPrice;
        var (vat, fee, total) = Quote(price, card);
        Sessions[chatId] = new Session("receipt", product.Id, plan?.Id, price, total, card.Id, DateTime.UtcNow + PriceHold);

        var title = plan is null ? product.Name : $"{product.Name} — {PlanLabel(plan)}";
        var amounts = vat > 0 || fee > 0
            ? $"قیمت: {Money(price)} تومان" + (vat > 0 ? $"\nمالیات: {Money(vat)} تومان" : "") + (fee > 0 ? $"\nکارمزد: {Money(fee)} تومان" : "") + "\n"
            : "";
        var instructions = string.IsNullOrWhiteSpace(card.Instructions) ? "" : $"\n{card.Instructions.Trim()}\n";
        await ReplyAsync(token, chatId,
            $"🧾 {title}\n\n{amounts}💰 مبلغ قابل پرداخت: {Money(total)} تومان\n\n"
            + $"💳 شماره کارت:\n{card.Value.Trim()}\n👤 به نام: {card.Holder}\n{instructions}\n"
            + "📸 پس از واریز، عکس رسید را همین‌جا بفرستید. اگر شماره پیگیری دارید، آن را در توضیح عکس بنویسید.\n"
            + $"⏳ این قیمت تا {JalaliDate.ToPersianDigits(((int)PriceHold.TotalMinutes).ToString())} دقیقه برایتان ثابت است.",
            ct, CancelKeyboard());
    }

    private async Task ShowOrderAsync(string token, long chatId, int orderId, CancellationToken ct)
    {
        var mine = Buyers(chatId).Select(b => b.Id).ToHashSet();
        if (_store.GetOrder(orderId) is not { } order || !mine.Contains(order.UserId))
        {
            await ReplyAsync(token, chatId, "این سفارش یافت نشد.", ct, Menu(chatId));
            return;
        }
        var lines = new List<string> { $"📄 سفارش {order.Code}", $"وضعیت: {StatusFa(order.Status)}", $"مبلغ: {Money(order.Total)} تومان" };
        foreach (var unit in order.Units.OrderBy(u => u.Id))
        {
            lines.Add("");
            lines.Add($"▫️ {unit.Name}{(string.IsNullOrWhiteSpace(unit.Plan) ? "" : $" — {unit.Plan}")}");
            if (unit.Delivered && !string.IsNullOrWhiteSpace(unit.DeliveryContent)) lines.Add(unit.DeliveryContent.Trim());
            else if (unit.Rejected) lines.Add("رد شد و مبلغ آن برگشت داده شد.");
            else lines.Add("هنوز تحویل نشده است.");
        }
        await ReplyAsync(token, chatId, string.Join("\n", lines), ct, Menu(chatId));
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
        try
        {
            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(15);
            var fields = new Dictionary<string, string> { ["callback_query_id"] = callbackId };
            if (text.Length > 0) fields["text"] = text;
            using var form = new FormUrlEncodedContent(fields);
            using var resp = await http.PostAsync($"https://api.telegram.org/bot{token}/answerCallbackQuery", form, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Customer bot answerCallbackQuery failed");
        }
    }

    // The tracking number a customer may have written under the receipt: its digits, Persian ones included.
    private static string? Digits(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var digits = new string(text.Select(ch => ch is >= '۰' and <= '۹' ? (char)('0' + (ch - '۰'))
            : ch is >= '٠' and <= '٩' ? (char)('0' + (ch - '٠')) : ch).Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 0 ? null : digits.Length > 30 ? digits[..30] : digits;
    }

    private static string Money(long toman) => toman.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
}

using System.Text.Json;
using Phonix.Api.Data;
using Phonix.Api.Models;

namespace Phonix.Api.Services;

// The customer's own corner of the bot: their orders (a page at a time, searchable by code), each order as a
// card with what was delivered and — for a V2Ray or WireGuard service — its volume, expiry and the page to renew
// it; their account (wallet, identity level, notices on or off); the invite link; the guides for what they
// bought; and support. Notices about an order arrive with the buttons to act on them.
public sealed partial class TelegramCustomerBot
{
    private const int OrdersPerPage = 6;

    // ── Orders ──────────────────────────────────────────────────────────────────────────────────────────────

    private List<Order> MyOrders(long chatId) =>
        Buyers(chatId).SelectMany(b => _store.GetUserOrders(b.Id)).OrderByDescending(o => o.Id).ToList();

    private static string StatusFa(OrderStatus s) => s switch
    {
        OrderStatus.PendingApproval => "در انتظار تأیید پرداخت",
        OrderStatus.Preparing => "در حال آماده‌سازی",
        OrderStatus.Completed => "تحویل شده",
        _ => "لغو شده",
    };

    private static string StatusIcon(OrderStatus s) => s switch
    {
        OrderStatus.PendingApproval => "⏳",
        OrderStatus.Preparing => "🛠",
        OrderStatus.Completed => "✅",
        _ => "❌",
    };

    private async Task ShowOrdersAsync(string token, Screen at, int page, CancellationToken ct)
    {
        var orders = MyOrders(at.ChatId);
        if (orders.Count == 0)
        {
            await ShowAsync(token, at, "📦 سفارش‌های من\n\nهنوز سفارشی ندارید.",
                Inline(new[] { new[] { Btn("🛍 خرید محصول", "shop:cats") }, HomeRow() }), ct);
            return;
        }
        var pages = (orders.Count + OrdersPerPage - 1) / OrdersPerPage;
        page = Math.Clamp(page, 1, pages);
        var rows = orders.Skip((page - 1) * OrdersPerPage).Take(OrdersPerPage)
            .Select(o => new[] { Btn($"{StatusIcon(o.Status)} {o.Code} · {Short(o.Items.FirstOrDefault()?.Name, 22)}", $"ord:v:{o.Id}") })
            .ToList();
        if (pages > 1)
        {
            var pager = new List<object>();
            if (page > 1) pager.Add(Btn("« قبلی", $"ord:l:{page - 1}"));
            pager.Add(Btn($"صفحه {Fa(page)} از {Fa(pages)}", "noop"));
            if (page < pages) pager.Add(Btn("بعدی »", $"ord:l:{page + 1}"));
            rows.Add(pager.ToArray());
        }
        rows.Add(new[] { Btn("🔎 جستجوی سفارش", "ord:s") });
        rows.Add(HomeRow());
        await ShowAsync(token, at,
            $"📦 سفارش‌های من ({Fa(orders.Count)})\n\nبرای دیدن جزئیات و اطلاعات اکانت، روی سفارش بزنید.\n\n⏳ در انتظار تأیید · 🛠 در حال آماده‌سازی · ✅ تحویل شده · ❌ لغو شده",
            Inline(rows), ct);
    }

    // An order of this chat's, as a card: the delivered accounts in the text (one tap copies them), the facts as
    // a table, and for a V2Ray or WireGuard service its numbers and the page that renews it.
    private async Task ShowOrderAsync(string token, Screen at, int orderId, CancellationToken ct)
    {
        var mine = Buyers(at.ChatId).Select(b => b.Id).ToHashSet();
        if (_store.GetOrder(orderId) is not { } order || !mine.Contains(order.UserId))
        {
            await ShowAsync(token, at, "این سفارش یافت نشد.", Inline(new[] { NavRow("ord:l:1", "⬅️ سفارش‌ها") }), ct);
            return;
        }

        var units = order.Units.OrderBy(u => u.Id).ToList();
        var text = new List<string> { $"📄 <b>سفارش {H(order.Code)}</b>" };
        // The accounts share one message: each gets its part of Telegram's limit, so the card is never cut short.
        var room = Math.Max(250, 2800 / Math.Clamp(units.Count, 1, 8));
        foreach (var unit in units.Take(8))
        {
            text.Add("");
            text.Add($"▫️ <b>{H(unit.Name)}</b>{(string.IsNullOrWhiteSpace(unit.Plan) ? "" : $" — {H(unit.Plan)}")}");
            if (unit.Delivered && !string.IsNullOrWhiteSpace(unit.DeliveryContent))
            {
                var content = unit.DeliveryContent.Trim();
                text.Add($"<pre>{H(content.Length > room ? content[..room] + "…" : content)}</pre>");
            }
            else if (unit.Rejected) text.Add("❌ رد شد و مبلغ آن برگشت داده شد.");
            else if (order.Status == OrderStatus.Cancelled) text.Add("❌ لغو شده");
            else text.Add("⏳ هنوز تحویل نشده است.");
        }
        if (units.Count > 8) text.Add($"\n… و {Fa(units.Count - 8)} اکانت دیگر.");
        if (units.Count == 0 && !string.IsNullOrWhiteSpace(order.DeliveryContent))
            text.Add($"\n<pre>{H(order.DeliveryContent.Trim().Length > 2000 ? order.DeliveryContent.Trim()[..2000] + "…" : order.DeliveryContent.Trim())}</pre>");

        var rows = new List<object[]>
        {
            Cell(order.Code, "🧾 کد سفارش"),
            Cell($"{StatusIcon(order.Status)} {StatusFa(order.Status)}", "📍 وضعیت"),
            Cell(JalaliDate.ToPersianDigits(order.Date), "📅 تاریخ ثبت"),
            Cell(Toman(order.Total), "💰 مبلغ کل"),
        };
        var months = order.Items.Where(i => i.PlanMonths is > 0).Select(i => i.PlanMonths!.Value).DefaultIfEmpty(0).Max();
        if (order.Status == OrderStatus.Completed && order.DeliveredAtUtc is DateTime delivered && months > 0
            && !units.Any(u => u.V2Ray is not null || u.WireGuard is not null))
            rows.Add(Cell(JalaliDate.Format(delivered.AddMonths(months)), "⏳ اعتبار تا"));

        // Each service: its numbers, and its own page — the live usage, the config, and renewing it.
        var services = units.Where(u => u.V2Ray is { Token.Length: > 0 } || u.WireGuard is { Token.Length: > 0 }).Take(3).ToList();
        foreach (var unit in services)
        {
            var many = services.Count > 1 ? $" {Fa(unit.UnitIndex)}" : "";
            var (volume, expires, protocol, removed, path) = unit.V2Ray is { } v
                ? (v.VolumeGb, v.ExpiresAtUtc, v.Protocol, v.PanelDeletedAtUtc is not null, $"/config/{v.Token}")
                : (unit.WireGuard!.VolumeGb, unit.WireGuard.ExpiresAtUtc, "WireGuard", unit.WireGuard.PanelDeletedAtUtc is not null, $"/wg/{unit.WireGuard.Token}");
            if (removed) rows.Add(Cell("پایان یافته", $"🔌 سرویس{many}"));
            else
            {
                if (volume > 0) rows.Add(Cell($"{Fa(volume)} گیگابایت", $"📦 حجم{many}"));
                if (expires is DateTime e)
                    rows.Add(Cell(e <= DateTime.UtcNow ? "منقضی شده" : JalaliDate.Format(e), $"⏳ انقضا{many}"));
                if (!string.IsNullOrWhiteSpace(protocol)) rows.Add(Cell(protocol, $"🔌 پروتکل{many}"));
            }
            rows.Add(new[] { SiteButton($"🔗 صفحه‌ی سرویس{many} و تمدید", path) });
        }
        rows.Add(NavRow("ord:l:1", "⬅️ سفارش‌ها"));
        await ShowAsync(token, at, string.Join("\n", text), Inline(rows), ct, html: true);
    }

    private async Task FindMyOrderAsync(string token, long chatId, string query, CancellationToken ct)
    {
        var digits = Digits(query);
        var wanted = query.Trim();
        var found = MyOrders(chatId).FirstOrDefault(o =>
            o.Code.Equals(wanted, StringComparison.OrdinalIgnoreCase)
            || (digits is { Length: >= 3 } && Digits(o.Code) == digits));
        if (found is null)
        {
            await ReplyAsync(token, chatId, "سفارشی با این کد در سفارش‌های شما پیدا نشد.", ct,
                Inline(new[] { new[] { Btn("🔎 دوباره جستجو کنید", "ord:s") }, NavRow("ord:l:1", "⬅️ سفارش‌ها") }));
            return;
        }
        await ShowOrderAsync(token, new Screen(chatId), found.Id, ct);
    }

    // ── The account ─────────────────────────────────────────────────────────────────────────────────────────

    private static string LevelFa(int level) => level switch
    {
        >= 2 => "سطح ۲ ✅ (کارت ملی)",
        1 => "سطح ۱ (کارت بانکی)",
        _ => "سطح ۰ (احراز نشده)",
    };

    private async Task ShowAccountAsync(string token, Screen at, CancellationToken ct)
    {
        if (_store.FindUserByTelegramChat(at.ChatId) is { } user)
        {
            var rows = new List<object[]>
            {
                Cell(Short(DisplayName(user), 30), "👤 نام"),
                Cell(user.Username, "🆔 نام کاربری"),
                Cell(LevelFa(user.VerificationLevel), "🪪 احراز هویت"),
                Cell(Toman(user.Wallet), "💰 کیف پول"),
                Cell(Fa(user.Orders), "📦 سفارش‌ها"),
                Cell(Toman(user.TotalSpent), "💳 مجموع خرید"),
            };
            if (!string.IsNullOrWhiteSpace(user.JoinedAt)) rows.Add(Cell(JalaliDate.ToPersianDigits(user.JoinedAt), "📅 عضویت"));
            rows.Add(new[] { SiteButton("💰 شارژ کیف پول", "/account/wallet"), SiteButton("🪪 احراز هویت", "/account/kyc") });
            rows.Add(new[] { Btn(user.TelegramNotify ? "🔔 اعلان‌ها: روشن" : "🔕 اعلان‌ها: خاموش", "acc:notify"), Btn("🔌 قطع اتصال", "acc:unlink") });
            rows.Add(HomeRow());
            await ShowAsync(token, at,
                $"👤 حساب من\n\nحساب سایت «{DisplayName(user)}» به این تلگرام وصل است ✅\nسفارش‌ها، تأیید پرداخت‌ها و پیام‌های حسابتان اینجا هم می‌آید.\n\n🕒 {JalaliDate.NowFa()}",
                Inline(rows), ct);
            return;
        }

        var guest = _store.FindTelegramGuest(at.ChatId);
        var bought = guest is null ? 0 : _store.GetUserOrders(guest.Id).Count;
        await ShowAsync(token, at,
            "👤 حساب من\n\nاین تلگرام به حساب سایت وصل نیست."
            + (bought > 0 ? $"\nتا امروز {Fa(bought)} سفارش بدون ثبت‌نام از همین ربات ثبت کرده‌اید؛ همه در «📦 سفارش‌های من» هستند." : "")
            + "\n\nبرای خرید همه‌ی محصولات و دیدن سوابقتان در سایت:\n"
            + "۱. در سایت ثبت‌نام کنید (یا وارد شوید).\n"
            + "۲. از منوی حساب کاربری «اتصال به تلگرام» را بزنید.\n"
            + $"۳. کد {CodeLengthFa} رقمی‌ای را که به ایمیلتان می‌آید همین‌جا بفرستید.",
            RegisterKeyboard(null), ct);
    }

    private async Task ToggleNotifyAsync(string token, Screen at, CancellationToken ct)
    {
        if (_store.FindUserByTelegramChat(at.ChatId) is { } user)
            _store.UpdateUser(user.Id, u => u.TelegramNotify = !u.TelegramNotify);
        await ShowAccountAsync(token, at, ct);
    }

    // The invite link the site's own invite page hands out, with what it has earned so far.
    private async Task ShowReferralAsync(string token, Screen at, CancellationToken ct)
    {
        var percent = _store.GetSettings().ReferralCommissionPercent;
        if (_store.FindUserByTelegramChat(at.ChatId) is not { } user || percent <= 0)
        {
            await ShowAccountAsync(token, at, ct);
            return;
        }
        var link = $"{Site}/signup?ref={Uri.EscapeDataString(user.Username)}";
        var earnings = _store.GetReferralEarnings(user.Id);
        var share = $"https://t.me/share/url?url={Uri.EscapeDataString(link)}&text={Uri.EscapeDataString("با این لینک در فونیکس وریفای ثبت‌نام کن 👇")}";
        await ShowAsync(token, at,
            $"🎁 دعوت دوستان\n\nلینک اختصاصی شما:\n{link}\n\nهر کس با این لینک ثبت‌نام کند، از هر خریدش {JalaliDate.ToPersianDigits(percent.ToString("0.##"))}٪ به کیف پول شما اضافه می‌شود.",
            Inline(new[]
            {
                Cell($"{Fa(_store.CountReferredUsers(user.Id))} نفر", "👥 دعوت‌شده‌ها"),
                Cell(Fa(earnings.Count), "🛒 خریدهای آن‌ها"),
                Cell(Toman(earnings.Sum(e => e.Commission)), "💰 درآمد شما"),
                new[] { Link("📤 فرستادن لینک برای دوستان", share) },
                NavRow("acc", "⬅️ حساب من"),
            }), ct);
    }

    // ── Guides ──────────────────────────────────────────────────────────────────────────────────────────────

    // The guides of what this chat paid for — the same ones the site shows a buyer, and only those.
    private List<Tutorial> MyTutorials(long chatId)
    {
        var paid = Buyers(chatId).SelectMany(b => _store.GetUserOrders(b.Id))
            .Where(o => o.Status is OrderStatus.Preparing or OrderStatus.Completed)
            .SelectMany(o => o.Items.Select(i => i.ProductId)).ToHashSet();
        return paid.Count == 0
            ? new List<Tutorial>()
            : _store.GetTutorials().Where(t => t.IsActive && t.ProductIds.Any(paid.Contains)).OrderBy(t => t.SortOrder).ToList();
    }

    private async Task ShowTutorialsAsync(string token, Screen at, CancellationToken ct)
    {
        var tutorials = MyTutorials(at.ChatId);
        if (tutorials.Count == 0)
        {
            await ShowAsync(token, at, "📚 آموزش‌ها\n\nآموزش نصب و استفاده‌ی هر محصول، بعد از خرید آن همین‌جا برایتان باز می‌شود.",
                Inline(new[] { new[] { Btn("🛍 خرید محصول", "shop:cats") }, HomeRow() }), ct);
            return;
        }
        var rows = tutorials.Take(30).Select(t => new[] { Btn($"📖 {Short(t.Title, 34)}", $"tut:{t.Id}") }).Append(HomeRow());
        await ShowAsync(token, at, "📚 آموزش‌ها\n\nآموزش محصولاتی که خریده‌اید 👇", Inline(rows), ct);
    }

    private async Task ShowTutorialAsync(string token, Screen at, int tutorialId, CancellationToken ct)
    {
        if (MyTutorials(at.ChatId).FirstOrDefault(t => t.Id == tutorialId) is not { } tutorial)
        {
            await ShowTutorialsAsync(token, at, ct);
            return;
        }
        var body = Summary(tutorial.Body, 3400);
        var videos = tutorial.Videos.Count > 0
            ? $"\n\n🎬 این آموزش {Fa(tutorial.Videos.Count)} ویدیو هم دارد که در سایت، بخش سفارش‌های حساب کاربری دیده می‌شود."
            : "";
        await ShowAsync(token, at, $"📖 {tutorial.Title}\n\n{body}{videos}", Inline(new[] { NavRow("tut", "⬅️ آموزش‌ها") }), ct);
    }

    // ── Support ─────────────────────────────────────────────────────────────────────────────────────────────

    private Task StartSupportAsync(string token, Screen at, CancellationToken ct)
    {
        Sessions[at.ChatId] = Session.Typing("support");
        return ShowAsync(token, at,
            "💬 پشتیبانی\n\nپیامتان را بنویسید و بفرستید؛ پاسخ پشتیبانی همین‌جا برایتان می‌آید.\nاگر درباره‌ی سفارشی است، کد آن را هم بنویسید.",
            Inline(new[] { new[] { Btn("❌ انصراف", "home") } }), ct);
    }

    private async Task SendToSupportAsync(string token, long chatId, string text, JsonElement from, CancellationToken ct)
    {
        var buyer = BuyerFor(chatId, from);
        if (text.Length > MaxSupportMessage) text = text[..MaxSupportMessage];
        var conversation = _store.SendUserMessage(buyer.Id, DisplayName(buyer), text, viaTelegram: true);
        if (Resolve<ITelegramSupportBot>() is { } support && conversation.Messages.LastOrDefault() is { } sent)
            _ = support.NotifyChatMessageAsync(conversation, sent);
        await ReplyAsync(token, chatId, "✅ پیامتان برای پشتیبانی ارسال شد؛ پاسخ همین‌جا برایتان می‌آید.", ct,
            Inline(new[] { new[] { Btn("✍️ پیام دیگر", "sup") }, HomeRow() }));
    }

    // ── Notices ─────────────────────────────────────────────────────────────────────────────────────────────

    // A V2Ray or WireGuard service running out of time or volume. Someone who bought in the bot has no email at
    // all, so for them this is the only warning there is.
    public static string RunningOutNotice(string orderCode, string? expiresFa, string? remainingFa) =>
        $"⚠️ سرویس شما رو به پایان است — سفارش {orderCode}\n"
        + (expiresFa is { Length: > 0 } ? $"\n⏳ اعتبار تا {expiresFa}" : "")
        + (remainingFa is { Length: > 0 } ? $"\n📦 حجم باقی‌مانده: {remainingFa}" : "")
        + "\n\nبرای ادامه‌ی اتصال، سرویس را تمدید کنید؛ لینک اتصال شما تغییر نمی‌کند.";

    public static string RemovedNotice(string orderCode) =>
        $"🔌 پایان سرویس — سفارش {orderCode}\n\nمهلت تمدید این سرویس تمام شد و از سرور حذف شد. برای ادامه، از «🛍 خرید محصول» سرویس تازه بخرید.";

    // The Telegram copy of a notice from a background worker: never throws, never holds the worker up.
    public static async Task NotifyFromWorkerAsync(IDataStore store, ITelegramCustomerBot? telegram, ILogger logger,
        int userId, int orderId, string text, string? renewSitePath = null, int? renewProductId = null)
    {
        if (telegram is null || !telegram.IsActive || store.GetUser(userId) is not { } user) return;
        try
        {
            await telegram.NotifyOrderAsync(user, text, orderId, renewSitePath, renewProductId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Telegram notice for order {OrderId} failed", orderId);
        }
    }

    public async Task<bool> NotifyOrderAsync(AppUser user, string text, int orderId, string? renewSitePath = null,
        int? renewProductId = null, CancellationToken ct = default)
    {
        if (ActiveToken() is not { } token || !user.TelegramNotify || (user.TelegramChatId ?? user.TelegramGuestChatId) is not long chatId)
            return false;
        var rows = new List<object[]>();
        if (renewSitePath is not null) rows.Add(new[] { SiteButton("♻️ تمدید سرویس", renewSitePath) });
        else if (renewProductId is int productId) rows.Add(new[] { Btn("♻️ تمدید / خرید دوباره", $"shop:p:{productId}:n") });
        rows.Add(new[] { Btn("📄 مشاهده‌ی سفارش", $"ord:v:{orderId}:n") });
        return await ReplyAsync(token, chatId, text, ct, Inline(rows));
    }
}

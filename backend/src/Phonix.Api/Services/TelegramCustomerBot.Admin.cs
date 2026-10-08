using Phonix.Api.Data;
using Phonix.Api.Models;

namespace Phonix.Api.Services;

// Running the shop from the bot. A staff member who linked their own site account sees «⚙️ مدیریت فروشگاه» in
// the menu while staff management in the bot is switched on (TelegramSettings.CustomerBotAdmin, off until an
// Admin turns it on). Every section follows the panel's own permissions — an Admin holds all of them, a Support
// account only the sections granted to it — and both the role and the sections are read fresh on every press,
// so a revoked role or section takes effect on the next tap.
//
// It is the quick look and the quick switch, not a second panel: statistics, what is waiting, an order or a
// customer looked up, a product or a discount code switched on or off, a message to every customer in the bot,
// the bot's own switches. Every change is recorded in the panel's audit log under the staff member's name.
// Decisions on receipts and orders stay where they are made today: the receipt and order bots, and the panel.
public sealed partial class TelegramCustomerBot
{
    private static readonly string[] OrderSections = { "orders-status", "orders-receipts", "orders-fulfillment" };
    private static int _broadcasting;
    private static readonly TimeSpan BroadcastPace = TimeSpan.FromMilliseconds(60);

    // A staff member's own linked account, while staff management in the bot is on.
    private AppUser? StaffFor(long chatId)
    {
        if (!_store.GetTelegramSettings().CustomerBotAdmin) return null;
        return _store.FindUserByTelegramChat(chatId) is { Blocked: false } u && u.Role is UserRole.Admin or UserRole.Support ? u : null;
    }

    // The panel's rule: an Admin holds every section; Support only those granted to it.
    private static bool Can(AppUser staff, params string[] sections) =>
        staff.Role == UserRole.Admin || sections.Any(staff.Permissions.Contains);

    private void Audit(AppUser staff, AuditAction action, string entity, string? entityId, string what)
    {
        if (Resolve<AuditStore>() is not { } audit) return;
        audit.Record(new AuditLog
        {
            ActionType = action, Entity = entity, EntityId = entityId,
            ActorId = staff.Id, ActorName = staff.Username, ActorRole = staff.Role.ToString(),
            Method = "TELEGRAM", Path = $"ربات تلگرام: {what}", Ip = "telegram", StatusCode = 200, Success = true,
        });
    }

    private static object[] StaffNav(string? back = null) => back is null
        ? new[] { Btn("⬅️ مدیریت", "adm"), Btn(MenuHome, "home") }
        : new[] { Btn("⬅️ بازگشت", back), Btn("⚙️ مدیریت", "adm") };

    // ── Buttons ─────────────────────────────────────────────────────────────────────────────────────────────

    private async Task HandleStaffCallbackAsync(string token, Screen at, string[] parts, CancellationToken ct)
    {
        if (StaffFor(at.ChatId) is not { } staff)
        {
            await ShowHomeAsync(token, at, null, ct);
            return;
        }
        static bool Id(string s, out int id) => int.TryParse(s, out id);

        switch (parts)
        {
            case ["adm", "st"] when Can(staff, "reports"):
                await ShowStatsAsync(token, at, ct);
                break;
            case ["adm", "q"]:
                await ShowQueueAsync(token, at, staff, ct);
                break;
            case ["adm", "os"] when Can(staff, OrderSections):
                Sessions[at.ChatId] = Session.Typing("a-order");
                await ShowAsync(token, at, "🔎 جستجوی سفارش\n\nکد سفارش را بفرستید؛ مثلاً PX-100150 یا فقط 100150.", Inline(new[] { StaffNav() }), ct);
                break;
            case ["adm", "o", var o] when Can(staff, OrderSections) && Id(o, out var orderId):
                await ShowStaffOrderAsync(token, at, staff, orderId, ct);
                break;
            case ["adm", "us"] when Can(staff, "users"):
                Sessions[at.ChatId] = Session.Typing("a-user");
                await ShowAsync(token, at, "👤 جستجوی کاربر\n\nنام کاربری، نام، ایمیل یا شماره موبایل را بفرستید.", Inline(new[] { StaffNav() }), ct);
                break;
            case ["adm", "u", var u] when Can(staff, "users") && Id(u, out var userId):
                await ShowStaffUserAsync(token, at, staff, userId, ct);
                break;
            case ["adm", "uo", var u] when Can(staff, "users") && Can(staff, OrderSections) && Id(u, out var userId):
                await ShowStaffUserOrdersAsync(token, at, userId, ct);
                break;
            case ["adm", "pc"] when Can(staff, "products"):
                await ShowStaffCategoriesAsync(token, at, ct);
                break;
            case ["adm", "pl", var c] when Can(staff, "products") && Id(c, out var categoryId):
                await ShowStaffProductsAsync(token, at, categoryId, ct);
                break;
            case ["adm", "pr", var p] when Can(staff, "products") && Id(p, out var productId):
                await ShowStaffProductAsync(token, at, productId, null, ct);
                break;
            case ["adm", "pa" or "pg", var p] when Can(staff, "products") && Id(p, out var productId):
                await ToggleProductAsync(token, at, staff, productId, guestSale: parts[1] == "pg", ct);
                break;
            case ["adm", "dc"] when Can(staff, "discounts"):
                await ShowDiscountsAsync(token, at, ct);
                break;
            case ["adm", "d", var d] when Can(staff, "discounts") && Id(d, out var discountId):
                await ShowDiscountAsync(token, at, discountId, ct);
                break;
            case ["adm", "dt", var d] when Can(staff, "discounts") && Id(d, out var discountId):
                if (_store.GetDiscountCodes().FirstOrDefault(x => x.Id == discountId) is { } code)
                {
                    code.IsActive = !code.IsActive;
                    if (_store.UpdateDiscountCode(code))
                        Audit(staff, AuditAction.Update, "discounts", code.Id.ToString(), $"کد تخفیف {code.Code} {(code.IsActive ? "فعال" : "غیرفعال")} شد");
                }
                await ShowDiscountAsync(token, at, discountId, ct);
                break;
            case ["adm", "top"] when Can(staff, "reports"):
                await ShowTopBuyersAsync(token, at, staff, ct);
                break;
            case ["adm", "bc"] when Can(staff, "notifications"):
                Sessions[at.ChatId] = Session.Typing("a-cast");
                await ShowAsync(token, at,
                    "📣 پیام همگانی\n\nمتن پیام را بفرستید. برای "
                    + $"{Fa(BroadcastChats().Count)} مشتری فرستاده می‌شود: کسانی که تلگرامشان به ربات وصل است یا از ربات خرید کرده‌اند و اعلان‌ها را خاموش نکرده‌اند."
                    + "\n\nقبل از ارسال، پیش‌نمایش را می‌بینید.",
                    Inline(new[] { new[] { Btn("❌ انصراف", "adm") } }), ct);
                break;
            case ["adm", "bcy"] when Can(staff, "notifications"):
                await StartBroadcastAsync(token, at, staff, ct);
                break;
            case ["adm", "set"] when staff.Role == UserRole.Admin:
                await ShowBotSettingsAsync(token, at, null, ct);
                break;
            case ["adm", "set", "shop" or "sales"] when staff.Role == UserRole.Admin:
                await ToggleBotSettingAsync(token, at, staff, parts[2], ct);
                break;
            default:
                await ShowStaffHomeAsync(token, at, staff, ct);
                break;
        }
    }

    // What staff typed after a search or a broadcast asked for it.
    private async Task HandleStaffTextAsync(string token, long chatId, string kind, string text, CancellationToken ct)
    {
        var at = new Screen(chatId);
        if (StaffFor(chatId) is not { } staff)
        {
            await ShowHomeAsync(token, at, null, ct);
            return;
        }
        switch (kind)
        {
            case "a-order" when Can(staff, OrderSections):
                var digits = Digits(text);
                var order = _store.GetOrders().FirstOrDefault(o =>
                    o.Code.Equals(text.Trim(), StringComparison.OrdinalIgnoreCase) || (digits is { Length: >= 3 } && Digits(o.Code) == digits));
                if (order is null)
                    await ReplyAsync(token, chatId, "سفارشی با این کد پیدا نشد.", ct, Inline(new[] { new[] { Btn("🔎 دوباره جستجو کنید", "adm:os") }, StaffNav() }));
                else await ShowStaffOrderAsync(token, at, staff, order.Id, ct);
                break;
            case "a-user" when Can(staff, "users"):
                await FindUsersAsync(token, at, text, ct);
                break;
            case "a-cast" when Can(staff, "notifications"):
                var message = text.Length > 3500 ? text[..3500] : text;
                Sessions[chatId] = Session.Typing("a-cast-ok", message);
                var count = BroadcastChats().Count;
                await ReplyAsync(token, chatId, $"پیش‌نمایش پیام همگانی 👇\n\n📣 {message}\n\nگیرندگان: {Fa(count)} نفر", ct,
                    Inline(new[] { new[] { Btn($"✅ ارسال برای {Fa(count)} نفر", "adm:bcy"), Btn("❌ انصراف", "adm") } }));
                break;
            default:
                await ShowStaffHomeAsync(token, at, staff, ct);
                break;
        }
    }

    // ── The staff menu ──────────────────────────────────────────────────────────────────────────────────────

    private async Task ShowStaffHomeAsync(string token, Screen at, AppUser staff, CancellationToken ct)
    {
        Sessions.TryRemove(at.ChatId, out _);
        var b = _store.GetAdminBadgeCounts();
        var buttons = new List<object>();
        if (Can(staff, "reports")) buttons.Add(Btn("📊 آمار کلی", "adm:st"));
        buttons.Add(Btn("🧾 کارهای منتظر", "adm:q"));
        if (Can(staff, OrderSections)) buttons.Add(Btn("🔎 جستجوی سفارش", "adm:os"));
        if (Can(staff, "users")) buttons.Add(Btn("👤 جستجوی کاربر", "adm:us"));
        if (Can(staff, "products")) buttons.Add(Btn("🛍 محصولات", "adm:pc"));
        if (Can(staff, "discounts")) buttons.Add(Btn("🎟 کدهای تخفیف", "adm:dc"));
        if (Can(staff, "reports")) buttons.Add(Btn("🏆 برترین خریداران", "adm:top"));
        if (Can(staff, "notifications")) buttons.Add(Btn("📣 پیام همگانی", "adm:bc"));
        var rows = Pairs(buttons).ToList();
        if (staff.Role == UserRole.Admin) rows.Add(new[] { Btn("🤖 تنظیمات ربات", "adm:set") });
        rows.Add(new[] { Link("🌐 پنل مدیریت سایت", $"{Site}/admin") });
        rows.Add(HomeRow());
        await ShowAsync(token, at,
            $"⚙️ مدیریت فروشگاه\n\nسلام {DisplayName(staff)}، به بخش مدیریت خوش آمدید.\n\n"
            + $"🧾 {Fa(b.PendingOrders)} رسید در انتظار · 🛠 {Fa(b.PreparingOrders)} سفارش برای تحویل · 🎫 {Fa(b.OpenTickets)} تیکت باز\n\n"
            + "تغییراتی که اینجا می‌دهید به نام شما در لاگ ممیزی پنل ثبت می‌شود.",
            Inline(rows), ct);
    }

    // ── Statistics ──────────────────────────────────────────────────────────────────────────────────────────

    private async Task ShowStatsAsync(string token, Screen at, CancellationToken ct)
    {
        var users = _store.GetUsers(role: UserRole.Customer);
        var orders = _store.GetOrders();
        var today = DateTime.UtcNow.AddMinutes(210).Date;   // Tehran's calendar day
        // Tehran's day an order was placed on: its moment when it has one, its Jalali date otherwise.
        DateTime? Day(Order o) => o.PlacedAtUtc?.AddMinutes(210).Date ?? JalaliDate.TryParse(o.Date);
        long Sales(int days) => orders
            .Where(o => o.Status is OrderStatus.Preparing or OrderStatus.Completed && Day(o) is DateTime d && d > today.AddDays(-days))
            .Sum(o => o.Total);
        var paid = orders.Where(o => o.Status is OrderStatus.Preparing or OrderStatus.Completed).ToList();
        var products = _store.GetProducts();

        await ShowAsync(token, at, $"📊 آمار کلی فروشگاه\n\n🕒 {JalaliDate.NowFa()}", Inline(new[]
        {
            Cell(Fa(users.Count), "👥 کل مشتریان"),
            Cell(Fa(users.Count(u => JalaliDate.TryParse(u.JoinedAt) == today)), "🆕 عضو امروز"),
            Cell(Fa(paid.Select(o => o.UserId).Distinct().Count()), "🛒 خریداران"),
            Cell(Fa(users.Count(u => u.VerificationLevel >= 2)), "🪪 احراز سطح ۲"),
            Cell(Fa(users.Count(u => u.TelegramChatId is not null)), "🔗 وصل به تلگرام"),
            Cell(Fa(users.Count(u => u.TelegramGuestChatId is not null)), "🤖 خریدار بدون ثبت‌نام"),
            Cell(Fa(users.Count(u => u.Blocked)), "⛔ مسدود"),
            Cell($"{Fa(products.Count(p => p.IsActive))} از {Fa(products.Count)}", "🛍 محصولات فعال"),
            Cell(Fa(_store.GetCategories().Count(c => c.IsActive)), "🗂 دسته‌بندی‌ها"),
            Cell(Fa(orders.Count), "📦 کل سفارش‌ها"),
            Cell(Fa(orders.Count(o => o.Status != OrderStatus.Cancelled && Day(o) == today)), "📦 سفارش امروز"),
            Cell(Toman(Sales(1)), "💰 فروش امروز"),
            Cell(Toman(Sales(7)), "📅 فروش ۷ روز"),
            Cell(Toman(Sales(30)), "🗓 فروش ۳۰ روز"),
            Cell(Toman(paid.Sum(o => o.Total)), "💎 کل فروش"),
            StaffNav(),
        }), ct);
    }

    // What is waiting for staff, each opening its own page in the panel. Only the sections this person holds.
    private async Task ShowQueueAsync(string token, Screen at, AppUser staff, CancellationToken ct)
    {
        var b = _store.GetAdminBadgeCounts();
        var items = new (string Section, string Label, int Count, string Path)[]
        {
            ("orders-receipts", "🧾 رسید در انتظار تأیید", b.PendingOrders, "/admin/orders/receipts"),
            ("orders-fulfillment", "🛠 سفارش برای تحویل", b.PreparingOrders, "/admin/orders/fulfillment"),
            ("transactions", "💳 تراکنش در انتظار", b.PendingTransactions, "/admin/transactions"),
            ("kyc", "🪪 احراز هویت", b.PendingKyc, "/admin/kyc"),
            ("cards", "🏦 کارت بانکی", b.PendingCards, "/admin/cards"),
            ("seat-info", "📱 اطلاعات دستگاه", b.PendingSeatInfo, "/admin/seat-info"),
            ("tickets", "🎫 تیکت باز", b.OpenTickets, "/admin/tickets"),
            ("chat", "💬 گفتگوی خوانده‌نشده", b.UnreadChats, "/admin/chat"),
            ("comments", "🗨 نظر در انتظار", b.PendingComments, "/admin/comments"),
        };
        var rows = items.Where(i => Can(staff, i.Section))
            .Select(i => new[] { Link($"{i.Label}: {Fa(i.Count)}", Site + i.Path) })
            .Append(StaffNav());
        await ShowAsync(token, at,
            "🧾 کارهای منتظر\n\nهر مورد در پنل سایت باز می‌شود. رسیدها و سفارش‌ها در ربات‌های رسید و سفارش هم تأیید و تحویل می‌شوند.",
            Inline(rows), ct);
    }

    // ── Orders and customers ────────────────────────────────────────────────────────────────────────────────

    private async Task ShowStaffOrderAsync(string token, Screen at, AppUser staff, int orderId, CancellationToken ct)
    {
        if (_store.GetOrder(orderId) is not { } order)
        {
            await ShowAsync(token, at, "این سفارش یافت نشد.", Inline(new[] { StaffNav() }), ct);
            return;
        }
        var customer = _store.GetUser(order.UserId);
        // What happened to each account — never its content: that stays in the panel and the customer's chat.
        var lines = order.Units.OrderBy(u => u.Id).Take(15).Select(u =>
            $"▫️ {u.Name}{(string.IsNullOrWhiteSpace(u.Plan) ? "" : $" — {u.Plan}")}: "
            + (u.Delivered ? "✅ تحویل شده" : u.Rejected ? "❌ رد شده" : order.Status == OrderStatus.Cancelled ? "❌ لغو شده" : "⏳ منتظر تحویل"));
        var rows = new List<object[]>
        {
            Cell(order.Code, "🧾 کد سفارش"),
            Cell($"{StatusIcon(order.Status)} {StatusFa(order.Status)}", "📍 وضعیت"),
            Cell(Short(customer is null ? order.UserName : $"{DisplayName(customer)} ({customer.Username})", 30), "👤 مشتری"),
            Cell(JalaliDate.ToPersianDigits(order.Date), "📅 تاریخ"),
            Cell(Toman(order.Total), "💰 مبلغ"),
            Cell(Short(order.PaymentMethod, 24), "💳 پرداخت"),
            Cell(order.PlacedVia == PlacedViaBot ? "ربات تلگرام" : "سایت", "🛒 ثبت از"),
        };
        if (!string.IsNullOrWhiteSpace(order.DiscountCode)) rows.Add(Cell(order.DiscountCode, "🎟 کد تخفیف"));
        if (customer is not null && Can(staff, "users")) rows.Add(new[] { Btn("👤 کارت مشتری", $"adm:u:{customer.Id}") });
        rows.Add(new[] { Link("🌐 در پنل سایت", $"{Site}/admin/orders/status") });
        rows.Add(StaffNav());
        await ShowAsync(token, at, $"📄 سفارش {order.Code}\n\n{string.Join("\n", lines)}", Inline(rows), ct);
    }

    private async Task FindUsersAsync(string token, Screen at, string query, CancellationToken ct)
    {
        var term = query.Trim().TrimStart('@');
        var found = term.Length < 2
            ? new List<AppUser>()
            : _store.GetUsers().Where(u =>
                u.Username.Contains(term, StringComparison.OrdinalIgnoreCase) || u.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || u.Email.Contains(term, StringComparison.OrdinalIgnoreCase) || u.Phone.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (u.TelegramUsername ?? "").Equals(term, StringComparison.OrdinalIgnoreCase))
              .Take(10).ToList();
        if (found.Count == 1 && StaffFor(at.ChatId) is { } staff)
        {
            await ShowStaffUserAsync(token, at, staff, found[0].Id, ct);
            return;
        }
        var retry = new[] { Btn("🔎 جستجوی دوباره", "adm:us") };
        if (found.Count == 0)
        {
            await ShowAsync(token, at, "کاربری با این مشخصات پیدا نشد.", Inline(new[] { retry, StaffNav() }), ct);
            return;
        }
        var rows = found.Select(u => new[] { Btn($"{Short(DisplayName(u), 20)} · {u.Username}", $"adm:u:{u.Id}") }).ToList();
        rows.Add(retry);
        rows.Add(StaffNav());
        await ShowAsync(token, at, $"👤 {Fa(found.Count)} کاربر پیدا شد{(found.Count == 10 ? " (ده مورد اول)" : "")}:", Inline(rows), ct);
    }

    // Enough of an address to recognise it, not enough to copy it out of a chat.
    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0) return string.IsNullOrWhiteSpace(email) ? "ندارد" : "***";
        return email[..Math.Min(2, at)] + "***" + email[at..];
    }

    private static string MaskPhone(string phone)
    {
        var p = (phone ?? "").Trim();
        return p.Length < 7 ? (p.Length == 0 ? "ندارد" : "***") : $"{p[..4]}***{p[^3..]}";
    }

    private async Task ShowStaffUserAsync(string token, Screen at, AppUser staff, int userId, CancellationToken ct)
    {
        if (_store.GetUser(userId) is not { } u)
        {
            await ShowAsync(token, at, "این کاربر یافت نشد.", Inline(new[] { StaffNav() }), ct);
            return;
        }
        var role = u.Role switch { UserRole.Admin => "مدیر", UserRole.Support => "پشتیبان", _ => "مشتری" };
        var telegram = u.TelegramChatId is not null ? $"وصل{(string.IsNullOrWhiteSpace(u.TelegramUsername) ? "" : $" (@{u.TelegramUsername})")}"
            : u.TelegramGuestChatId is not null ? "خریدار بدون ثبت‌نام" : "وصل نیست";
        var rows = new List<object[]>
        {
            Cell(Short(DisplayName(u), 30), "👤 نام"),
            Cell(u.Username, "🆔 نام کاربری"),
            Cell(MaskEmail(u.Email), "📧 ایمیل"),
            Cell(MaskPhone(u.Phone), "📱 موبایل"),
            Cell(role, "🎖 نقش"),
            Cell(LevelFa(u.VerificationLevel), "🪪 احراز هویت"),
            Cell(Toman(u.Wallet), "💰 کیف پول"),
            Cell(Fa(u.Orders), "📦 سفارش‌ها"),
            Cell(Toman(u.TotalSpent), "💳 مجموع خرید"),
            Cell(string.IsNullOrWhiteSpace(u.JoinedAt) ? "-" : JalaliDate.ToPersianDigits(u.JoinedAt), "📅 عضویت"),
            Cell(u.Blocked ? "⛔ مسدود" : "✅ فعال", "📍 وضعیت"),
            Cell(telegram, "✈️ تلگرام"),
        };
        if (Can(staff, OrderSections)) rows.Add(new[] { Btn("📦 سفارش‌های این کاربر", $"adm:uo:{u.Id}") });
        rows.Add(new[] { Link("🌐 در پنل سایت", $"{Site}/admin/users") });
        rows.Add(StaffNav());
        await ShowAsync(token, at, $"👤 کارت کاربر «{DisplayName(u)}»", Inline(rows), ct);
    }

    private async Task ShowStaffUserOrdersAsync(string token, Screen at, int userId, CancellationToken ct)
    {
        var orders = _store.GetUserOrders(userId).OrderByDescending(o => o.Id).Take(10).ToList();
        var rows = orders.Select(o => new[] { Btn($"{StatusIcon(o.Status)} {o.Code} · {Short(o.Items.FirstOrDefault()?.Name, 18)} · {Toman(o.Total)}", $"adm:o:{o.Id}") })
            .Append(StaffNav($"adm:u:{userId}"));
        await ShowAsync(token, at, orders.Count == 0 ? "این کاربر سفارشی ندارد." : "📦 آخرین سفارش‌های این کاربر:", Inline(rows), ct);
    }

    private async Task ShowTopBuyersAsync(string token, Screen at, AppUser staff, CancellationToken ct)
    {
        var top = _store.GetUsers(role: UserRole.Customer).Where(u => u.TotalSpent > 0).OrderByDescending(u => u.TotalSpent).Take(10).ToList();
        string[] medals = { "🥇", "🥈", "🥉" };
        var users = Can(staff, "users");
        var rows = top.Select((u, i) => new[]
        {
            Btn($"{(i < 3 ? medals[i] : JalaliDate.ToPersianDigits($"{i + 1}."))} {Short(DisplayName(u), 18)} · {Toman(u.TotalSpent)}", users ? $"adm:u:{u.Id}" : "noop"),
        }).Append(StaffNav());
        await ShowAsync(token, at, top.Count == 0 ? "هنوز خریدی ثبت نشده است." : "🏆 برترین خریداران، بر اساس مجموع خرید:", Inline(rows), ct);
    }

    // ── Products ────────────────────────────────────────────────────────────────────────────────────────────

    private async Task ShowStaffCategoriesAsync(string token, Screen at, CancellationToken ct)
    {
        var counts = _store.GetProducts().GroupBy(p => p.CategoryId).ToDictionary(g => g.Key, g => g.Count());
        var buttons = _store.GetCategories().Where(c => counts.ContainsKey(c.Id)).OrderBy(c => c.SortOrder)
            .Select(c => Btn($"{(c.IsActive ? "" : "⛔ ")}{Short(c.Name, 18)} ({Fa(counts[c.Id])})", $"adm:pl:{c.Id}"));
        await ShowAsync(token, at, "🛍 مدیریت محصولات\n\nدسته‌بندی را انتخاب کنید:", Inline(Pairs(buttons).Append(StaffNav())), ct);
    }

    private async Task ShowStaffProductsAsync(string token, Screen at, int categoryId, CancellationToken ct)
    {
        var buttons = _store.GetProducts(categoryId)
            .Select(p => Btn($"{(p.IsActive ? "✅" : "⛔")}{(p.TelegramGuestSale ? "🔓" : "")} {Short(p.Name, 18)}", $"adm:pr:{p.Id}"));
        await ShowAsync(token, at, $"🛍 {_store.GetCategory(categoryId)?.Name}\n\n✅ فعال · ⛔ غیرفعال · 🔓 خرید بدون ثبت‌نام در ربات",
            Inline(Pairs(buttons).Append(StaffNav("adm:pc"))), ct);
    }

    private async Task ShowStaffProductAsync(string token, Screen at, int productId, string? notice, CancellationToken ct)
    {
        if (_store.GetProduct(productId) is not { } p)
        {
            await ShowAsync(token, at, "این محصول یافت نشد.", Inline(new[] { StaffNav("adm:pc") }), ct);
            return;
        }
        var plans = p.Plans.Where(x => x.IsActive).ToList();
        var rows = new List<object[]>
        {
            Cell(p.IsActive ? "✅ فعال" : "⛔ غیرفعال", "📍 وضعیت"),
            Cell(p.TelegramGuestSale ? "🔓 روشن" : "خاموش", "🔓 خرید بدون ثبت‌نام"),
            Cell(p.IsPanelProvisioned ? "خودکار از پنل" : Fa(p.Stock), "📦 موجودی"),
            Cell(plans.Count > 0 ? $"{(plans.Count > 1 ? "از " : "")}{Toman(plans.Min(x => x.FinalPrice))}" : Toman(p.FinalPrice), "💰 قیمت"),
            Cell(Fa(plans.Count), "🗂 پلن‌های فعال"),
            Cell($"سطح {JalaliDate.ToPersianDigits(p.RequiredLevel.ToString())}", "🪪 احراز لازم"),
            new[] { Btn(p.IsActive ? "⛔ غیرفعال کردن محصول" : "✅ فعال کردن محصول", $"adm:pa:{p.Id}") },
        };
        if (p.RequiredLevel <= 1)
            rows.Add(new[] { Btn(p.TelegramGuestSale ? "🔒 بستن خرید بدون ثبت‌نام" : "🔓 باز کردن خرید بدون ثبت‌نام", $"adm:pg:{p.Id}") });
        rows.Add(new[] { Btn("👁 نمای مشتری", $"shop:p:{p.Id}") });
        rows.Add(StaffNav($"adm:pl:{p.CategoryId}"));

        var lines = new List<string> { $"🛍 {p.Name}" };
        if (notice is not null) lines.Add("\n" + notice);
        // Switched on but not actually offered: say why, so nobody wonders why the bot doesn't show it.
        if (p.TelegramGuestSale && !GuestSellable(p))
            lines.Add("\n⚠️ خرید بدون ثبت‌نام روشن است ولی فعلاً عرضه نمی‌شود: "
                      + (!p.IsActive ? "محصول غیرفعال است." : !InStock(p) ? "موجودی ندارد." : "همه‌ی پلن‌های فعالش از خریدار اطلاعات می‌خواهند."));
        if (p.TelegramGuestSale && !SalesOn) lines.Add("\n⚠️ «خرید بدون ثبت‌نام در ربات» در تنظیمات ربات خاموش است.");
        lines.Add("\nتغییرات همین‌جا ذخیره و در لاگ ممیزی ثبت می‌شود. قیمت، پلن و موجودی را در پنل سایت ویرایش کنید.");
        await ShowAsync(token, at, string.Join("\n", lines), Inline(rows), ct);
    }

    private async Task ToggleProductAsync(string token, Screen at, AppUser staff, int productId, bool guestSale, CancellationToken ct)
    {
        if (_store.GetProduct(productId) is not { } p)
        {
            await ShowStaffProductAsync(token, at, productId, null, ct);
            return;
        }
        string? notice = null;
        if (guestSale)
        {
            // The panel's rule: a product that asks for identity documents is never sold without an account.
            if (p.RequiredLevel > 1) notice = "⚠️ محصولی که احراز هویت سطح ۲ می‌خواهد، بدون ثبت‌نام فروخته نمی‌شود.";
            else p.TelegramGuestSale = !p.TelegramGuestSale;
        }
        else p.IsActive = !p.IsActive;

        if (notice is null && _store.UpdateProduct(p))
        {
            Resolve<CatalogCache>()?.Invalidate();
            Audit(staff, AuditAction.Update, "products", p.Id.ToString(), guestSale
                ? $"خرید بدون ثبت‌نام «{p.Name}» {(p.TelegramGuestSale ? "باز" : "بسته")} شد"
                : $"محصول «{p.Name}» {(p.IsActive ? "فعال" : "غیرفعال")} شد");
            notice = "✅ ذخیره شد.";
        }
        await ShowStaffProductAsync(token, at, productId, notice, ct);
    }

    // ── Discount codes ──────────────────────────────────────────────────────────────────────────────────────

    private static string DiscountValue(DiscountCode d) => d.Type == DiscountType.Percent
        ? $"{Fa(d.Value)}٪{(d.MaxDiscount > 0 ? $" تا {Toman(d.MaxDiscount)}" : "")}"
        : Toman(d.Value);

    private async Task ShowDiscountsAsync(string token, Screen at, CancellationToken ct)
    {
        var codes = _store.GetDiscountCodes().OrderByDescending(d => d.IsActive).ThenByDescending(d => d.Id).Take(24).ToList();
        var rows = codes.Select(d => new[]
        {
            Btn($"{(d.IsActive ? "✅" : "⛔")} {d.Code} · {DiscountValue(d)} · {Fa(d.UsedCount)}/{(d.UsageLimit > 0 ? Fa(d.UsageLimit) : "∞")}", $"adm:d:{d.Id}"),
        }).Append(StaffNav());
        await ShowAsync(token, at, codes.Count == 0 ? "کد تخفیفی ساخته نشده است. کدها در پنل سایت ساخته می‌شوند." : "🎟 کدهای تخفیف\n\nبرای فعال یا غیرفعال کردن، روی کد بزنید:",
            Inline(rows), ct);
    }

    private async Task ShowDiscountAsync(string token, Screen at, int id, CancellationToken ct)
    {
        if (_store.GetDiscountCodes().FirstOrDefault(x => x.Id == id) is not { } d)
        {
            await ShowDiscountsAsync(token, at, ct);
            return;
        }
        await ShowAsync(token, at, $"🎟 کد تخفیف {d.Code}", Inline(new[]
        {
            Cell(d.Code, "🎟 کد"),
            Cell(DiscountValue(d), "💰 مقدار"),
            Cell($"{Fa(d.UsedCount)} از {(d.UsageLimit > 0 ? Fa(d.UsageLimit) : "نامحدود")}", "🔢 استفاده"),
            Cell(d.MinOrder > 0 ? Toman(d.MinOrder) : "ندارد", "🛒 حداقل خرید"),
            Cell(d.ExpiresAt is DateTime e ? JalaliDate.Format(e) : "ندارد", "⏳ انقضا"),
            Cell(d.ProductIds.Count > 0 ? $"{Fa(d.ProductIds.Count)} محصول" : "همه", "🛍 محصولات"),
            Cell(d.IsActive ? "✅ فعال" : "⛔ غیرفعال", "📍 وضعیت"),
            new[] { Btn(d.IsActive ? "⛔ غیرفعال کردن" : "✅ فعال کردن", $"adm:dt:{d.Id}") },
            StaffNav("adm:dc"),
        }), ct);
    }

    // ── A message to everyone ───────────────────────────────────────────────────────────────────────────────

    // Every customer the bot can write to: linked or bought here, not blocked, notices left on. Once per chat.
    private List<long> BroadcastChats() => _store.GetUsers(role: UserRole.Customer)
        .Where(u => !u.Blocked && u.TelegramNotify)
        .Select(u => u.TelegramChatId ?? u.TelegramGuestChatId).OfType<long>().Distinct().ToList();

    private async Task StartBroadcastAsync(string token, Screen at, AppUser staff, CancellationToken ct)
    {
        // Only the text this staff member previewed, and only once.
        if (!Sessions.TryRemove(at.ChatId, out var session) || session.Kind != "a-cast-ok" || session.Text is not { Length: > 0 } text)
        {
            await ShowAsync(token, at, "این پیش‌نمایش دیگر معتبر نیست. دوباره از «📣 پیام همگانی» شروع کنید.", Inline(new[] { StaffNav() }), ct);
            return;
        }
        if (Interlocked.CompareExchange(ref _broadcasting, 1, 0) != 0)
        {
            await ShowAsync(token, at, "یک پیام همگانی دیگر در حال ارسال است. بعد از پایان آن دوباره امتحان کنید.", Inline(new[] { StaffNav() }), ct);
            return;
        }
        var chats = BroadcastChats();
        Audit(staff, AuditAction.Create, "notifications", null, $"پیام همگانی تلگرام برای {chats.Count} نفر");
        await ShowAsync(token, at, $"⏳ ارسال برای {Fa(chats.Count)} نفر شروع شد. پایان کار همین‌جا خبر داده می‌شود.", Inline(new[] { StaffNav() }), ct);

        // Paced under Telegram's limit for one bot, and in the background: a few thousand chats take minutes.
        _ = Task.Run(async () =>
        {
            var sent = 0;
            try
            {
                foreach (var chat in chats)
                {
                    if (await ReplyAsync(token, chat, $"📣 {text}", CancellationToken.None)) sent++;
                    await Task.Delay(BroadcastPace);
                }
                _logger.LogInformation("Customer bot broadcast by {Staff}: {Sent} of {Total}", staff.Username, sent, chats.Count);
                await ReplyAsync(token, at.ChatId, $"✅ پیام همگانی برای {Fa(sent)} از {Fa(chats.Count)} نفر ارسال شد.", CancellationToken.None,
                    Inline(new[] { StaffNav() }));
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Customer bot broadcast failed after {Sent}", sent); }
            finally { Interlocked.Exchange(ref _broadcasting, 0); }
        });
    }

    // ── The bot's own switches ──────────────────────────────────────────────────────────────────────────────

    private async Task ShowBotSettingsAsync(string token, Screen at, string? notice, CancellationToken ct)
    {
        var s = _store.GetTelegramSettings();
        var users = _store.GetUsers(role: UserRole.Customer);
        await ShowAsync(token, at, "🤖 تنظیمات ربات" + (notice is null ? "" : $"\n\n{notice}"), Inline(new[]
        {
            Cell(s.CustomerBotShop ? "✅ روشن" : "خاموش", "🛒 فروشگاه داخل تلگرام"),
            Cell(s.CustomerBotSales ? "✅ روشن" : "خاموش", "🔓 خرید بدون ثبت‌نام"),
            Cell(Fa(users.Count(u => u.TelegramChatId is not null)), "🔗 حساب‌های وصل"),
            Cell(Fa(users.Count(u => u.TelegramGuestChatId is not null)), "🤖 خریداران ربات"),
            Cell(Fa(_store.GetProducts().Count(GuestSellable)), "🔓 محصولات بدون ثبت‌نام"),
            new[]
            {
                Btn(s.CustomerBotShop ? "🛒 خاموش کردن فروشگاه" : "🛒 روشن کردن فروشگاه", "adm:set:shop"),
                Btn(s.CustomerBotSales ? "🔒 بستن خرید بدون ثبت‌نام" : "🔓 باز کردن خرید بدون ثبت‌نام", "adm:set:sales"),
            },
            new[] { Link("🌐 همه‌ی تنظیمات در پنل", $"{Site}/admin/customer-bot") },
            StaffNav(),
        }), ct);
    }

    private async Task ToggleBotSettingAsync(string token, Screen at, AppUser staff, string which, CancellationToken ct)
    {
        var s = _store.GetTelegramSettings();
        string notice;
        if (which == "shop")
        {
            var on = !s.CustomerBotShop;
            if (on && ShopUrl is null)
            {
                await ShowBotSettingsAsync(token, at, "⚠️ آدرس سایت باید https باشد؛ تلگرام فروشگاه را فقط روی https باز می‌کند.", ct);
                return;
            }
            _store.SetCustomerBot(s.CustomerBotEnabled, s.CustomerBotPublic, null, null, shop: on);
            var (ok, error) = await SyncMenuButtonAsync(ct);
            notice = ok ? "✅ ذخیره شد." : $"✅ ذخیره شد، ولی {error}";
            Audit(staff, AuditAction.Update, "customer-bot", null, $"فروشگاه داخل تلگرام {(on ? "روشن" : "خاموش")} شد");
        }
        else
        {
            _store.SetCustomerBot(s.CustomerBotEnabled, s.CustomerBotPublic, null, null, sales: !s.CustomerBotSales);
            notice = "✅ ذخیره شد.";
            Audit(staff, AuditAction.Update, "customer-bot", null, $"خرید بدون ثبت‌نام در ربات {(!s.CustomerBotSales ? "باز" : "بسته")} شد");
        }
        await ShowBotSettingsAsync(token, at, notice, ct);
    }
}

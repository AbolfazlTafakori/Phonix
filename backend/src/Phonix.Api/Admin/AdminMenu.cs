using Phonix.Api.Models;

namespace Phonix.Api.Admin;

// The admin sidebar is STATIC structure — it ships with the code, not the data store, so it lives here as
// typed config rather than in the database. Only the badge counts are dynamic (see StoreData.GetAdminBadgeCounts).
// Visibility is by role RANK, reusing the existing UserRole (Customer=0, Support=1, Admin=2) so the menu and
// the [Authorize] guards on the real endpoints share one source of truth.
public static class RoleRank
{
    public static bool IsAtLeast(this UserRole role, UserRole min) => (int)role >= (int)min;
}

// Which "needs attention" counter (if any) feeds an item's badge.
public enum AdminBadge
{
    None,
    PendingOrders,
    PreparingOrders,
    PendingTransactions,
    OpenTickets,
    PendingKyc,
    PendingCards,
    PendingComments,
    UnreadChats,
    PendingSeatInfo,
}

public sealed record AdminMenuItem(
    string Key,
    string Title,
    string Icon,
    string Route,
    UserRole MinRole = UserRole.Support,
    AdminBadge Badge = AdminBadge.None,
    bool ComingSoon = false, // FUTURE FEATURE → rendered visible-but-disabled with a "به‌زودی" tag.
    bool OwnerOnly = false); // Only the owner account sees it, even above the item's MinRole (see OwnerAccount).

public sealed record AdminMenuGroup(
    string Key,
    string Title,
    UserRole MinRole,
    IReadOnlyList<AdminMenuItem> Items);

// One assignable panel section a limited (Support) staff member can be granted.
public sealed record AdminPermissionInfo(string Key, string Title, string Group);

public static class AdminMenu
{
    // Declaration order == display order: high-frequency daily ops first, low-frequency system settings last.
    // NOTE: declared before AssignableKeys so the static initializer that reads it (below) sees a populated list —
    // static fields initialize in declaration order, and AssignableKeys depends on this one.
    public static readonly IReadOnlyList<AdminMenuGroup> Groups = new AdminMenuGroup[]
    {
        new("ops", "عملیات اصلی", UserRole.Support, new AdminMenuItem[]
        {
            new("dashboard",    "داشبورد",             "dashboard", "/admin"),
            new("orders-receipts",    "تأیید رسید واریز",  "wallet",   "/admin/orders/receipts",    Badge: AdminBadge.PendingOrders),
            new("orders-fulfillment", "تحویل سفارش",       "cart",     "/admin/orders/fulfillment", Badge: AdminBadge.PreparingOrders),
            new("orders-status",      "وضعیت سفارشات",     "activity", "/admin/orders/status"),
            new("transactions", "تراکنش‌ها و کیف پول",  "wallet",    "/admin/transactions", Badge: AdminBadge.PendingTransactions),
            new("tickets",      "تیکت‌های پشتیبانی",    "ticket",    "/admin/tickets",      Badge: AdminBadge.OpenTickets),
            new("chat",         "گفتگوی زنده",          "chat",      "/admin/chat",         Badge: AdminBadge.UnreadChats),
            // No badge: the unread count lives on the IMAP server, and fetching it here would put a network
            // round-trip (and a hang, when the mail host is down) in front of every admin page load.
            new("mailbox",      "صندوق ایمیل",          "mail",      "/admin/mailbox"),
        }),
        new("users", "کاربران و احراز هویت", UserRole.Support, new AdminMenuItem[]
        {
            new("kyc",           "احراز هویت (KYC)",  "shield", "/admin/kyc",           Badge: AdminBadge.PendingKyc),
            new("cards",         "تأیید کارت بانکی",   "card",   "/admin/cards",         Badge: AdminBadge.PendingCards),
            new("users",         "مدیریت کاربران",     "users",  "/admin/users"),
            new("notifications", "اعلان‌ها و پیام‌ها",  "bell",   "/admin/notifications"),
        }),
        new("catalog", "محصولات و انبار", UserRole.Support, new AdminMenuItem[]
        {
            new("products",   "محصولات و پلن‌ها",           "box",     "/admin/products"),
            new("plan-types", "نوع سرویس (پلن‌ها)",          "tag",     "/admin/plan-types"),
            new("stock-pool", "انبار مجازی / استخر اکانت",  "grid",    "/admin/stock"),
            new("seat-info",  "اطلاعات کاربران اکانت‌ها",   "users",   "/admin/seat-info", Badge: AdminBadge.PendingSeatInfo),
            new("categories", "دسته‌بندی‌ها",               "columns", "/admin/categories"),
        }),
        new("finance", "مالی، بازاریابی و تحلیل", UserRole.Support, new AdminMenuItem[]
        {
            new("invoices",   "مدیریت فاکتورها",      "news",     "/admin/invoices"),
            new("discounts",  "کدهای تخفیف",         "tag",      "/admin/discounts"),
            new("affiliates", "همکاری در فروش",       "star",     "/admin/affiliates", ComingSoon: true),
            new("payments",   "روش‌های پرداخت",       "card",     "/admin/payments"),
            new("pricing",    "تنظیمات قیمت‌گذاری",   "activity", "/admin/pricing"),
            new("reports",    "گزارش‌ها و تحلیل",     "chart",    "/admin/reports"),
        }),
        new("cms", "محتوا و انجمن", UserRole.Support, new AdminMenuItem[]
        {
            new("comments", "نظرات و دیدگاه‌ها",      "chat",    "/admin/comments", Badge: AdminBadge.PendingComments),
            new("layout",   "مدیریت چیدمان و محتوا",  "layout",  "/admin/home"),
            new("banners",  "اسلایدر اصلی",           "image",   "/admin/banners"),
            new("header",   "هدر و منو",              "layout",  "/admin/header"),
            new("footer",   "فوتر",                   "columns", "/admin/footer"),
            new("blog",     "وبلاگ و مقالات",         "news",    "/admin/blog"),
            new("pages",    "صفحات ثابت و قوانین",    "columns", "/admin/rules"),
        }),
        // Personal account security — available to EVERY staff member regardless of level or granted sections,
        // so any admin (full or limited) can protect their own login with 2FA. Not an assignable permission.
        new("account", "حساب کاربری من", UserRole.Support, new AdminMenuItem[]
        {
            new("security", "امنیت و ورود دو‌مرحله‌ای", "shield", "/admin/settings/2fa"),
        }),
        // ── Strict Admin-only: DevOps & System Settings. Support never receives this group. ──
        new("system", "دواپس و تنظیمات سیستم", UserRole.Admin, new AdminMenuItem[]
        {
            new("staff",    "مدیریت کارکنان و نقش‌ها",     "shield",   "/admin/staff",          UserRole.Admin),
            new("audit",    "لاگ‌های ممیزی سیستم",         "search",   "/admin/audit-logs",     UserRole.Admin),
            new("logs",     "لاگ‌های فایل سیستم",          "activity", "/admin/logs",           UserRole.Admin),
            new("email-log", "ایمیل‌های ارسال‌شده",         "bell",     "/admin/email-log",      UserRole.Admin),
            new("backup",   "پشتیبان‌گیری و ربات تلگرام",  "disk",     "/admin/backup",         UserRole.Admin),
            new("receipt-bot", "تأیید رسید خودکار و ربات تلگرام", "wallet", "/admin/receipt-bot", UserRole.Admin),
            new("order-bot",   "ارسال سفارشات و ربات تلگرام",     "cart",   "/admin/order-bot",   UserRole.Admin),
            new("email",    "تنظیمات ایمیل و پیامک",       "bell",     "/admin/settings/email", UserRole.Admin),
            new("settings", "تنظیمات عمومی و پیشرفته",     "settings", "/admin/settings",       UserRole.Admin),
            new("v2ray",    "تنظیمات پنل v2ray",           "cpu",      "/admin/v2ray",          UserRole.Admin, OwnerOnly: true),
            new("v2ray-plans", "پلن‌های v2ray",            "box",      "/admin/v2ray/plans",    UserRole.Admin, OwnerOnly: true),
            new("cluster",  "مدیریت خوشه (HA)",            "activity", "/admin/cluster",        UserRole.Admin),
        }),
    };

    // Sections every staff member can always reach, so they're neither permission-gated nor assignable:
    // the dashboard landing page and personal 2FA security.
    public static readonly HashSet<string> AlwaysAvailableKeys =
        new(new[] { "dashboard", "security" }, StringComparer.Ordinal);

    // The sections an Admin may grant to a limited staff account: every real (non-"coming soon") item in a
    // Support-reachable group, except the always-available dashboard. The Admin-only system group is never
    // assignable. Drives both the staff-management checklist and server-side permission validation.
    public static IReadOnlyList<AdminPermissionInfo> AssignablePermissions() =>
        Groups
            .Where(g => g.MinRole == UserRole.Support)
            .SelectMany(g => g.Items
                .Where(i => !i.ComingSoon && i.MinRole == UserRole.Support && !AlwaysAvailableKeys.Contains(i.Key))
                .Select(i => new AdminPermissionInfo(i.Key, i.Title, g.Title)))
            .ToList();

    public static readonly HashSet<string> AssignableKeys =
        AssignablePermissions().Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
}

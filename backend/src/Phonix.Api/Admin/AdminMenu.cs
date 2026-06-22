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
    PendingTransactions,
    OpenTickets,
    PendingKyc,
    PendingCards,
    PendingComments,
}

public sealed record AdminMenuItem(
    string Key,
    string Title,
    string Icon,
    string Route,
    UserRole MinRole = UserRole.Support,
    AdminBadge Badge = AdminBadge.None,
    bool ComingSoon = false); // FUTURE FEATURE → rendered visible-but-disabled with a "به‌زودی" tag.

public sealed record AdminMenuGroup(
    string Key,
    string Title,
    UserRole MinRole,
    IReadOnlyList<AdminMenuItem> Items);

public static class AdminMenu
{
    // Declaration order == display order: high-frequency daily ops first, low-frequency system settings last.
    public static readonly IReadOnlyList<AdminMenuGroup> Groups = new AdminMenuGroup[]
    {
        new("ops", "عملیات اصلی", UserRole.Support, new AdminMenuItem[]
        {
            new("dashboard",    "داشبورد",             "dashboard", "/admin"),
            new("orders",       "مدیریت سفارش‌ها",      "cart",      "/admin/orders",       Badge: AdminBadge.PendingOrders),
            new("transactions", "تراکنش‌ها و کیف پول",  "wallet",    "/admin/transactions", Badge: AdminBadge.PendingTransactions),
            new("tickets",      "تیکت‌های پشتیبانی",    "ticket",    "/admin/tickets",      Badge: AdminBadge.OpenTickets),
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
            new("stock-pool", "انبار مجازی / استخر اکانت",  "grid",    "/admin/stock", ComingSoon: true),
            new("categories", "دسته‌بندی‌ها",               "columns", "/admin/categories"),
        }),
        new("finance", "مالی، بازاریابی و تحلیل", UserRole.Support, new AdminMenuItem[]
        {
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
            new("blog",     "وبلاگ و مقالات",         "news",    "/admin/blog"),
            new("pages",    "صفحات ثابت و قوانین",    "columns", "/admin/rules"),
        }),
        // ── Strict Admin-only: DevOps & System Settings. Support never receives this group. ──
        new("system", "دواپس و تنظیمات سیستم", UserRole.Admin, new AdminMenuItem[]
        {
            new("staff",    "مدیریت کارکنان و نقش‌ها",     "shield",   "/admin/staff",          UserRole.Admin, ComingSoon: true),
            new("audit",    "لاگ‌های ممیزی سیستم",         "search",   "/admin/audit",          UserRole.Admin, ComingSoon: true),
            new("backup",   "پشتیبان‌گیری و ربات تلگرام",  "disk",     "/admin/backup",         UserRole.Admin),
            new("email",    "تنظیمات ایمیل و پیامک",       "bell",     "/admin/settings/email", UserRole.Admin),
            new("settings", "تنظیمات عمومی و پیشرفته",     "settings", "/admin/settings",       UserRole.Admin),
        }),
    };
}

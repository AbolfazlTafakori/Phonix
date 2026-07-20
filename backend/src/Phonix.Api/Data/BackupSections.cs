namespace Phonix.Api.Data;

public sealed record BackupLogEntry(string Section, string Target, bool Ok, string Error, DateTime AtUtc);

public enum BackupSection
{
    Catalog,   // categories, products, plans, plan types, discount codes
    Content,   // hero slides, home categories, showcase, blog, site content, display settings
    Users,     // users, bank-card records, KYC records, referral earnings, favorites
    Commerce,  // orders, transactions, payment methods, payment settings
    Support,   // tickets, comments, notifications, live-chat conversations
    System,    // email, telegram and advanced settings
}

public static class BackupSections
{
    // The domains a backup can be taken and restored in, with the label the admin panel shows for each.
    public static readonly IReadOnlyList<(BackupSection Section, string Label)> All = new[]
    {
        (BackupSection.Catalog, "محصولات و کاتالوگ"),
        (BackupSection.Content, "ظاهر و محتوای سایت"),
        (BackupSection.Users, "کاربران و هویت"),
        (BackupSection.Commerce, "مالی و سفارش‌ها"),
        (BackupSection.Support, "پشتیبانی و ارتباطات"),
        (BackupSection.System, "تنظیمات سیستم"),
    };
}

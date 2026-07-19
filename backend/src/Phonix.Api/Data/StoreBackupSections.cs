using System.Text.Json;

namespace Phonix.Api.Data;

// Per-section backup. The store is split into independent domains so each can be exported, sent to Telegram,
// and restored on its own (each file stays small and under Telegram's size limit). A partial backup carries
// only its section's collections; restoring it replaces ONLY that section and leaves everything else intact,
// then ID counters are recomputed from the live data so nothing ever collides.
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

public partial class StoreData
{
    // Recent backup activity shown in the admin panel (in-memory ring, newest first). Kept small.
    private readonly List<BackupLogEntry> _backupLog = new();

    public void RecordBackup(string section, string target, bool ok, string error)
    {
        lock (_backupLog)
        {
            _backupLog.Insert(0, new BackupLogEntry(section, target, ok, error, DateTime.UtcNow));
            if (_backupLog.Count > 100) _backupLog.RemoveRange(100, _backupLog.Count - 100);
        }
    }

    public IReadOnlyList<BackupLogEntry> GetBackupLog()
    {
        lock (_backupLog) return _backupLog.ToList();
    }

    public static readonly IReadOnlyList<(BackupSection Section, string Label)> BackupSections = new[]
    {
        (BackupSection.Catalog, "محصولات و کاتالوگ"),
        (BackupSection.Content, "ظاهر و محتوای سایت"),
        (BackupSection.Users, "کاربران و هویت"),
        (BackupSection.Commerce, "مالی و سفارش‌ها"),
        (BackupSection.Support, "پشتیبانی و ارتباطات"),
        (BackupSection.System, "تنظیمات سیستم"),
    };

    // Serializes one section as a partial snapshot. Done inside _gate, so a shallow ToList() is safe.
    public string SerializeSection(BackupSection section)
    {
        lock (_gate)
        {
            var s = new StoreSnapshot { Section = section.ToString() };
            switch (section)
            {
                case BackupSection.Catalog:
                    s.Categories = _categories.ToList();
                    s.Products = _products.ToList();
                    s.StockItems = _stockItems.ToList();
                    s.StockAccounts = _stockAccounts.ToList();
                    s.Plans = _plans.ToList();
                    s.PlanTypes = _planTypes.ToList();
                    s.DiscountCodes = _discountCodes.ToList();
                    break;
                case BackupSection.Content:
                    s.HeroSlides = _heroSlides.ToList();
                    s.HomeCategories = _homeCategories.ToList();
                    s.Showcase = _showcase.ToList();
                    s.BlogPosts = _blogPosts.ToList();
                    s.SiteContent = _siteContent;
                    s.Settings = _settings;
                    break;
                case BackupSection.Users:
                    s.Users = _users.ToList();
                    s.Cards = _cards.ToList();
                    s.Kyc = _kyc.ToList();
                    s.ReferralEarnings = _referralEarnings.ToList();
                    s.Favorites = _favorites.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
                    break;
                case BackupSection.Commerce:
                    s.Orders = _orders.ToList();
                    s.Transactions = _transactions.ToList();
                    s.PaymentMethods = _paymentMethods.ToList();
                    s.PaymentSettings = _paymentSettings;
                    s.SeatSubmissions = _seatSubmissions.ToList();
                    break;
                case BackupSection.Support:
                    s.Tickets = _tickets.ToList();
                    s.Comments = _comments.ToList();
                    s.Notifications = _notifications.ToList();
                    s.Conversations = _conversations.ToList();
                    break;
                case BackupSection.System:
                    s.EmailSettings = _emailSettings;
                    s.TelegramSettings = _telegramSettings;
                    s.AdvancedSettings = _advancedSettings;
                    break;
            }
            return JsonSerializer.Serialize(s, PersistOptions);
        }
    }

    // Replaces ONLY the given section's collections from a partial snapshot, then recomputes ID counters and
    // persists. Other sections are untouched.
    public void RestoreSection(BackupSection section, StoreSnapshot s)
    {
        lock (_gate)
        {
            switch (section)
            {
                case BackupSection.Catalog:
                    Replace(_categories, s.Categories);
                    Replace(_products, s.Products);
                    Replace(_stockItems, s.StockItems);
                    Replace(_stockAccounts, s.StockAccounts);
                    Replace(_plans, s.Plans);
                    Replace(_planTypes, s.PlanTypes);
                    Replace(_discountCodes, s.DiscountCodes);
                    break;
                case BackupSection.Content:
                    Replace(_heroSlides, s.HeroSlides);
                    Replace(_homeCategories, s.HomeCategories);
                    Replace(_showcase, s.Showcase);
                    Replace(_blogPosts, s.BlogPosts);
                    _siteContent = s.SiteContent;
                    _settings = s.Settings;
                    break;
                case BackupSection.Users:
                    Replace(_users, s.Users);
                    Replace(_cards, s.Cards);
                    Replace(_kyc, s.Kyc);
                    Replace(_referralEarnings, s.ReferralEarnings);
                    _favorites.Clear();
                    foreach (var kv in s.Favorites) _favorites[kv.Key] = new HashSet<int>(kv.Value);
                    break;
                case BackupSection.Commerce:
                    Replace(_orders, s.Orders);
                    Replace(_transactions, s.Transactions);
                    Replace(_paymentMethods, s.PaymentMethods);
                    _paymentSettings = s.PaymentSettings;
                    Replace(_seatSubmissions, s.SeatSubmissions);
                    break;
                case BackupSection.Support:
                    Replace(_tickets, s.Tickets);
                    Replace(_comments, s.Comments);
                    Replace(_notifications, s.Notifications);
                    Replace(_conversations, s.Conversations);
                    break;
                case BackupSection.System:
                    _emailSettings = s.EmailSettings;
                    _telegramSettings = s.TelegramSettings;
                    _advancedSettings = s.AdvancedSettings;
                    break;
            }
            RecomputeSeqFromData();
            RebuildCatalogView();
            RebuildUserIndex(); // a Users-section restore replaced _users — keep the lookup indexes in sync
        }
        Save();
    }

    // After a partial restore, push every ID counter to at least the max id present, so future inserts never
    // reuse an existing id (keeps things safe even when sections are restored independently).
    private void RecomputeSeqFromData()
    {
        static int Max(IEnumerable<int> ids, int current)
        {
            var m = current;
            foreach (var id in ids) if (id > m) m = id;
            return m;
        }

        _categorySeq = Max(_categories.Select(x => x.Id), _categorySeq);
        _productSeq = Max(_products.Select(x => x.Id), _productSeq);
        _userSeq = Max(_users.Select(x => x.Id), _userSeq);
        _planSeq = Max(_plans.Select(x => x.Id), _planSeq);
        _heroSeq = Max(_heroSlides.Select(x => x.Id), _heroSeq);
        _homeCatSeq = Max(_homeCategories.Select(x => x.Id), _homeCatSeq);
        _showcaseSeq = Max(_showcase.Select(x => x.Id), _showcaseSeq);
        _blogSeq = Max(_blogPosts.Select(x => x.Id), _blogSeq);
        _commentSeq = Max(_comments.Select(x => x.Id), _commentSeq);
        _paymentSeq = Max(_paymentMethods.Select(x => x.Id), _paymentSeq);
        _txSeq = Max(_transactions.Select(x => x.Id), _txSeq);
        _cardSeq = Max(_cards.Select(x => x.Id), _cardSeq);
        _kycSeq = Max(_kyc.Select(x => x.Id), _kycSeq);
        _orderSeq = Max(_orders.Select(x => x.Id), _orderSeq);
        _ticketSeq = Max(_tickets.Select(x => x.Id), _ticketSeq);
        _notificationSeq = Max(_notifications.Select(x => x.Id), _notificationSeq);
        _discountSeq = Max(_discountCodes.Select(x => x.Id), _discountSeq);
        _conversationSeq = Max(_conversations.Select(x => x.Id), _conversationSeq);
        foreach (var c in _conversations)
            _chatMessageSeq = Max(c.Messages.Select(m => m.Id), _chatMessageSeq);
    }
}

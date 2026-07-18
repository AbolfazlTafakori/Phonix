using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Microsoft.Data.Sqlite;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Data;

// Backup: snapshot bridge, backup log, per-section export/restore, VACUUM INTO backup.
// Partial of SqliteDataStore -- split by domain the same way the JSON StoreData is (StoreOrders.cs etc.).
public sealed partial class SqliteDataStore
{
    // ── Snapshot / backup bridge ────────────────────────────────────────────────────────────────────────
    // Produces / consumes the SAME StoreSnapshot shape the JSON store uses, so a backup taken from either
    // implementation restores into the other (and the Telegram-bot/admin backup flow keeps working unchanged).
    // NOTE: this currently bridges the domains already migrated to SQLite (users, products, orders,
    // transactions, cards, discounts, payment methods, referral earnings, notifications, pricing/payment
    // settings). The remaining lists fill in as their tables are added in later chunks.

    private static readonly JsonSerializerOptions SnapshotJson = new()
    {
        WriteIndented = true, // matches the JSON store's on-disk/backup format
        Converters = { new JsonStringEnumConverter() },
    };

    private static int MaxId(SqliteConnection conn, string table) =>
        conn.ExecuteScalar<int?>($"SELECT MAX(Id) FROM {table}") ?? 0; // table is a constant literal, not user input


    // ── Backup log (in-memory ring, like the JSON store) ────────────────────────────────────────────────
    private readonly List<BackupLogEntry> _backupLog = new();
    public void RecordBackup(string section, string target, bool ok, string error)
    {
        lock (_backupLog)
        {
            _backupLog.Insert(0, new BackupLogEntry(section, target, ok, error, DateTime.UtcNow));
            if (_backupLog.Count > 100) _backupLog.RemoveRange(100, _backupLog.Count - 100);
        }
    }
    public IReadOnlyList<BackupLogEntry> GetBackupLog() { lock (_backupLog) return _backupLog.ToList(); }

    // Writes are durable on COMMIT (WAL); a passive checkpoint keeps the WAL from growing unbounded.
    public void Save() { using var conn = OpenConnection(); conn.Execute("PRAGMA wal_checkpoint(PASSIVE);"); }

    public StoreSnapshot CaptureSnapshot()
    {
        using var conn = OpenConnection();
        List<T> All<T>(string table) =>
            conn.Query<string>($"SELECT DataJson FROM {table} ORDER BY Id").Select(j => Deserialize<T>(j)!).ToList();

        var snap = new StoreSnapshot
        {
            Categories = All<Category>("Categories"),
            Products = All<Product>("Products"),
            StockItems = All<StockItem>("StockItems"),
            StockAccounts = All<StockAccount>("StockAccounts"),
            Users = All<AppUser>("Users"),
            Plans = All<SubscriptionPlan>("Plans"),
            HeroSlides = All<HeroSlide>("HeroSlides"),
            HomeCategories = All<HomeCategory>("HomeCategories"),
            Showcase = All<Showcase>("Showcase"),
            BlogPosts = All<BlogPost>("BlogPosts"),
            Comments = All<Comment>("Comments"),
            PaymentMethods = All<PaymentMethod>("PaymentMethods"),
            Transactions = All<Transaction>("Transactions"),
            Cards = All<BankCard>("Cards"),
            Kyc = All<KycRequest>("Kyc"),
            Orders = All<Order>("Orders"),
            Tickets = All<Ticket>("Tickets"),
            Notifications = All<Notification>("Notifications"),
            Conversations = All<ChatConversation>("Conversations"),
            // ReferralEarnings has no Id column — order by rowid instead.
            ReferralEarnings = conn.Query<string>("SELECT DataJson FROM ReferralEarnings ORDER BY rowid")
                .Select(j => Deserialize<ReferralEarning>(j)!).ToList(),
            DiscountCodes = All<DiscountCode>("DiscountCodes"),
            PlanTypes = GetSingleton<List<string>>(PlanTypesKey),
            Favorites = GetSingleton<Dictionary<int, List<int>>>(FavoritesKey),
            Settings = GetSettings(),
            SiteContent = GetSiteContent(),
            AdvancedSettings = GetAdvancedSettings(),
            PaymentSettings = GetPaymentSettings(),
            EmailSettings = GetEmailSettings(),
            TelegramSettings = GetTelegramSettings(),
            Seq = new StoreSnapshot.SeqState
            {
                Category = MaxId(conn, "Categories"),
                Product = MaxId(conn, "Products"),
                Stock = MaxId(conn, "StockItems"),
                StockAccount = MaxId(conn, "StockAccounts"),
                User = MaxId(conn, "Users"),
                Plan = MaxId(conn, "Plans"),
                Hero = MaxId(conn, "HeroSlides"),
                HomeCat = MaxId(conn, "HomeCategories"),
                Showcase = MaxId(conn, "Showcase"),
                Blog = MaxId(conn, "BlogPosts"),
                Comment = MaxId(conn, "Comments"),
                Payment = MaxId(conn, "PaymentMethods"),
                Tx = MaxId(conn, "Transactions"),
                Card = MaxId(conn, "Cards"),
                Kyc = MaxId(conn, "Kyc"),
                Order = MaxId(conn, "Orders"),
                Ticket = MaxId(conn, "Tickets"),
                Notification = MaxId(conn, "Notifications"),
                Discount = MaxId(conn, "DiscountCodes"),
                Conversation = MaxId(conn, "Conversations"),
                ChatMessage = conn.ExecuteScalar<int?>("SELECT Value FROM Counters WHERE Name='chatMessage'") ?? 0,
            },
        };
        return snap;
    }

    public string SerializeSnapshot() => JsonSerializer.Serialize(CaptureSnapshot(), SnapshotJson);

    public StoreSnapshot? DeserializeSnapshot(string json) => JsonSerializer.Deserialize<StoreSnapshot>(json, SnapshotJson);

    // Replaces the durable contents with a snapshot — atomically (one IMMEDIATE transaction): the whole import
    // commits or nothing does, so a failed/partial restore can never leave a half-loaded database. When
    // clustering is on, a restore invalidates whatever sync cursor the peer thinks it's at (the data it was
    // tracking no longer exists in this shape), so it forces a clean re-sync afterward — see
    // ReseedOutboxFromCurrentState. That runs as its own transaction, sequentially after this one commits,
    // since WriteTx/WriteTxNoFk can't nest.
    public void LoadSnapshot(StoreSnapshot s)
    {
        LoadSnapshotTx(s);
        if (_clusterEnabled) ReseedOutboxFromCurrentState();
    }

    private void LoadSnapshotTx(StoreSnapshot s) =>
        WriteTxNoFk<object?>((conn, tx) =>
        {
            conn.Execute(@"
DELETE FROM Users; DELETE FROM Products; DELETE FROM StockItems; DELETE FROM StockAccounts; DELETE FROM Orders; DELETE FROM Transactions;
DELETE FROM Cards; DELETE FROM DiscountCodes; DELETE FROM PaymentMethods;
DELETE FROM ReferralEarnings; DELETE FROM Notifications; DELETE FROM Categories;
DELETE FROM Plans; DELETE FROM HeroSlides; DELETE FROM HomeCategories; DELETE FROM Showcase;
DELETE FROM BlogPosts; DELETE FROM Comments; DELETE FROM Kyc; DELETE FROM Tickets;
DELETE FROM Conversations; DELETE FROM Counters;", transaction: tx);

            // hybrid-column tables use their typed upserts/inserts (Id preserved)
            foreach (var u in s.Users) UpsertUser(conn, tx, u);
            foreach (var p in s.Products) UpsertProduct(conn, tx, p);
            foreach (var si in s.StockItems)
                conn.Execute("INSERT INTO StockItems (Id, ProductId, Status, DataJson) VALUES (@Id,@ProductId,@Status,@DataJson)",
                    new { si.Id, si.ProductId, Status = (int)si.Status, DataJson = Serialize(si) }, tx);
            foreach (var sa in s.StockAccounts)
                conn.Execute("INSERT INTO StockAccounts (Id, ProductId, DataJson) VALUES (@Id,@ProductId,@DataJson)",
                    new { sa.Id, sa.ProductId, DataJson = Serialize(sa) }, tx);
            foreach (var o in s.Orders) UpsertOrder(conn, tx, o);
            foreach (var t in s.Transactions)
                conn.Execute("INSERT INTO Transactions (Id, UserId, Status, Date, DataJson) VALUES (@Id,@UserId,@Status,@Date,@DataJson)",
                    new { t.Id, t.UserId, Status = (int)t.Status, t.Date, DataJson = Serialize(t) }, tx);
            foreach (var c in s.Cards)
                conn.Execute("INSERT INTO Cards (Id, UserId, Status, DataJson) VALUES (@Id,@UserId,@Status,@DataJson)",
                    new { c.Id, c.UserId, Status = (int)c.Status, DataJson = Serialize(c) }, tx);
            foreach (var d in s.DiscountCodes)
                conn.Execute("INSERT INTO DiscountCodes (Id, Code, DataJson) VALUES (@Id,@Code,@DataJson)",
                    new { d.Id, d.Code, DataJson = Serialize(d) }, tx);
            foreach (var m in s.PaymentMethods)
                conn.Execute("INSERT INTO PaymentMethods (Id, DataJson) VALUES (@Id,@DataJson)", new { m.Id, DataJson = Serialize(m) }, tx);
            foreach (var r in s.ReferralEarnings)
                conn.Execute("INSERT INTO ReferralEarnings (ReferrerId, DataJson) VALUES (@ReferrerId,@DataJson)", new { r.ReferrerId, DataJson = Serialize(r) }, tx);
            foreach (var n in s.Notifications)
                conn.Execute("INSERT INTO Notifications (Id, UserId, DataJson) VALUES (@Id,@UserId,@DataJson)", new { n.Id, n.UserId, DataJson = Serialize(n) }, tx);

            // simple id-keyed JSON tables
            void Ins<T>(string table, int id, T obj) => conn.Execute($"INSERT INTO {table} (Id, DataJson) VALUES (@id,@d)", new { id, d = Serialize(obj) }, tx);
            foreach (var c in s.Categories) Ins("Categories", c.Id, c);
            foreach (var p in s.Plans) Ins("Plans", p.Id, p);
            foreach (var h in s.HeroSlides) Ins("HeroSlides", h.Id, h);
            foreach (var h in s.HomeCategories) Ins("HomeCategories", h.Id, h);
            foreach (var sh in s.Showcase) Ins("Showcase", sh.Id, sh);
            foreach (var b in s.BlogPosts) Ins("BlogPosts", b.Id, b);
            foreach (var cm in s.Comments) Ins("Comments", cm.Id, cm);
            foreach (var k in s.Kyc) Ins("Kyc", k.Id, k);
            foreach (var tk in s.Tickets) Ins("Tickets", tk.Id, tk);
            foreach (var cv in s.Conversations) Ins("Conversations", cv.Id, cv);

            // restore the global chat-message counter so new messages keep unique ids
            conn.Execute("INSERT INTO Counters (Name, Value) VALUES ('chatMessage', @v) ON CONFLICT(Name) DO UPDATE SET Value=@v",
                new { v = s.Seq.ChatMessage }, tx);

            // singletons / settings
            WriteSingleton(conn, tx, PricingKey, s.Settings);
            WriteSingleton(conn, tx, PaymentKey, s.PaymentSettings);
            WriteSingleton(conn, tx, SiteContentKey, s.SiteContent);
            WriteSingleton(conn, tx, AdvancedKey, s.AdvancedSettings);
            WriteSingleton(conn, tx, EmailKey, s.EmailSettings);
            WriteSingleton(conn, tx, TelegramKey, s.TelegramSettings);
            WriteSingleton(conn, tx, PlanTypesKey, s.PlanTypes);
            WriteSingleton(conn, tx, FavoritesKey, s.Favorites);
            return null;
        });

    private static void InsRow<T>(SqliteConnection conn, SqliteTransaction tx, string table, int id, T obj) =>
        conn.Execute($"INSERT INTO {table} (Id, DataJson) VALUES (@id, @d)", new { id, d = Serialize(obj) }, tx);

    // ── Per-section backup (each domain exported / restored on its own — small Telegram-friendly files) ──
    // Mirrors StoreData's section→collections mapping EXACTLY, so a partial file taken from either backend
    // restores into the other. A section snapshot carries only its own collections plus the Section marker the
    // backup controller verifies before restoring.
    public string SerializeSection(BackupSection section)
    {
        var s = new StoreSnapshot { Section = section.ToString() };
        switch (section)
        {
            case BackupSection.Catalog:
                s.Categories = GetCategories().ToList();
                s.Products = GetProducts().ToList();
                s.StockItems = GetStockItems().ToList();
                s.StockAccounts = GetStockAccounts().ToList();
                s.Plans = GetPlans().ToList();
                s.PlanTypes = GetPlanTypes().ToList();
                s.DiscountCodes = GetDiscountCodes().ToList();
                break;
            case BackupSection.Content:
                s.HeroSlides = GetHeroSlides().ToList();
                s.HomeCategories = GetHomeCategories().ToList();
                s.Showcase = GetShowcase().ToList();
                s.BlogPosts = GetBlogPosts().ToList();
                s.SiteContent = GetSiteContent();
                s.Settings = GetSettings();
                break;
            case BackupSection.Users:
                s.Users = GetUsers().ToList();
                s.Cards = GetAllCards().ToList();
                s.Kyc = GetAllKyc().ToList();
                s.ReferralEarnings = AllJson<ReferralEarning>("ReferralEarnings");
                s.Favorites = GetSingleton<Dictionary<int, List<int>>>(FavoritesKey);
                break;
            case BackupSection.Commerce:
                s.Orders = GetOrders().ToList();
                s.Transactions = GetTransactions().ToList();
                s.PaymentMethods = GetPaymentMethods().ToList();
                s.PaymentSettings = GetPaymentSettings();
                break;
            case BackupSection.Support:
                s.Tickets = GetTickets().ToList();
                s.Comments = GetComments().ToList();
                s.Notifications = GetAllNotifications().ToList();
                s.Conversations = GetConversations().ToList();
                break;
            case BackupSection.System:
                s.EmailSettings = GetEmailSettings();
                s.TelegramSettings = GetTelegramSettings();
                s.AdvancedSettings = GetAdvancedSettings();
                break;
        }
        return JsonSerializer.Serialize(s, SnapshotJson);
    }

    // Replaces ONLY the given section's tables/singletons from a partial snapshot; every other domain is left
    // untouched. Runs in one FK-disabled IMMEDIATE transaction (a Users restore drops user rows that live
    // Transactions still reference), so the swap is atomic and never trips the nominal foreign keys. Forces
    // the same clean re-sync as a full restore (see LoadSnapshot) when clustering is on.
    public void RestoreSection(BackupSection section, StoreSnapshot s)
    {
        RestoreSectionTx(section, s);
        if (_clusterEnabled) ReseedOutboxFromCurrentState();
    }

    private void RestoreSectionTx(BackupSection section, StoreSnapshot s) =>
        WriteTxNoFk<object?>((conn, tx) =>
        {
            switch (section)
            {
                case BackupSection.Catalog:
                    conn.Execute("DELETE FROM Categories; DELETE FROM Products; DELETE FROM StockItems; DELETE FROM StockAccounts; DELETE FROM Plans; DELETE FROM DiscountCodes;", transaction: tx);
                    foreach (var c in s.Categories) InsRow(conn, tx, "Categories", c.Id, c);
                    foreach (var p in s.Products) UpsertProduct(conn, tx, p);
                    foreach (var si in s.StockItems)
                        conn.Execute("INSERT INTO StockItems (Id, ProductId, Status, DataJson) VALUES (@Id,@ProductId,@Status,@DataJson)",
                            new { si.Id, si.ProductId, Status = (int)si.Status, DataJson = Serialize(si) }, tx);
                    foreach (var sa in s.StockAccounts)
                        conn.Execute("INSERT INTO StockAccounts (Id, ProductId, DataJson) VALUES (@Id,@ProductId,@DataJson)",
                            new { sa.Id, sa.ProductId, DataJson = Serialize(sa) }, tx);
                    foreach (var p in s.Plans) InsRow(conn, tx, "Plans", p.Id, p);
                    foreach (var d in s.DiscountCodes)
                        conn.Execute("INSERT INTO DiscountCodes (Id, Code, DataJson) VALUES (@Id,@Code,@DataJson)",
                            new { d.Id, d.Code, DataJson = Serialize(d) }, tx);
                    WriteSingleton(conn, tx, PlanTypesKey, s.PlanTypes);
                    break;
                case BackupSection.Content:
                    conn.Execute("DELETE FROM HeroSlides; DELETE FROM HomeCategories; DELETE FROM Showcase; DELETE FROM BlogPosts;", transaction: tx);
                    foreach (var h in s.HeroSlides) InsRow(conn, tx, "HeroSlides", h.Id, h);
                    foreach (var h in s.HomeCategories) InsRow(conn, tx, "HomeCategories", h.Id, h);
                    foreach (var sh in s.Showcase) InsRow(conn, tx, "Showcase", sh.Id, sh);
                    foreach (var b in s.BlogPosts) InsRow(conn, tx, "BlogPosts", b.Id, b);
                    WriteSingleton(conn, tx, SiteContentKey, s.SiteContent);
                    WriteSingleton(conn, tx, PricingKey, s.Settings);
                    break;
                case BackupSection.Users:
                    conn.Execute("DELETE FROM Users; DELETE FROM Cards; DELETE FROM Kyc; DELETE FROM ReferralEarnings;", transaction: tx);
                    foreach (var u in s.Users) UpsertUser(conn, tx, u);
                    foreach (var c in s.Cards)
                        conn.Execute("INSERT INTO Cards (Id, UserId, Status, DataJson) VALUES (@Id,@UserId,@Status,@DataJson)",
                            new { c.Id, c.UserId, Status = (int)c.Status, DataJson = Serialize(c) }, tx);
                    foreach (var k in s.Kyc) InsRow(conn, tx, "Kyc", k.Id, k);
                    foreach (var r in s.ReferralEarnings)
                        conn.Execute("INSERT INTO ReferralEarnings (ReferrerId, DataJson) VALUES (@ReferrerId,@DataJson)",
                            new { r.ReferrerId, DataJson = Serialize(r) }, tx);
                    WriteSingleton(conn, tx, FavoritesKey, s.Favorites);
                    break;
                case BackupSection.Commerce:
                    conn.Execute("DELETE FROM Orders; DELETE FROM Transactions; DELETE FROM PaymentMethods;", transaction: tx);
                    foreach (var o in s.Orders) UpsertOrder(conn, tx, o);
                    foreach (var t in s.Transactions)
                        conn.Execute("INSERT INTO Transactions (Id, UserId, Status, Date, DataJson) VALUES (@Id,@UserId,@Status,@Date,@DataJson)",
                            new { t.Id, t.UserId, Status = (int)t.Status, t.Date, DataJson = Serialize(t) }, tx);
                    foreach (var m in s.PaymentMethods)
                        conn.Execute("INSERT INTO PaymentMethods (Id, DataJson) VALUES (@Id,@DataJson)", new { m.Id, DataJson = Serialize(m) }, tx);
                    WriteSingleton(conn, tx, PaymentKey, s.PaymentSettings);
                    break;
                case BackupSection.Support:
                    conn.Execute("DELETE FROM Tickets; DELETE FROM Comments; DELETE FROM Notifications; DELETE FROM Conversations;", transaction: tx);
                    foreach (var tk in s.Tickets) InsRow(conn, tx, "Tickets", tk.Id, tk);
                    foreach (var cm in s.Comments) InsRow(conn, tx, "Comments", cm.Id, cm);
                    foreach (var n in s.Notifications)
                        conn.Execute("INSERT INTO Notifications (Id, UserId, DataJson) VALUES (@Id,@UserId,@DataJson)",
                            new { n.Id, n.UserId, DataJson = Serialize(n) }, tx);
                    foreach (var cv in s.Conversations) InsRow(conn, tx, "Conversations", cv.Id, cv);
                    // keep the global chat-message counter at least at the highest restored message id, so new
                    // messages never reuse an id (mirrors StoreData.RecomputeSeqFromData for the chat counter).
                    var maxMsg = s.Conversations.SelectMany(c => c.Messages).Select(m => m.Id).DefaultIfEmpty(0).Max();
                    conn.Execute("INSERT INTO Counters (Name, Value) VALUES ('chatMessage', @v) ON CONFLICT(Name) DO UPDATE SET Value = MAX(Value, @v)",
                        new { v = maxMsg }, tx);
                    break;
                case BackupSection.System:
                    WriteSingleton(conn, tx, EmailKey, s.EmailSettings);
                    WriteSingleton(conn, tx, TelegramKey, s.TelegramSettings);
                    WriteSingleton(conn, tx, AdvancedKey, s.AdvancedSettings);
                    break;
            }
            return null;
        });

    // ── Live, consistent single-file backup (for the Telegram bot) ──────────────────────────────────────
    // `VACUUM INTO` writes a transactionally-consistent, defragmented COPY of the whole database to one file,
    // taken as of a read snapshot. Under WAL it does NOT block writers (they keep appending to the WAL) and
    // needs no app downtime — so the backup worker can grab a clean `.db` while the server is live. The
    // produced file is a normal SQLite database the bot can ship as-is and that restores by simply opening it.
    public string BackupToFile(string destPath)
    {
        var full = Path.GetFullPath(destPath);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        if (File.Exists(full)) File.Delete(full); // VACUUM INTO requires the target not to exist

        using var conn = OpenConnection();
        conn.Execute($"VACUUM INTO '{full.Replace("'", "''")}'");
        return full;
    }
}

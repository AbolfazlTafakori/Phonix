using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Data;

// Settings singletons, USD rate, verification levels, admin badge counts.
// Partial of SqliteDataStore -- split by domain the same way the JSON StoreData is (StoreOrders.cs etc.).
public sealed partial class SqliteDataStore
{
    // ── Settings singletons (the remaining blobs) ───────────────────────────────────────────────────────
    private const string SiteContentKey = "sitecontent";
    private const string AdvancedKey = "advanced";
    private const string EmailKey = "email";
    private const string TelegramKey = "telegram";
    private const string PlanTypesKey = "plantypes";
    private const string FavoritesKey = "favorites";

    public SiteContent GetSiteContent() => GetSingleton<SiteContent>(SiteContentKey);
    public void UpdateSiteContent(SiteContent c) { using var conn = OpenConnection(); WriteSingleton(conn, null, SiteContentKey, c); }
    public AdvancedSettings GetAdvancedSettings() => GetSingleton<AdvancedSettings>(AdvancedKey);
    public void UpdateAdvancedSettings(AdvancedSettings s) { using var conn = OpenConnection(); WriteSingleton(conn, null, AdvancedKey, s); }
    public EmailSettings GetEmailSettings() => GetSingleton<EmailSettings>(EmailKey);
    public void UpdateEmailSettings(EmailSettings settings) { using var conn = OpenConnection(); WriteSingleton(conn, null, EmailKey, settings); }
    public TelegramSettings GetTelegramSettings() => GetSingleton<TelegramSettings>(TelegramKey);

    public void UpdateTelegramSettings(TelegramSettings settings)
    {
        using var conn = OpenConnection();
        var t = ReadSingletonNoTx<TelegramSettings>(conn, TelegramKey);
        t.BackupEnabled = settings.BackupEnabled;
        t.AlertsEnabled = settings.AlertsEnabled;
        t.ReceiptBotEnabled = settings.ReceiptBotEnabled;
        t.BotToken = (settings.BotToken ?? "").Trim();
        t.ChatId = (settings.ChatId ?? "").Trim();
        t.ReceiptBotToken = (settings.ReceiptBotToken ?? "").Trim();
        t.ReceiptChatId = (settings.ReceiptChatId ?? "").Trim();
        t.OrderBotEnabled = settings.OrderBotEnabled;
        t.OrderBotToken = (settings.OrderBotToken ?? "").Trim();
        t.OrderChatId = (settings.OrderChatId ?? "").Trim();
        t.IntervalHours = settings.IntervalHours < 1 ? 1 : settings.IntervalHours;
        t.LastBackupError = "";
        WriteSingleton(conn, null, TelegramKey, t);
    }

    public void RecordTelegramBackup(bool success, string error)
    {
        using var conn = OpenConnection();
        var t = ReadSingletonNoTx<TelegramSettings>(conn, TelegramKey);
        if (success) t.LastBackupAtUtc = DateTime.UtcNow;
        t.LastBackupError = success ? "" : error;
        WriteSingleton(conn, null, TelegramKey, t);
    }

    private static T ReadSingletonNoTx<T>(SqliteConnection conn, string key) where T : new()
    {
        var json = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Singletons WHERE Key = @key", new { key });
        return json is null ? new T() : (Deserialize<T>(json) ?? new T());
    }

    // ── USD rate + verification levels ──────────────────────────────────────────────────────────────────
    public void SetUsdRate(long manualToman, bool auto)
    {
        using var conn = OpenConnection();
        var s = ReadSingletonNoTx<PricingSettings>(conn, PricingKey);
        s.ManualUsdRate = Math.Max(0, manualToman);
        s.UsdRateAuto = auto;
        WriteSingleton(conn, null, PricingKey, s);
    }

    public bool ApplyUsdRate(long tomanPerUsd)
    {
        if (tomanPerUsd <= 0) return false;
        return WriteTx((conn, tx) =>
        {
            var changed = false;
            foreach (var row in conn.Query("SELECT Id, DataJson FROM Products", transaction: tx).ToList())
            {
                var p = Deserialize<Product>((string)row.DataJson)!;
                var rowChanged = false;
                if (p.PriceUsd > 0)
                {
                    var toman = (long)Math.Round(p.PriceUsd * tomanPerUsd);
                    if (toman != p.Price) { p.Price = toman; rowChanged = true; }
                }
                foreach (var pl in p.Plans)
                {
                    if (pl.PriceUsd <= 0) continue;
                    var planToman = (long)Math.Round(pl.PriceUsd * tomanPerUsd);
                    if (planToman != pl.Price) { pl.Price = planToman; rowChanged = true; }
                }
                if (rowChanged) { UpsertProduct(conn, tx, p); changed = true; }
            }
            foreach (var pl in conn.Query<string>("SELECT DataJson FROM Plans", transaction: tx).ToList())
            {
                var plan = Deserialize<SubscriptionPlan>(pl)!;
                if (plan.PriceUsd <= 0) continue;
                var toman = (long)Math.Round(plan.PriceUsd * tomanPerUsd);
                if (toman != plan.Price) { plan.Price = toman; conn.Execute("UPDATE Plans SET DataJson=@d WHERE Id=@id", new { d = Serialize(plan), id = plan.Id }, tx); changed = true; }
            }
            return changed;
        });
    }

    public void HealVerificationLevels() =>
        WriteTx<object?>((conn, tx) =>
        {
            var cards = conn.Query<string>("SELECT DataJson FROM Cards", transaction: tx).Select(j => Deserialize<BankCard>(j)!).ToList();
            foreach (var uj in conn.Query<string>("SELECT DataJson FROM Users", transaction: tx).ToList())
            {
                var u = Deserialize<AppUser>(uj)!;
                var derived = u.Verified ? 2 : (cards.Any(c => c.UserId == u.Id && c.Status == BankCardStatus.Approved) ? 1 : 0);
                var changed = false;
                if (u.VerificationLevel < derived) { u.VerificationLevel = derived; changed = true; }
                if (u.VerificationLevel >= 2 && !u.Verified) { u.Verified = true; changed = true; }
                if (changed) UpsertUser(conn, tx, u);
            }
            return null;
        });

    public AppUser? SetVerificationLevel(int userId, int level) =>
        WriteTx<AppUser?>((conn, tx) =>
        {
            var user = LoadUser(conn, tx, userId);
            if (user is null) return null;
            level = Math.Clamp(level, 0, 2);

            if (level < 2)
                foreach (var row in conn.Query("SELECT Id, DataJson FROM Kyc", transaction: tx).ToList())
                {
                    var k = Deserialize<KycRequest>((string)row.DataJson)!;
                    if (k.UserId == userId && k.Status == KycStatus.Approved)
                    {
                        k.Status = KycStatus.Rejected; k.Note = "احراز هویت توسط مدیر لغو شد";
                        conn.Execute("UPDATE Kyc SET DataJson=@d WHERE Id=@id", new { d = Serialize(k), id = k.Id }, tx);
                    }
                }
            if (level < 1)
                foreach (var c in conn.Query<string>("SELECT DataJson FROM Cards", transaction: tx).ToList())
                {
                    var card = Deserialize<BankCard>(c)!;
                    if (card.UserId == userId && card.Status == BankCardStatus.Approved)
                    {
                        card.Status = BankCardStatus.Rejected; card.Note = "توسط مدیر لغو شد";
                        conn.Execute("UPDATE Cards SET Status=@s, DataJson=@d WHERE Id=@id", new { s = (int)card.Status, d = Serialize(card), id = card.Id }, tx);
                    }
                }

            user.VerificationLevel = level;
            user.Verified = level >= 2;
            UpsertUser(conn, tx, user);
            return user;
        });


    // ── Admin badge counts ──────────────────────────────────────────────────────────────────────────────
    public AdminBadgeCounts GetAdminBadgeCounts()
    {
        using var conn = OpenConnection();
        int Count(string sql, object p) => conn.ExecuteScalar<int>(sql, p);
        var tickets = AllJson<Ticket>("Tickets");
        var kyc = AllJson<KycRequest>("Kyc");
        var comments = AllJson<Comment>("Comments");
        var convos = AllJson<ChatConversation>("Conversations");
        return new AdminBadgeCounts(
            PendingOrders: Count("SELECT COUNT(1) FROM Orders WHERE Status=@s", new { s = (int)OrderStatus.PendingApproval }),
            PreparingOrders: Count("SELECT COUNT(1) FROM Orders WHERE Status=@s", new { s = (int)OrderStatus.Preparing }),
            PendingTransactions: Count("SELECT COUNT(1) FROM Transactions WHERE Status=@s", new { s = (int)TxStatus.Pending }),
            OpenTickets: tickets.Count(t => t.Status == TicketStatus.Open),
            PendingKyc: kyc.Count(k => k.Status == KycStatus.Pending),
            PendingCards: Count("SELECT COUNT(1) FROM Cards WHERE Status=@s", new { s = (int)BankCardStatus.Pending }),
            PendingComments: comments.Count(c => c.Status == CommentStatus.Pending),
            UnreadChats: convos.Count(c => c.Messages.Any(m => !m.FromAdmin && m.Id > c.AdminReadUpTo)));
    }
}

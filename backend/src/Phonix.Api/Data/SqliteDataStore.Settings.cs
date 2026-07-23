using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Phonix.Api.Models;
using Phonix.Api.Security;
using Phonix.Api.Services;

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
    private const string MailboxKey = "mailbox";
    private const string V2RayKey = "v2ray";
    private const string PlanTypesKey = "plantypes";
    private const string FavoritesKey = "favorites";

    public SiteContent GetSiteContent() => GetSingleton<SiteContent>(SiteContentKey);
    public void UpdateSiteContent(SiteContent c) { using var conn = OpenConnection(); WriteSingleton(conn, null, SiteContentKey, c); }
    public AdvancedSettings GetAdvancedSettings() => GetSingleton<AdvancedSettings>(AdvancedKey);
    public void UpdateAdvancedSettings(AdvancedSettings s) { using var conn = OpenConnection(); WriteSingleton(conn, null, AdvancedKey, s); }
    public EmailSettings GetEmailSettings() => GetSingleton<EmailSettings>(EmailKey);
    public void UpdateEmailSettings(EmailSettings settings) { using var conn = OpenConnection(); WriteSingleton(conn, null, EmailKey, settings); }
    public MailboxSettings GetMailboxSettings()
    {
        var m = GetSingleton<MailboxSettings>(MailboxKey);
        m.Password = SensitiveField.Reveal(m.Password ?? "");
        return m;
    }

    public void UpdateMailboxSettings(MailboxSettings settings)
    {
        using var conn = OpenConnection();
        var stored = ReadSingletonNoTx<MailboxSettings>(conn, MailboxKey);

        stored.Enabled = settings.Enabled;
        stored.ImapHost = (settings.ImapHost ?? "").Trim();
        stored.ImapPort = settings.ImapPort is > 0 and <= 65535 ? settings.ImapPort : 993;
        stored.ImapUseSsl = settings.ImapUseSsl;
        stored.SmtpHost = (settings.SmtpHost ?? "").Trim();
        stored.SmtpPort = settings.SmtpPort is > 0 and <= 65535 ? settings.SmtpPort : 587;
        stored.SmtpUseSsl = settings.SmtpUseSsl;
        stored.Username = (settings.Username ?? "").Trim();
        stored.Address = (settings.Address ?? "").Trim();
        stored.DisplayName = (settings.DisplayName ?? "").Trim();

        // The panel never receives the stored password, so it cannot send it back. An empty value therefore
        // means "unchanged", not "clear it" — otherwise editing the display name would break the connection.
        var incoming = settings.Password ?? "";
        if (!string.IsNullOrEmpty(incoming)) stored.Password = SensitiveField.Protect(incoming);

        WriteSingleton(conn, null, MailboxKey, stored);
    }

    // ── V2Ray panels ────────────────────────────────────────────────────────────────────────────────
    public IReadOnlyList<V2RayPanel> GetV2RayPanels()
    {
        var s = GetSingleton<V2RaySettings>(V2RayKey);
        foreach (var p in s.Panels) p.Password = SensitiveField.Reveal(p.Password ?? "");
        return s.Panels;
    }

    public V2RayPanel? GetV2RayPanel(int id) => GetV2RayPanels().FirstOrDefault(p => p.Id == id);

    public V2RayPanel AddV2RayPanel(V2RayPanel panel)
    {
        using var conn = OpenConnection();
        var s = ReadSingletonNoTx<V2RaySettings>(conn, V2RayKey);
        if (s.NextId < 1) s.NextId = 1;

        panel.Id = s.NextId++;
        panel.CreatedAtUtc = DateTime.UtcNow.ToString("O");
        // Password arrives in plaintext from the controller; it never sits unencrypted in the store.
        panel.Password = string.IsNullOrEmpty(panel.Password) ? "" : SensitiveField.Protect(panel.Password);
        s.Panels.Add(panel);
        WriteSingleton(conn, null, V2RayKey, s);

        panel.Password = SensitiveField.Reveal(panel.Password);
        return panel;
    }

    public bool DeleteV2RayPanel(int id)
    {
        using var conn = OpenConnection();
        var s = ReadSingletonNoTx<V2RaySettings>(conn, V2RayKey);
        var removed = s.Panels.RemoveAll(p => p.Id == id) > 0;
        if (removed) WriteSingleton(conn, null, V2RayKey, s);
        return removed;
    }

    public void RecordV2RayPanelCheck(int id, bool ok, string error, int inboundCount)
    {
        using var conn = OpenConnection();
        var s = ReadSingletonNoTx<V2RaySettings>(conn, V2RayKey);
        var panel = s.Panels.FirstOrDefault(p => p.Id == id);
        if (panel is null) return;
        panel.LastCheckAtUtc = DateTime.UtcNow.ToString("O");
        panel.LastCheckOk = ok;
        panel.LastCheckError = ok ? "" : (error ?? "");
        if (ok) panel.InboundCount = inboundCount;
        WriteSingleton(conn, null, V2RayKey, s);
    }

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
                if (toman != plan.Price)
                {
                    plan.Price = toman;
                    var planJson = Serialize(plan);
                    conn.Execute("UPDATE Plans SET DataJson=@d WHERE Id=@id", new { d = planJson, id = plan.Id }, tx);
                    AppendOutbox(conn, tx, "Plans", plan.Id, SyncOp.Upsert, planJson);
                    changed = true;
                }
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
                        var kJson = Serialize(k);
                        conn.Execute("UPDATE Kyc SET DataJson=@d WHERE Id=@id", new { d = kJson, id = k.Id }, tx);
                        AppendOutbox(conn, tx, "Kyc", k.Id, SyncOp.Upsert, kJson);
                    }
                }
            if (level < 1)
                foreach (var c in conn.Query<string>("SELECT DataJson FROM Cards", transaction: tx).ToList())
                {
                    var card = Deserialize<BankCard>(c)!;
                    if (card.UserId == userId && card.Status == BankCardStatus.Approved)
                    {
                        card.Status = BankCardStatus.Rejected; card.Note = "توسط مدیر لغو شد";
                        var cardJson = Serialize(card);
                        conn.Execute("UPDATE Cards SET Status=@s, DataJson=@d WHERE Id=@id", new { s = (int)card.Status, d = cardJson, id = card.Id }, tx);
                        AppendOutbox(conn, tx, "Cards", card.Id, SyncOp.Upsert, cardJson);
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
            UnreadChats: convos.Count(c => c.Messages.Any(m => !m.FromAdmin && m.Id > c.AdminReadUpTo)),
            PendingSeatInfo: Count("SELECT COUNT(1) FROM SeatSubmissions WHERE Status=@s", new { s = (int)SeatSubmissionStatus.Pending }));
    }
}

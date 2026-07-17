using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Phonix.Api.Models;

namespace Phonix.Api.Data;

// A full snapshot of the durable store. Sessions are intentionally excluded (ephemeral).
public class StoreSnapshot
{
    // Set on a partial (per-section) backup so a restore can verify the file matches the chosen section.
    // Null/empty on a full snapshot.
    public string? Section { get; set; }

    public List<Category> Categories { get; set; } = new();
    public List<Product> Products { get; set; } = new();
    public List<StockItem> StockItems { get; set; } = new();
    public List<StockAccount> StockAccounts { get; set; } = new();
    public List<AppUser> Users { get; set; } = new();
    public List<SubscriptionPlan> Plans { get; set; } = new();
    public List<HeroSlide> HeroSlides { get; set; } = new();
    public List<HomeCategory> HomeCategories { get; set; } = new();
    public List<Showcase> Showcase { get; set; } = new();
    public List<BlogPost> BlogPosts { get; set; } = new();
    public List<Comment> Comments { get; set; } = new();
    public List<PaymentMethod> PaymentMethods { get; set; } = new();
    public List<Transaction> Transactions { get; set; } = new();
    public List<BankCard> Cards { get; set; } = new();
    public List<KycRequest> Kyc { get; set; } = new();
    public List<Order> Orders { get; set; } = new();
    public List<Ticket> Tickets { get; set; } = new();
    public List<Notification> Notifications { get; set; } = new();
    public List<ChatConversation> Conversations { get; set; } = new();
    public List<ReferralEarning> ReferralEarnings { get; set; } = new();
    public List<DiscountCode> DiscountCodes { get; set; } = new();
    public List<string> PlanTypes { get; set; } = new();
    public Dictionary<int, List<int>> Favorites { get; set; } = new();

    public PricingSettings Settings { get; set; } = new();
    public SiteContent SiteContent { get; set; } = new();
    public AdvancedSettings AdvancedSettings { get; set; } = new();
    public PaymentSettings PaymentSettings { get; set; } = new();
    public EmailSettings EmailSettings { get; set; } = new();
    public TelegramSettings TelegramSettings { get; set; } = new();

    public SeqState Seq { get; set; } = new();

    public class SeqState
    {
        public int Category { get; set; }
        public int Product { get; set; }
        public int Stock { get; set; }
        public int StockAccount { get; set; }
        public int User { get; set; }
        public int Plan { get; set; }
        public int Hero { get; set; }
        public int HomeCat { get; set; }
        public int Showcase { get; set; }
        public int Blog { get; set; }
        public int Comment { get; set; }
        public int Payment { get; set; }
        public int Tx { get; set; }
        public int Card { get; set; }
        public int Kyc { get; set; }
        public int Order { get; set; }
        public int Ticket { get; set; }
        public int Notification { get; set; }
        public int Discount { get; set; }
        public int Conversation { get; set; }
        public int ChatMessage { get; set; }
    }
}

public partial class StoreData
{
    private static readonly JsonSerializerOptions PersistOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string DataFilePath { get; private set; } = "";

    // serializes all disk writes so the periodic flush and the shutdown save can never race on the
    // same file (which previously collided on a shared ".tmp" and threw / risked a corrupt store).
    private readonly object _saveGate = new();

    // ── O(1) dirty tracking ──────────────────────────────────────────────────────────────────────────
    // MarkDirty() bumps _version on every change wired to it (PersistNow and the chat/notification paths).
    // The periodic flush persists immediately whenever _version advances, so idle ticks never serialize or
    // hash. Because most mutators rely on the periodic flush rather than calling PersistNow, a pure version
    // counter alone could miss an unsignaled change; the low-frequency safety re-hash below (≈ once per
    // minute) catches anything that didn't call MarkDirty(), and the unconditional shutdown Save() is the
    // final backstop. Net effect: the heavy WriteIndented serialization + SHA-256 no longer run on every
    // 10-second tick while the store is idle.
    private long _version;                              // bumped on every signaled mutation
    private long _savedVersion = -1;                    // last version written; -1 forces the first flush
    private string _lastSavedHash = "";                 // last on-disk content hash (safety-net comparison)
    private int _idleHashCountdown = SafetyHashInterval; // ticks until the next idle safety re-hash
    private const int SafetyHashInterval = 6;            // ≈ 60s at the worker's 10s cadence

    // Proactive-warning thresholds (see CaptureSnapshot / WriteAtomic): surface lock contention and runaway
    // store growth as warnings before they turn into latency or disk problems.
    private const int GateWaitWarnMs = 250;                      // slow acquisition of the store write lock
    private const long GrowthWarnFloorBytes = 5L * 1024 * 1024;  // ignore growth noise below this size
    private long _lastWrittenBytes;                             // last persisted size, for sharp-growth detection

    // Signals that durable state changed. Cheap and lock-free; safe to call with or without _gate held.
    public void MarkDirty() => Interlocked.Increment(ref _version);

    public StoreSnapshot CaptureSnapshot()
    {
        var sw = Stopwatch.StartNew();
        var lockTaken = false;
        Monitor.Enter(_gate, ref lockTaken);
        sw.Stop();
        try
        {
            if (sw.ElapsedMilliseconds >= GateWaitWarnMs)
                _logger.LogWarning("store.json write lock contended: waited {WaitMs}ms to acquire _gate for a snapshot",
                    sw.ElapsedMilliseconds);

            return new StoreSnapshot
            {
                Categories = _categories.ToList(),
                Products = _products.ToList(),
                StockItems = _stockItems.ToList(),
                StockAccounts = _stockAccounts.ToList(),
                Users = _users.ToList(),
                Plans = _plans.ToList(),
                HeroSlides = _heroSlides.ToList(),
                HomeCategories = _homeCategories.ToList(),
                Showcase = _showcase.ToList(),
                BlogPosts = _blogPosts.ToList(),
                Comments = _comments.ToList(),
                PaymentMethods = _paymentMethods.ToList(),
                Transactions = _transactions.ToList(),
                Cards = _cards.ToList(),
                Kyc = _kyc.ToList(),
                Orders = _orders.ToList(),
                Tickets = _tickets.ToList(),
                Notifications = _notifications.ToList(),
                // Deep-copy each conversation AND its Messages list. The snapshot is serialized OUTSIDE _gate,
                // so a shallow _conversations.ToList() would leave the nested Messages lists shared with the
                // live store and let a concurrent AppendMessage corrupt the serializer's enumeration.
                Conversations = _conversations.Select(c => new ChatConversation
                {
                    Id = c.Id,
                    UserId = c.UserId,
                    UserName = c.UserName,
                    Status = c.Status,
                    CreatedAtUtc = c.CreatedAtUtc,
                    LastMessageAtUtc = c.LastMessageAtUtc,
                    UserReadUpTo = c.UserReadUpTo,
                    AdminReadUpTo = c.AdminReadUpTo,
                    Messages = new List<ChatMessage>(c.Messages),
                }).ToList(),
                ReferralEarnings = _referralEarnings.ToList(),
                DiscountCodes = _discountCodes.ToList(),
                PlanTypes = _planTypes.ToList(),
                Favorites = _favorites.ToDictionary(kv => kv.Key, kv => kv.Value.ToList()),
                Settings = _settings,
                SiteContent = _siteContent,
                AdvancedSettings = _advancedSettings,
                PaymentSettings = _paymentSettings,
                EmailSettings = _emailSettings,
                TelegramSettings = _telegramSettings,
                Seq = new StoreSnapshot.SeqState
                {
                    Category = _categorySeq,
                    Product = _productSeq,
                    Stock = _stockSeq,
                    User = _userSeq,
                    Plan = _planSeq,
                    Hero = _heroSeq,
                    HomeCat = _homeCatSeq,
                    Showcase = _showcaseSeq,
                    Blog = _blogSeq,
                    Comment = _commentSeq,
                    Payment = _paymentSeq,
                    Tx = _txSeq,
                    Card = _cardSeq,
                    Kyc = _kycSeq,
                    Order = _orderSeq,
                    Ticket = _ticketSeq,
                    Notification = _notificationSeq,
                    Discount = _discountSeq,
                    Conversation = _conversationSeq,
                    ChatMessage = _chatMessageSeq,
                },
            };
        }
        finally
        {
            if (lockTaken) Monitor.Exit(_gate);
        }
    }

    public void LoadSnapshot(StoreSnapshot s)
    {
        lock (_gate)
        {
            Replace(_categories, s.Categories);
            Replace(_products, s.Products);
            Replace(_stockItems, s.StockItems);
            Replace(_stockAccounts, s.StockAccounts);
            Replace(_users, s.Users);
            Replace(_plans, s.Plans);
            Replace(_heroSlides, s.HeroSlides);
            Replace(_homeCategories, s.HomeCategories);
            Replace(_showcase, s.Showcase);
            Replace(_blogPosts, s.BlogPosts);
            Replace(_comments, s.Comments);
            Replace(_paymentMethods, s.PaymentMethods);
            Replace(_transactions, s.Transactions);
            Replace(_cards, s.Cards);
            Replace(_kyc, s.Kyc);
            Replace(_orders, s.Orders);
            Replace(_tickets, s.Tickets);
            Replace(_notifications, s.Notifications);
            Replace(_conversations, s.Conversations);
            Replace(_referralEarnings, s.ReferralEarnings);
            Replace(_discountCodes, s.DiscountCodes);
            Replace(_planTypes, s.PlanTypes);

            _favorites.Clear();
            foreach (var kv in s.Favorites) _favorites[kv.Key] = new HashSet<int>(kv.Value);

            _settings = s.Settings;
            _siteContent = s.SiteContent;
            _advancedSettings = s.AdvancedSettings;
            _paymentSettings = s.PaymentSettings;
            _emailSettings = s.EmailSettings;
            _telegramSettings = s.TelegramSettings;

            _categorySeq = s.Seq.Category;
            _productSeq = s.Seq.Product;
            // Older snapshots have no Stock seq — heal from the item list so new ids never collide.
            _stockSeq = Math.Max(s.Seq.Stock, s.StockItems.Count == 0 ? 0 : s.StockItems.Max(x => x.Id));
            _stockAccountSeq = Math.Max(s.Seq.StockAccount, s.StockAccounts.Count == 0 ? 0 : s.StockAccounts.Max(x => x.Id));
            _userSeq = s.Seq.User;
            _planSeq = s.Seq.Plan;
            _heroSeq = s.Seq.Hero;
            _homeCatSeq = s.Seq.HomeCat;
            _showcaseSeq = s.Seq.Showcase;
            _blogSeq = s.Seq.Blog;
            _commentSeq = s.Seq.Comment;
            _paymentSeq = s.Seq.Payment;
            _txSeq = s.Seq.Tx;
            _cardSeq = s.Seq.Card;
            _kycSeq = s.Seq.Kyc;
            _orderSeq = s.Seq.Order;
            _ticketSeq = s.Seq.Ticket;
            _notificationSeq = s.Seq.Notification;
            _discountSeq = s.Seq.Discount;
            _conversationSeq = s.Seq.Conversation;
            _chatMessageSeq = s.Seq.ChatMessage;
            RebuildCatalogView();
            RebuildUserIndex(); // _users was replaced wholesale — rebuild the id/username lookup indexes
        }
    }

    private static void Replace<T>(List<T> target, List<T> source)
    {
        target.Clear();
        target.AddRange(source);
    }

    private bool TryLoad()
    {
        try
        {
            if (!File.Exists(DataFilePath)) return false;
            var json = File.ReadAllText(DataFilePath);
            var snapshot = JsonSerializer.Deserialize<StoreSnapshot>(json, PersistOptions);
            if (snapshot is null) return false;
            LoadSnapshot(snapshot);
            _lastSavedHash = Hash(json);
            // In-memory state now matches disk; align the saved version so the first flush is a no-op
            // unless something actually mutates afterwards.
            _savedVersion = Interlocked.Read(ref _version);
            _idleHashCountdown = SafetyHashInterval;
            return true;
        }
        catch
        {
            return false;
        }
    }

    // the exact on-disk store format (used by the backup export so a download equals store.json).
    public string SerializeSnapshot() => JsonSerializer.Serialize(CaptureSnapshot(), PersistOptions);

    // parses a snapshot from raw JSON using the persistence options (matching enum handling).
    public StoreSnapshot? DeserializeSnapshot(string json) => JsonSerializer.Deserialize<StoreSnapshot>(json, PersistOptions);

    // Unconditional flush (seed bootstrap + shutdown). Always writes and re-aligns the dirty trackers.
    public void Save()
    {
        lock (_saveGate)
        {
            var version = Interlocked.Read(ref _version);
            var json = JsonSerializer.Serialize(CaptureSnapshot(), PersistOptions);
            WriteAtomic(json);
            _savedVersion = version;
            _lastSavedHash = Hash(json);
            _idleHashCountdown = SafetyHashInterval;
        }
    }

    // Durably persists the current state right now (atomic write). The financial units of work call this
    // AFTER fully committing their in-memory mutation under _gate, so a crash can't lose a money-moving
    // change that the 10-second periodic flush hadn't written yet. MarkDirty() guarantees the subsequent
    // SaveIfChanged() observes the change and writes immediately.
    public void PersistNow()
    {
        MarkDirty();
        SaveIfChanged();
    }

    // Periodic flush helper. Idle ticks are O(1) (a version compare); a signaled change writes without the
    // SHA-256; and a periodic safety re-hash catches any mutation that didn't call MarkDirty().
    public void SaveIfChanged()
    {
        lock (_saveGate)
        {
            // Read the version BEFORE capturing so that, if a mutation slips in between, we under-report
            // (savedVersion stays low) and simply write again next tick — never the other way round.
            var version = Interlocked.Read(ref _version);

            if (version != _savedVersion)
            {
                var json = JsonSerializer.Serialize(CaptureSnapshot(), PersistOptions);
                WriteAtomic(json);
                _savedVersion = version;
                _lastSavedHash = Hash(json);
                _idleHashCountdown = SafetyHashInterval;
                return;
            }

            // No signaled change. Most idle ticks return here in O(1).
            if (--_idleHashCountdown > 0) return;
            _idleHashCountdown = SafetyHashInterval;

            // Safety net (≈ once per minute): detect any change made by a path not wired to MarkDirty().
            var snapshotJson = JsonSerializer.Serialize(CaptureSnapshot(), PersistOptions);
            var hash = Hash(snapshotJson);
            if (hash == _lastSavedHash) return;
            WriteAtomic(snapshotJson);
            _lastSavedHash = hash;
            _savedVersion = version;
        }
    }

    // callers hold _saveGate. Writes to a unique temp file then atomically swaps it into place, so a
    // crash mid-write can never leave a half-written store.json and stray temps never collide.
    private void WriteAtomic(string json)
    {
        var dir = Path.GetDirectoryName(DataFilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var bytes = Encoding.UTF8.GetByteCount(json);
        if (_lastWrittenBytes > 0 && bytes > GrowthWarnFloorBytes && bytes > _lastWrittenBytes * 2)
            _logger.LogWarning("store.json grew sharply in one flush: {PreviousKb}KB → {CurrentKb}KB",
                _lastWrittenBytes / 1024, bytes / 1024);

        var tmp = $"{DataFilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tmp, json);
            File.Move(tmp, DataFilePath, overwrite: true);
            _lastWrittenBytes = bytes;
        }
        catch
        {
            if (File.Exists(tmp)) { try { File.Delete(tmp); } catch { /* best effort cleanup */ } }
            throw;
        }
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

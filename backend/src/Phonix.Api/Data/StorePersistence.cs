using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Phonix.Api.Models;

namespace Phonix.Api.Data;

// A full snapshot of the durable store. Sessions are intentionally excluded (ephemeral).
public class StoreSnapshot
{
    public List<Category> Categories { get; set; } = new();
    public List<Product> Products { get; set; } = new();
    public List<AppUser> Users { get; set; } = new();
    public List<SubscriptionPlan> Plans { get; set; } = new();
    public List<HeroSlide> HeroSlides { get; set; } = new();
    public List<HomeCategory> HomeCategories { get; set; } = new();
    public List<Showcase> Showcase { get; set; } = new();
    public List<BlogPost> BlogPosts { get; set; } = new();
    public List<Comment> Comments { get; set; } = new();
    public List<PaymentMethod> PaymentMethods { get; set; } = new();
    public List<Transaction> Transactions { get; set; } = new();
    public List<KycRequest> Kyc { get; set; } = new();
    public List<Order> Orders { get; set; } = new();
    public List<Ticket> Tickets { get; set; } = new();
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
        public int User { get; set; }
        public int Plan { get; set; }
        public int Hero { get; set; }
        public int HomeCat { get; set; }
        public int Showcase { get; set; }
        public int Blog { get; set; }
        public int Comment { get; set; }
        public int Payment { get; set; }
        public int Tx { get; set; }
        public int Kyc { get; set; }
        public int Order { get; set; }
        public int Ticket { get; set; }
        public int Discount { get; set; }
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
    private string _lastSavedHash = "";

    public StoreSnapshot CaptureSnapshot()
    {
        lock (_gate)
        {
            return new StoreSnapshot
            {
                Categories = _categories.ToList(),
                Products = _products.ToList(),
                Users = _users.ToList(),
                Plans = _plans.ToList(),
                HeroSlides = _heroSlides.ToList(),
                HomeCategories = _homeCategories.ToList(),
                Showcase = _showcase.ToList(),
                BlogPosts = _blogPosts.ToList(),
                Comments = _comments.ToList(),
                PaymentMethods = _paymentMethods.ToList(),
                Transactions = _transactions.ToList(),
                Kyc = _kyc.ToList(),
                Orders = _orders.ToList(),
                Tickets = _tickets.ToList(),
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
                    User = _userSeq,
                    Plan = _planSeq,
                    Hero = _heroSeq,
                    HomeCat = _homeCatSeq,
                    Showcase = _showcaseSeq,
                    Blog = _blogSeq,
                    Comment = _commentSeq,
                    Payment = _paymentSeq,
                    Tx = _txSeq,
                    Kyc = _kycSeq,
                    Order = _orderSeq,
                    Ticket = _ticketSeq,
                    Discount = _discountSeq,
                },
            };
        }
    }

    public void LoadSnapshot(StoreSnapshot s)
    {
        lock (_gate)
        {
            Replace(_categories, s.Categories);
            Replace(_products, s.Products);
            Replace(_users, s.Users);
            Replace(_plans, s.Plans);
            Replace(_heroSlides, s.HeroSlides);
            Replace(_homeCategories, s.HomeCategories);
            Replace(_showcase, s.Showcase);
            Replace(_blogPosts, s.BlogPosts);
            Replace(_comments, s.Comments);
            Replace(_paymentMethods, s.PaymentMethods);
            Replace(_transactions, s.Transactions);
            Replace(_kyc, s.Kyc);
            Replace(_orders, s.Orders);
            Replace(_tickets, s.Tickets);
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
            _userSeq = s.Seq.User;
            _planSeq = s.Seq.Plan;
            _heroSeq = s.Seq.Hero;
            _homeCatSeq = s.Seq.HomeCat;
            _showcaseSeq = s.Seq.Showcase;
            _blogSeq = s.Seq.Blog;
            _commentSeq = s.Seq.Comment;
            _paymentSeq = s.Seq.Payment;
            _txSeq = s.Seq.Tx;
            _kycSeq = s.Seq.Kyc;
            _orderSeq = s.Seq.Order;
            _ticketSeq = s.Seq.Ticket;
            _discountSeq = s.Seq.Discount;
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
            return true;
        }
        catch
        {
            return false;
        }
    }

    // the exact on-disk store format (used by the backup export so a download equals store.json).
    public string SerializeSnapshot() => JsonSerializer.Serialize(CaptureSnapshot(), PersistOptions);

    public void Save()
    {
        var json = JsonSerializer.Serialize(CaptureSnapshot(), PersistOptions);
        WriteAtomic(json);
        _lastSavedHash = Hash(json);
    }

    // periodic flush helper: only touches disk when the data actually changed.
    public void SaveIfChanged()
    {
        var json = JsonSerializer.Serialize(CaptureSnapshot(), PersistOptions);
        var hash = Hash(json);
        if (hash == _lastSavedHash) return;
        WriteAtomic(json);
        _lastSavedHash = hash;
    }

    private void WriteAtomic(string json)
    {
        var dir = Path.GetDirectoryName(DataFilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = DataFilePath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, DataFilePath, overwrite: true);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

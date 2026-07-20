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
    public List<SeatSubmission> SeatSubmissions { get; set; } = new();
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
        public int SeatSubmission { get; set; }
        public int ChatMessage { get; set; }
    }
}

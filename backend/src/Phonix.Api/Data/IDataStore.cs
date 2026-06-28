using Phonix.Api.Models;

namespace Phonix.Api.Data;

// The data-access contract for the whole application. Today the only implementation is the in-memory,
// JSON-backed StoreData; the SQLite implementation (step 2 of the persistence migration) will satisfy the
// SAME interface, so swapping it is a one-line DI change with no controller/service edits.
//
// Deliberately EXCLUDED from this contract (they are JSON-persistence internals, not data operations, and
// the SQLite store will have no equivalent): MarkDirty, PersistNow, SaveIfChanged, CaptureSnapshot, and the
// DataFilePath property. Those stay on the concrete StoreData and are consumed only by the JSON-specific
// flush worker (StorePersistenceWorker). The backup/snapshot members ARE part of the contract, because the
// backup controller is implementation-agnostic and SQLite can export/import the same snapshot shape.
public interface IDataStore
{
    // ── Pricing / USD rate ──────────────────────────────────────────────────────────────────────────
    bool ApplyUsdRate(long tomanPerUsd);
    PricingSettings GetSettings();
    void SetUsdRate(long manualToman, bool auto);
    void UpdateSettings(PricingSettings settings);

    // ── Identity verification levels ────────────────────────────────────────────────────────────────
    void HealVerificationLevels();
    AppUser? SetVerificationLevel(int userId, int level);

    // ── Categories ──────────────────────────────────────────────────────────────────────────────────
    IReadOnlyList<Category> GetCategories();
    Category? GetCategory(int id);
    int CountProducts(int categoryId);
    Category AddCategory(Category category);
    bool UpdateCategory(Category category);
    bool DeleteCategory(int id);

    // ── Products + delivery templates ───────────────────────────────────────────────────────────────
    IReadOnlyList<Product> GetProducts(int? categoryId = null, string? search = null);
    Product? GetProduct(int id);
    Product AddProduct(Product product);
    bool UpdateProduct(Product product);
    bool DeleteProduct(int id);
    IReadOnlyList<ProductDeliveryTemplate> GetDeliveryTemplates(int productId);
    ProductDeliveryTemplate? AddDeliveryTemplate(int productId, string title, string content);
    bool DeleteDeliveryTemplate(int productId, int templateId);

    // ── Users ───────────────────────────────────────────────────────────────────────────────────────
    IReadOnlyList<AppUser> GetUsers(string? search = null, UserRole? role = null, bool? blocked = null);
    AppUser? GetUser(int id);
    bool UpdateUser(int id, Action<AppUser> mutate);
    bool DeleteUser(int id);
    bool UsernameExists(string username);
    AppUser? GetUserByUsername(string username);
    string? SetUsername(int userId, string username);
    bool EmailExists(string email);
    string? SetEmail(int userId, string email);
    AppUser? FindByLogin(string identifier);
    AppUser RegisterUser(AppUser user);
    void EnsureOwnerFromEnvironment();

    // ── Subscription plans ──────────────────────────────────────────────────────────────────────────
    IReadOnlyList<SubscriptionPlan> GetPlans();
    SubscriptionPlan AddPlan(SubscriptionPlan plan);
    bool UpdatePlan(SubscriptionPlan plan);
    bool DeletePlan(int id);

    // ── Staff / auth / 2FA / one-time tokens ────────────────────────────────────────────────────────
    StaffResult PromoteToStaff(string username, UserRole role, IEnumerable<string> permissions);
    bool SetUserPermissions(int userId, IEnumerable<string> permissions);
    string RotateSecurityStamp(int userId);
    bool SetTwoFactorSecret(int userId, string secret);
    bool SetTwoFactorEnabled(int userId, bool enabled);
    string CreateToken(int userId, string purpose, TimeSpan lifetime);
    int? ConsumeToken(string? token, string purpose);

    // ── Admin sidebar badges ────────────────────────────────────────────────────────────────────────
    AdminBadgeCounts GetAdminBadgeCounts();

    // ── Live chat ───────────────────────────────────────────────────────────────────────────────────
    ChatConversation? GetUserConversation(int userId);
    void CloseUserConversation(int userId);
    ChatConversation? GetConversation(int id);
    IReadOnlyList<ChatConversation> GetConversations();
    ChatConversation SendUserMessage(int userId, string userName, string body);
    ChatConversation? AddAdminMessage(int conversationId, string authorName, string body);
    void MarkConversationRead(int conversationId, bool byAdmin);
    bool CloseConversation(int id);
    int CountUnreadForUser(int userId);
    int UnreadChatsForAdmin();
    int UnreadMessagesForAdmin(ChatConversation conv);

    // ── Bank cards (level-1 identity) ───────────────────────────────────────────────────────────────
    IReadOnlyList<BankCard> GetAllCards(BankCardStatus? status = null);
    IReadOnlyList<BankCard> GetUserCards(int userId);
    BankCard? GetCard(int id);
    AddCardResult AddCard(int userId, string cardNumber, string holderName, string cardImage);
    BankCard? SetCardStatus(int id, BankCardStatus status, string? note);
    bool DeleteCard(int id);

    // ── Product comments ────────────────────────────────────────────────────────────────────────────
    IReadOnlyList<Comment> GetComments(int? productId = null, CommentStatus? status = null);
    IReadOnlyList<Comment> GetApprovedForProduct(int productId);
    Comment AddComment(Comment c);
    bool SetCommentStatus(int id, CommentStatus status);
    Comment? AddReply(int parentId, string body, string author);
    bool DeleteComment(int id);

    // ── Telegram backup/alert settings ──────────────────────────────────────────────────────────────
    TelegramSettings GetTelegramSettings();
    void UpdateTelegramSettings(TelegramSettings settings);
    void RecordTelegramBackup(bool success, string error);

    // ── Discount codes ──────────────────────────────────────────────────────────────────────────────
    IReadOnlyList<DiscountCode> GetDiscountCodes();
    DiscountCode AddDiscountCode(DiscountCode code);
    bool UpdateDiscountCode(DiscountCode code);
    bool DeleteDiscountCode(int id);
    DiscountResult ResolveDiscount(string? code, long subtotal);

    // ── Email settings ──────────────────────────────────────────────────────────────────────────────
    EmailSettings GetEmailSettings();
    void UpdateEmailSettings(EmailSettings settings);

    // ── Content: hero slides ────────────────────────────────────────────────────────────────────────
    IReadOnlyList<HeroSlide> GetHeroSlides();
    HeroSlide? GetHeroSlide(int id);
    HeroSlide AddHeroSlide(HeroSlide s);
    bool UpdateHeroSlide(HeroSlide s);
    bool DeleteHeroSlide(int id);

    // ── Content: home categories ────────────────────────────────────────────────────────────────────
    IReadOnlyList<HomeCategory> GetHomeCategories();
    HomeCategory? GetHomeCategory(int id);
    HomeCategory AddHomeCategory(HomeCategory c);
    bool UpdateHomeCategory(HomeCategory c);
    bool DeleteHomeCategory(int id);

    // ── Content: showcase cards ─────────────────────────────────────────────────────────────────────
    IReadOnlyList<Showcase> GetShowcase();
    Showcase? GetShowcaseItem(int id);
    Showcase AddShowcase(Showcase s);
    bool UpdateShowcase(Showcase s);
    bool DeleteShowcase(int id);

    // ── Content: blog posts ─────────────────────────────────────────────────────────────────────────
    IReadOnlyList<BlogPost> GetBlogPosts();
    BlogPost? GetBlogPost(int id);
    BlogPost AddBlogPost(BlogPost p);
    bool UpdateBlogPost(BlogPost p);
    bool DeleteBlogPost(int id);

    // ── Content: site content + advanced settings ───────────────────────────────────────────────────
    SiteContent GetSiteContent();
    void UpdateSiteContent(SiteContent c);
    AdvancedSettings GetAdvancedSettings();
    void UpdateAdvancedSettings(AdvancedSettings s);

    // ── Finance: payment methods + settings ─────────────────────────────────────────────────────────
    IReadOnlyList<PaymentMethod> GetPaymentMethods();
    PaymentMethod? GetPaymentMethod(int id);
    PaymentMethod AddPaymentMethod(PaymentMethod m);
    bool UpdatePaymentMethod(PaymentMethod m);
    bool DeletePaymentMethod(int id);
    PaymentSettings GetPaymentSettings();
    void UpdatePaymentSettings(PaymentSettings s);

    // ── Finance: transactions + withdrawals ─────────────────────────────────────────────────────────
    IReadOnlyList<Transaction> GetTransactions(TxStatus? status = null);
    Transaction? GetTransaction(int id);
    IReadOnlyList<Transaction> GetUserTransactions(int userId);
    Transaction AddTransaction(Transaction t);
    bool SetTransactionStatus(int id, TxStatus status, string via, string? note);
    WithdrawalResult RequestWithdrawal(int userId, long amount, string destination);

    // ── Notifications ───────────────────────────────────────────────────────────────────────────────
    Notification AddNotification(int? userId, string title, string body, string? link = null);
    IReadOnlyList<Notification> GetUserNotifications(int userId);
    IReadOnlyList<Notification> GetAllNotifications();
    int CountUnread(int userId);
    void MarkNotificationsRead(int userId);
    bool DeleteNotification(int id);

    // ── Favorites ───────────────────────────────────────────────────────────────────────────────────
    IReadOnlyList<int> GetFavorites(int userId);
    bool ToggleFavorite(int userId, int productId);

    // ── Plan types ──────────────────────────────────────────────────────────────────────────────────
    IReadOnlyList<string> GetPlanTypes();
    bool AddPlanType(string name);
    bool RenamePlanType(string oldName, string newName);
    bool RemovePlanType(string name);

    // ── KYC (level-2 identity) ──────────────────────────────────────────────────────────────────────
    IReadOnlyList<KycRequest> GetAllKyc(KycStatus? status = null);
    KycRequest? GetKycForUser(int userId);
    KycRequest SubmitKyc(KycRequest input);
    KycRequest? SetKycStatus(int id, KycStatus status, string? note);

    // ── Orders + fulfilment + referrals ─────────────────────────────────────────────────────────────
    IReadOnlyList<Order> GetOrders(OrderStatus? status = null);
    IReadOnlyList<Order> GetUserOrders(int userId);
    Order? GetOrder(int id);
    void RefreshAllUserOrderStats();
    PlaceOrderResult PlaceOrder(AppUser user, IEnumerable<(int productId, int quantity, int? planId)> items,
        string paymentMethod, bool fromWallet, string? discountCode = null, int? paymentMethodId = null,
        RemainderPayment? payment = null, bool customerCheckout = false, IReadOnlyList<OrderLineInfo>? lineInfo = null);
    Order? SetOrderStatus(int id, OrderStatus status, string? changedBy = null, string? reason = null);
    Order? DeliverOrder(int id, string content, string? changedBy = null);
    Order? SaveUnitDraft(int orderId, int unitId, string content, string? changedBy = null);
    (Order? order, bool justCompleted) DeliverUnit(int orderId, int unitId, string content, string? changedBy = null);
    OrderActionResult CancelOrder(int id, string? changedBy = null, string? reason = null);
    IReadOnlyList<RenewalReminder> CollectDueRenewalReminders(int hoursBefore);
    IReadOnlyList<ReferralEarning> GetReferralEarnings(int referrerId);
    int CountReferredUsers(int referrerId);

    // ── Support tickets ─────────────────────────────────────────────────────────────────────────────
    IReadOnlyList<Ticket> GetTickets(TicketStatus? status = null);
    IReadOnlyList<Ticket> GetUserTickets(int userId);
    Ticket? GetTicket(int id);
    Ticket CreateTicket(int userId, string userName, string subject, string department, string body,
        TicketPriority priority = TicketPriority.Medium, string attachment = "");
    Ticket CreateTicketForUser(int userId, string userName, string subject, string department, string body,
        string authorName, TicketPriority priority = TicketPriority.Medium, string attachment = "");
    Ticket? ReplyTicket(int id, string author, string body, bool isAdmin, string? attachment = null);
    bool SetTicketStatus(int id, TicketStatus status);

    // ── Backup / snapshot (implementation-agnostic; SQLite will export/import the same shape) ──────────
    string SerializeSnapshot();
    StoreSnapshot? DeserializeSnapshot(string json);
    void LoadSnapshot(StoreSnapshot s);
    void Save();
    string SerializeSection(BackupSection section);
    void RestoreSection(BackupSection section, StoreSnapshot s);
    void RecordBackup(string section, string target, bool ok, string error);
    IReadOnlyList<BackupLogEntry> GetBackupLog();
}

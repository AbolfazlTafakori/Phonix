using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Data;

public partial class StoreData
{
    private readonly object _gate = new();

    private readonly List<Category> _categories = new();
    private readonly List<Product> _products = new();
    private readonly List<AppUser> _users = new();
    private readonly List<SubscriptionPlan> _plans = new();
    private PricingSettings _settings = new();

    // Lock-free read views for the hottest anonymous traffic (the public catalog). They are immutable
    // array snapshots, rebuilt under _gate whenever the product/category set changes and published through
    // a volatile reference, so storefront reads never contend on the write lock that mutations hold.
    private volatile IReadOnlyList<Product> _productsView = Array.Empty<Product>();
    private volatile IReadOnlyList<Category> _categoriesView = Array.Empty<Category>();

    private int _categorySeq;
    private int _productSeq;
    private int _userSeq;
    private int _planSeq;

    public StoreData()
    {
        DataFilePath = Environment.GetEnvironmentVariable("PHONIX_DATA_FILE")
            ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "store.json");
        if (!TryLoad())
        {
            Seed();
            RefreshAllUserOrderStats(); // make the seeded per-user order stats reflect the seeded orders
            Save();
        }
        else
        {
            RefreshAllUserOrderStats(); // heal any drift carried by an older store.json
        }
        HealVerificationLevels(); // older snapshots predate VerificationLevel — derive it from Verified/cards
        RebuildCatalogView();
    }

    // Caller holds _gate (except the constructor, which runs single-threaded). Republishes the lock-free
    // catalog snapshots after the product/category set changes.
    private void RebuildCatalogView()
    {
        _productsView = _products.ToArray();
        _categoriesView = _categories.ToArray();
    }

    // Reconciles each user's identity level from the evidence available, so a store.json saved before the
    // level system (or restored from an old backup) doesn't drop verified users to level 0. Levels only
    // rise here, never fall (upgrades are permanent), and Verified stays in sync with level 2.
    public void HealVerificationLevels()
    {
        lock (_gate)
        {
            foreach (var u in _users)
            {
                var derived = u.Verified ? 2 : (_cards.Any(c => c.UserId == u.Id && c.Status == BankCardStatus.Approved) ? 1 : 0);
                if (u.VerificationLevel < derived) u.VerificationLevel = derived;
                if (u.VerificationLevel >= 2) u.Verified = true;
            }
        }
    }

    // Admin override of a user's identity tier. The approval flows only ever RAISE the level; this can also
    // lower it — revoking verification. When the level drops, the evidence that backed the higher tier is
    // rejected so the user must re-verify to climb back (and HealVerificationLevels won't re-raise it).
    public AppUser? SetVerificationLevel(int userId, int level)
    {
        lock (_gate)
        {
            var user = _users.FirstOrDefault(u => u.Id == userId);
            if (user is null) return null;
            level = Math.Clamp(level, 0, 2);

            // dropping below level 2 revokes the national-ID KYC; below level 1 also revokes the bank card.
            if (level < 2)
                foreach (var k in _kyc.Where(k => k.UserId == userId && k.Status == KycStatus.Approved))
                {
                    k.Status = KycStatus.Rejected;
                    k.Note = "احراز هویت توسط مدیر لغو شد";
                }
            if (level < 1)
                foreach (var c in _cards.Where(c => c.UserId == userId && c.Status == BankCardStatus.Approved))
                {
                    c.Status = BankCardStatus.Rejected;
                    c.Note = "توسط مدیر لغو شد";
                }

            user.VerificationLevel = level;
            user.Verified = level >= 2;
            return user;
        }
    }

    // categories

    public IReadOnlyList<Category> GetCategories() =>
        _categoriesView.OrderBy(c => c.SortOrder).ToList();

    public Category? GetCategory(int id) => _categoriesView.FirstOrDefault(c => c.Id == id);

    public int CountProducts(int categoryId) => _productsView.Count(p => p.CategoryId == categoryId);

    public Category AddCategory(Category category)
    {
        lock (_gate)
        {
            category.Id = ++_categorySeq;
            _categories.Add(category);
            RebuildCatalogView();
            return category;
        }
    }

    public bool UpdateCategory(Category category)
    {
        lock (_gate)
        {
            var existing = _categories.FirstOrDefault(c => c.Id == category.Id);
            if (existing is null) return false;
            existing.Name = category.Name;
            existing.Slug = category.Slug;
            existing.Icon = category.Icon;
            existing.IsActive = category.IsActive;
            existing.SortOrder = category.SortOrder;
            RebuildCatalogView();
            return true;
        }
    }

    public bool DeleteCategory(int id)
    {
        lock (_gate)
        {
            var existing = _categories.FirstOrDefault(c => c.Id == id);
            if (existing is null) return false;
            _categories.Remove(existing);
            RebuildCatalogView();
            return true;
        }
    }

    // products

    public IReadOnlyList<Product> GetProducts(int? categoryId = null, string? search = null)
    {
        IEnumerable<Product> query = _productsView;
        if (categoryId is int cid) query = query.Where(p => p.CategoryId == cid);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p =>
                p.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                p.Sku.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        return query.OrderBy(p => p.Id).ToList();
    }

    public Product? GetProduct(int id) => _productsView.FirstOrDefault(p => p.Id == id);

    public Product AddProduct(Product product)
    {
        lock (_gate)
        {
            product.Id = ++_productSeq;
            NumberPlans(product.Plans);
            _products.Add(product);
            RebuildCatalogView();
            return product;
        }
    }

    public bool UpdateProduct(Product product)
    {
        lock (_gate)
        {
            var existing = _products.FirstOrDefault(p => p.Id == product.Id);
            if (existing is null) return false;
            existing.Name = product.Name;
            existing.CategoryId = product.CategoryId;
            existing.Price = product.Price;
            existing.DiscountPercent = product.DiscountPercent;
            existing.Stock = product.Stock;
            existing.IsActive = product.IsActive;
            existing.Featured = product.Featured;
            existing.Image = product.Image;
            existing.Sku = product.Sku;
            existing.Description = product.Description;
            existing.Warning = product.Warning;
            existing.RequiredLevel = product.RequiredLevel;
            existing.DeliveryTemplate = product.DeliveryTemplate;
            existing.Features = product.Features;
            NumberPlans(product.Plans);
            existing.Plans = product.Plans;
            RebuildCatalogView();
            return true;
        }
    }

    private static void NumberPlans(List<ProductPlan> plans)
    {
        for (var i = 0; i < plans.Count; i++) plans[i].Id = i + 1;
    }

    public bool DeleteProduct(int id)
    {
        lock (_gate)
        {
            var existing = _products.FirstOrDefault(p => p.Id == id);
            if (existing is null) return false;
            _products.Remove(existing);
            RebuildCatalogView();
            return true;
        }
    }

    // reusable per-product delivery templates

    public IReadOnlyList<ProductDeliveryTemplate> GetDeliveryTemplates(int productId)
    {
        lock (_gate)
        {
            var p = _products.FirstOrDefault(x => x.Id == productId);
            return p is null ? Array.Empty<ProductDeliveryTemplate>() : p.DeliveryTemplates.ToList();
        }
    }

    // Adds a named template to a product. The id is unique within the product and stays stable when other
    // templates are deleted (max existing + 1), so the deliver-modal dropdown and delete-by-id stay correct.
    public ProductDeliveryTemplate? AddDeliveryTemplate(int productId, string title, string content)
    {
        ProductDeliveryTemplate? tpl;
        lock (_gate)
        {
            var p = _products.FirstOrDefault(x => x.Id == productId);
            if (p is null) return null;
            tpl = new ProductDeliveryTemplate
            {
                Id = (p.DeliveryTemplates.Count == 0 ? 0 : p.DeliveryTemplates.Max(x => x.Id)) + 1,
                ProductId = productId,
                Title = title.Trim(),
                TemplateContent = content,
            };
            p.DeliveryTemplates.Add(tpl);
        }
        PersistNow(); // admin config change — make it durable immediately.
        return tpl;
    }

    public bool DeleteDeliveryTemplate(int productId, int templateId)
    {
        bool removed;
        lock (_gate)
        {
            var p = _products.FirstOrDefault(x => x.Id == productId);
            var tpl = p?.DeliveryTemplates.FirstOrDefault(x => x.Id == templateId);
            if (p is null || tpl is null) return false;
            removed = p.DeliveryTemplates.Remove(tpl);
        }
        if (removed) PersistNow();
        return removed;
    }

    // users

    public IReadOnlyList<AppUser> GetUsers(string? search = null, UserRole? role = null, bool? blocked = null)
    {
        lock (_gate)
        {
            IEnumerable<AppUser> query = _users;
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(u =>
                    u.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    u.Email.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    u.Phone.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    u.Code.Contains(term, StringComparison.OrdinalIgnoreCase));
            }
            if (role is UserRole r) query = query.Where(u => u.Role == r);
            if (blocked is bool b) query = query.Where(u => u.Blocked == b);
            return query.OrderByDescending(u => u.Id).ToList();
        }
    }

    public AppUser? GetUser(int id)
    {
        lock (_gate) return _users.FirstOrDefault(u => u.Id == id);
    }

    public bool UpdateUser(int id, Action<AppUser> mutate)
    {
        lock (_gate)
        {
            var existing = _users.FirstOrDefault(u => u.Id == id);
            if (existing is null) return false;
            mutate(existing);
            return true;
        }
    }

    public bool DeleteUser(int id)
    {
        lock (_gate)
        {
            var existing = _users.FirstOrDefault(u => u.Id == id);
            if (existing is null) return false;
            _users.Remove(existing);
            return true;
        }
    }

    public bool UsernameExists(string username)
    {
        lock (_gate) return _users.Any(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
    }

    public AppUser? GetUserByUsername(string username)
    {
        lock (_gate) return _users.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
    }

    // Changes the LOGIN handle (which is also the referral code). Every piece of the user's data — orders,
    // transactions, tickets, cards, KYC, referrals (ReferredBy) — is keyed by the immutable integer Id, so a
    // rename re-points the handle without orphaning anything. Returns null on success, else a Persian error.
    public string? SetUsername(int userId, string username)
    {
        lock (_gate)
        {
            var user = _users.FirstOrDefault(u => u.Id == userId);
            if (user is null) return "کاربر یافت نشد.";
            var u = (username ?? "").Trim();
            if (string.Equals(u, user.Username, StringComparison.Ordinal)) return null; // unchanged
            if (u.Length is < 3 or > 20) return "نام کاربری باید بین ۳ تا ۲۰ کاراکتر باشد.";
            if (!u.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')))
                return "نام کاربری فقط می‌تواند شامل حروف و اعداد انگلیسی باشد (بدون فاصله و خط تیره).";
            if (_users.Any(x => x.Id != userId && string.Equals(x.Username, u, StringComparison.OrdinalIgnoreCase)))
                return "این نام کاربری قبلاً گرفته شده است.";
            user.Username = u;
            return null;
        }
    }

    public bool EmailExists(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        lock (_gate) return _users.Any(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
    }

    public AppUser? FindByLogin(string identifier)
    {
        lock (_gate)
            return _users.FirstOrDefault(u =>
                string.Equals(u.Username, identifier, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(u.Email, identifier, StringComparison.OrdinalIgnoreCase) ||
                u.Phone == identifier);
    }

    public AppUser RegisterUser(AppUser user)
    {
        lock (_gate)
        {
            user.Id = ++_userSeq;
            user.Code = $"U-{1000 + user.Id}";
            user.Role = UserRole.Customer;
            user.SecurityStamp = NewStamp();
            user.EmailVerified = false; // must confirm their email before they can order
            if (string.IsNullOrWhiteSpace(user.JoinedAt)) user.JoinedAt = Today();
            _users.Add(user);
            return user;
        }
    }

    // pricing

    public PricingSettings GetSettings()
    {
        lock (_gate) return _settings;
    }

    public void UpdateSettings(PricingSettings settings)
    {
        lock (_gate) _settings = settings;
        // Settings take effect instantly (every read goes through _gate and sees the new object). Flush
        // synchronously so a crash/restart/deploy right after the change can never lose it.
        PersistNow();
    }

    public IReadOnlyList<SubscriptionPlan> GetPlans()
    {
        lock (_gate) return _plans.OrderBy(p => p.Months).ToList();
    }

    public SubscriptionPlan AddPlan(SubscriptionPlan plan)
    {
        lock (_gate)
        {
            plan.Id = ++_planSeq;
            _plans.Add(plan);
            return plan;
        }
    }

    public bool UpdatePlan(SubscriptionPlan plan)
    {
        lock (_gate)
        {
            var existing = _plans.FirstOrDefault(p => p.Id == plan.Id);
            if (existing is null) return false;
            existing.Label = plan.Label;
            existing.Months = plan.Months;
            existing.Price = plan.Price;
            existing.DiscountPercent = plan.DiscountPercent;
            return true;
        }
    }

    public bool DeletePlan(int id)
    {
        lock (_gate)
        {
            var existing = _plans.FirstOrDefault(p => p.Id == id);
            if (existing is null) return false;
            _plans.Remove(existing);
            return true;
        }
    }

    private void Seed()
    {
        AddCategory(new Category { Name = "فیلم و سریال", Slug = "films", Icon = "/figma/cat-film.png", SortOrder = 1 });
        AddCategory(new Category { Name = "موسیقی", Slug = "music", Icon = "/figma/cat-music.png", SortOrder = 2 });
        AddCategory(new Category { Name = "گرافیک و طراحی", Slug = "graphic", Icon = "/figma/cat-graphic.png", SortOrder = 3 });
        AddCategory(new Category { Name = "کارت اعتباری", Slug = "credit", Icon = "/figma/e67d98d153b9caf9a7453da98a1c85ae776bd4bb.png", SortOrder = 4 });
        AddCategory(new Category { Name = "شبکه‌های اجتماعی", Slug = "social", Icon = "/figma/cat-social.png", SortOrder = 5 });
        AddCategory(new Category { Name = "بازی و سرگرمی", Slug = "games", Icon = "/figma/cat-games.png", IsActive = false, SortOrder = 6 });
        AddCategory(new Category { Name = "صرافی ارز دیجیتال", Slug = "exchange", Icon = "/figma/cat-exchange.png", SortOrder = 7 });

        AddProduct(new Product { Name = "اشتراک نتفلیکس", CategoryId = 1, Price = 290_000, Stock = 142, Featured = true, Image = "/figma/prod-netflix.png", Sku = "NFX-PR", Description = "اکانت پریمیوم نتفلیکس با کیفیت 4K و تحویل آنی.", Warning = "از تغییر ایمیل و رمز اکانت خودداری کنید. در صورت تغییر اطلاعات ورود، گارانتی باطل می‌شود.", DeliveryTemplate = "ایمیل اکانت: \nرمز عبور: \nپروفایل اختصاصی شما: \n\nلطفاً از تغییر ایمیل و رمز اکانت خودداری کنید.", Features = new() { Feat("تحویل آنی پس از پرداخت"), Feat("کیفیت 4K Ultra HD"), Feat("پشتیبانی ۲۴ ساعته"), Feat("گارانتی بازگشت وجه") }, Plans = new() { Plan("اشتراکی", 1, 290_000), Plan("اشتراکی", 3, 790_000, 10), Plan("اختصاصی", 1, 690_000), Plan("اختصاصی", 3, 1_850_000, 5) } });
        AddProduct(new Product { Name = "اسپاتیفای پریمیوم", CategoryId = 2, Price = 185_000, Stock = 88, Image = "/figma/prod-spotify.png", Sku = "SPT-PR", Description = "موسیقی نامحدود بدون تبلیغات.", Features = new() { Feat("تحویل آنی پس از پرداخت"), Feat("پخش بدون تبلیغات"), Feat("کیفیت صوتی بالا"), Feat("گارانتی بازگشت وجه", false) } });
        AddProduct(new Product { Name = "کانوا پرو", CategoryId = 3, Price = 210_000, DiscountPercent = 10, Stock = 53, Image = "/figma/prod-canva.png", Sku = "CNV-PRO", Description = "دسترسی کامل به ابزارها و قالب‌های حرفه‌ای کانوا.", Features = StdFeatures() });
        AddProduct(new Product { Name = "بایننس وریفای", CategoryId = 7, Price = 850_000, Stock = 0, IsActive = false, RequiredLevel = 2, Image = "/figma/prod-binance.png", Sku = "BNB-VRF", Description = "احراز هویت کامل حساب بایننس.", Features = new() { Feat("تحویل ۲۴ تا ۴۸ ساعته"), Feat("احراز هویت کامل"), Feat("پشتیبانی ۲۴ ساعته") } });
        AddProduct(new Product { Name = "اپل موزیک", CategoryId = 2, Price = 165_000, Stock = 67, Image = "/figma/prod-applemusic.png", Sku = "APL-MUS", Description = "اشتراک اپل موزیک با تحویل سریع.", Features = StdFeatures() });
        AddProduct(new Product { Name = "فری‌لنسر اکانت", CategoryId = 4, Price = 320_000, Stock = 24, Image = "/figma/prod-freelancer.png", Sku = "FRL-ACC", Description = "اکانت آماده فری‌لنسر برای دریافت پروژه.", Features = StdFeatures() });
        AddProduct(new Product { Name = "اینستاگرام وریفای", CategoryId = 5, Price = 450_000, DiscountPercent = 5, Stock = 18, Featured = true, RequiredLevel = 2, Image = "/figma/prod-binance.png", Sku = "IG-VRF", Description = "تیک آبی و افزایش اعتبار پیج.", Features = StdFeatures() });
        AddProduct(new Product { Name = "پابجی یوسی", CategoryId = 6, Price = 120_000, Stock = 200, IsActive = false, Image = "/figma/prod-canva.png", Sku = "PUBG-UC", Description = "شارژ یوسی بازی پابجی موبایل.", Features = StdFeatures() });

        AddUser(new AppUser { Code = "U-1024", Name = "علی محمدی", Username = "ali", Password = "1234", Email = "ali@example.com", Phone = "۰۹۱۲۱۱۱۲۲۳۳", Role = UserRole.Customer, Orders = 12, TotalSpent = 3_200_000, Wallet = 180_000, Verified = true, JoinedAt = "۱۴۰۳/۰۱/۱۵" });
        AddUser(new AppUser { Code = "U-1023", Name = "زهرا کریمی", Username = "zahra", Password = "1234", Email = "zahra@example.com", Phone = "۰۹۱۲۳۳۳۴۴۵۵", Role = UserRole.Customer, Orders = 8, TotalSpent = 1_850_000, Wallet = 54_000, Verified = true, JoinedAt = "۱۴۰۳/۰۲/۰۳" });
        AddUser(new AppUser { Code = "U-1022", Name = "محمد رضایی", Username = "mohammad", Password = "1234", Email = "mohammad@example.com", Phone = "۰۹۳۵۱۲۳۴۵۶۷", Role = UserRole.Support, Orders = 5, TotalSpent = 980_000, Wallet = 0, Verified = true, JoinedAt = "۱۴۰۳/۰۲/۱۱" });
        AddUser(new AppUser { Code = "U-1021", Name = "سارا احمدی", Username = "sara", Password = "1234", Email = "sara@example.com", Phone = "۰۹۹۰۸۷۶۵۴۳۲", Role = UserRole.Customer, Orders = 2, TotalSpent = 450_000, Wallet = 12_000, Verified = false, Blocked = true, JoinedAt = "۱۴۰۳/۰۲/۲۸" });
        AddUser(new AppUser { Code = "U-1020", Name = "رضا نوری", Username = "reza", Password = "1234", Email = "reza@example.com", Phone = "۰۹۱۰۵۵۵۶۶۷۷", Role = UserRole.Admin, Orders = 19, TotalSpent = 5_640_000, Wallet = 920_000, Verified = true, JoinedAt = "۱۴۰۲/۱۲/۰۵" });
        AddUser(new AppUser { Code = "U-1019", Name = "نگار شریفی", Username = "negar", Password = "1234", Email = "negar@example.com", Phone = "۰۹۳۸۴۴۴۳۳۲۲", Role = UserRole.Customer, Orders = 0, TotalSpent = 0, Wallet = 0, Verified = false, JoinedAt = "۱۴۰۳/۰۳/۲۰" });

        _settings = new PricingSettings
        {
            ReferralCommissionPercent = 10m,
            VatPercent = 9m,
            GatewayFeePercent = 1.5m,
            CancellationPenaltyPercent = 10m,
            MinWalletCharge = 50_000,
            MinWithdraw = 100_000,
            Currency = "تومان",
            ShowOriginalPrice = true,
        };

        AddPlan(new SubscriptionPlan { Label = "۱ ماهه", Months = 1, Price = 290_000, DiscountPercent = 0 });
        AddPlan(new SubscriptionPlan { Label = "۳ ماهه", Months = 3, Price = 790_000, DiscountPercent = 10 });
        AddPlan(new SubscriptionPlan { Label = "۶ ماهه", Months = 6, Price = 1_500_000, DiscountPercent = 15 });
        AddPlan(new SubscriptionPlan { Label = "۱۲ ماهه", Months = 12, Price = 2_700_000, DiscountPercent = 25 });

        AddPlanType("اشتراکی");
        AddPlanType("اختصاصی");

        AddDiscountCode(new DiscountCode { Code = "WELCOME10", Type = DiscountType.Percent, Value = 10, MaxDiscount = 100_000, IsActive = true });
        AddDiscountCode(new DiscountCode { Code = "OFF50", Type = DiscountType.Fixed, Value = 50_000, MinOrder = 200_000, UsageLimit = 100, IsActive = true });

        SeedContent();
        SeedFinance();
        SeedEngagement();
        SeedKyc();
        SeedOrders();
    }

    private void AddUser(AppUser user)
    {
        user.Id = ++_userSeq;
        user.Password = PasswordHasher.Hash(user.Password);
        user.EmailVerified = true; // seeded accounts are pre-verified
        user.VerificationLevel = user.Verified ? 2 : 0; // map the seed Verified flag onto the level system
        _users.Add(user);
    }

    private static ProductFeature Feat(string text, bool included = true) => new() { Text = text, Included = included };

    private static ProductPlan Plan(string type, int months, long price, int discountPercent = 0) =>
        new() { Type = type, Months = months, Price = price, DiscountPercent = discountPercent };

    private static List<ProductFeature> StdFeatures() => new()
    {
        Feat("تحویل آنی پس از پرداخت"),
        Feat("پشتیبانی ۲۴ ساعته"),
        Feat("گارانتی بازگشت وجه"),
    };
}

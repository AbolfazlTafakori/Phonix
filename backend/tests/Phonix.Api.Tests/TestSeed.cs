using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Tests;

// The fixture every test builds on, written against IDataStore so it seeds whichever store is behind it.
//
// This used to live inside the JSON store as production seeding, which is why the whole suite could only run
// against that store. Demo data is a test concern, so it lives with the tests: the store under test is now the
// one that actually ships.
//
// Ids are assigned by insertion order and tests depend on them, so DO NOT reorder these calls:
//   product 1 = Netflix (four plans: اشتراکی/اختصاصی × 1/3 months)
//   user 5    = reza, Admin, wallet 920,000 — enough to pay for anything here in full
//   payment 3 = ZarinPal gateway, 3% fee
internal static class TestSeed
{
    public static IDataStore Apply(IDataStore store)
    {
        store.AddCategory(new Category { Name = "فیلم و سریال", Slug = "films", SortOrder = 1 });
        store.AddCategory(new Category { Name = "موسیقی", Slug = "music", SortOrder = 2 });
        store.AddCategory(new Category { Name = "گرافیک و طراحی", Slug = "graphic", SortOrder = 3 });
        store.AddCategory(new Category { Name = "کارت اعتباری", Slug = "credit", SortOrder = 4 });
        store.AddCategory(new Category { Name = "شبکه‌های اجتماعی", Slug = "social", SortOrder = 5 });
        store.AddCategory(new Category { Name = "بازی و سرگرمی", Slug = "games", IsActive = false, SortOrder = 6 });
        store.AddCategory(new Category { Name = "صرافی ارز دیجیتال", Slug = "exchange", SortOrder = 7 });

        store.AddProduct(new Product
        {
            Name = "اشتراک نتفلیکس", CategoryId = 1, Price = 290_000, Stock = 142, Sku = "NFX-PR",
            Plans = new()
            {
                Plan("اشتراکی", 1, 290_000),
                Plan("اشتراکی", 3, 790_000, 10),
                Plan("اختصاصی", 1, 690_000),
                Plan("اختصاصی", 3, 1_850_000, 5),
            },
        });
        store.AddProduct(new Product { Name = "اسپاتیفای پریمیوم", CategoryId = 2, Price = 185_000, Stock = 88, Sku = "SPT-PR" });
        store.AddProduct(new Product { Name = "کانوا پرو", CategoryId = 3, Price = 210_000, DiscountPercent = 10, Stock = 53, Sku = "CNV-PRO" });
        store.AddProduct(new Product { Name = "بایننس وریفای", CategoryId = 7, Price = 850_000, Stock = 0, IsActive = false, RequiredLevel = 2, Sku = "BNB-VRF" });
        store.AddProduct(new Product { Name = "اپل موزیک", CategoryId = 2, Price = 165_000, Stock = 67, Sku = "APL-MUS" });
        store.AddProduct(new Product { Name = "فری‌لنسر اکانت", CategoryId = 4, Price = 320_000, Stock = 24, Sku = "FRL-ACC" });
        store.AddProduct(new Product { Name = "اینستاگرام وریفای", CategoryId = 5, Price = 450_000, DiscountPercent = 5, Stock = 18, RequiredLevel = 2, Sku = "IG-VRF" });
        store.AddProduct(new Product { Name = "پابجی یوسی", CategoryId = 6, Price = 120_000, Stock = 200, IsActive = false, Sku = "PUBG-UC" });

        User("U-1024", "علی محمدی", "ali", "ali@example.com", UserRole.Customer, wallet: 180_000, verified: true);
        User("U-1023", "زهرا کریمی", "zahra", "zahra@example.com", UserRole.Customer, wallet: 54_000, verified: true);
        User("U-1022", "محمد رضایی", "mohammad", "mohammad@example.com", UserRole.Support, wallet: 0, verified: true);
        User("U-1021", "سارا احمدی", "sara", "sara@example.com", UserRole.Customer, wallet: 12_000, verified: false, blocked: true);
        User("U-1020", "رضا نوری", "reza", "reza@example.com", UserRole.Admin, wallet: 920_000, verified: true);
        User("U-1019", "نگار شریفی", "negar", "negar@example.com", UserRole.Customer, wallet: 0, verified: false);

        store.UpdateSettings(new PricingSettings
        {
            ReferralCommissionPercent = 10m,
            VatPercent = 9m,
            GatewayFeePercent = 1.5m,
            CancellationPenaltyPercent = 10m,
            MinWalletCharge = 50_000,
            MinWithdraw = 100_000,
            Currency = "تومان",
            ShowOriginalPrice = true,
        });

        // Payment methods are addressed by seeded id too — method 3 is the 3%-fee gateway the fee tests use.
        store.AddPaymentMethod(new PaymentMethod { Type = PaymentType.Card, Title = "کارت بانکی", Holder = "علی محمدی", Value = "۶۰۳۷-۹۹۷۱-۲۳۴۵-۶۷۸۹", Network = "بانک ملی", SortOrder = 1 });
        store.AddPaymentMethod(new PaymentMethod { Type = PaymentType.Crypto, Title = "تتر (USDT)", Holder = "کیف پول فونیکس", Value = "TXk9...aZ2bQ", Network = "TRC20", SortOrder = 2 });
        store.AddPaymentMethod(new PaymentMethod { Type = PaymentType.Gateway, Title = "درگاه زرین‌پال", Holder = "Phoenix Verify", Value = "zp-merchant-0000", Network = "ZarinPal", FeePercent = 3, SortOrder = 3 });

        store.AddPlanType("اشتراکی");
        store.AddPlanType("اختصاصی");

        store.AddDiscountCode(new DiscountCode { Code = "WELCOME10", Type = DiscountType.Percent, Value = 10, MaxDiscount = 100_000, IsActive = true });
        store.AddDiscountCode(new DiscountCode { Code = "OFF50", Type = DiscountType.Fixed, Value = 50_000, MinOrder = 200_000, UsageLimit = 100, IsActive = true });

        SeedOrders();
        return store;

        // Two orders so the panel has something to show: one delivered (which is what mints an invoice number)
        // and one still awaiting receipt approval. Both are placed with a card rather than the wallet, so no
        // seeded balance is spent — several tests assert on those balances exactly.
        void SeedOrders()
        {
            var buyer = store.GetUser(2)!;   // zahra — not the wallet the money tests measure
            var completed = store.PlaceOrder(buyer, new[] { (1, 1, (int?)null) }, "کارت بانکی", fromWallet: false).Order;
            if (completed is not null)
            {
                store.SetOrderStatus(completed.Id, OrderStatus.Preparing, "seed");
                foreach (var unit in completed.Units.ToList())
                    store.DeliverUnit(completed.Id, unit.Id, "اطلاعات اکانت نمونه", "seed");
            }

            store.PlaceOrder(buyer, new[] { (2, 1, (int?)null) }, "کارت بانکی", fromWallet: false);
        }

        // RegisterUser is the customer sign-up path: it forces Customer/unverified, so the fixture's roles,
        // balances and verification are applied straight after.
        void User(string code, string name, string username, string email, UserRole role, long wallet,
            bool verified, bool blocked = false)
        {
            // Hashing is the caller's job on the real sign-up path (see AuthController), so the fixture does
            // it too — otherwise these accounts could never log in.
            var created = store.RegisterUser(new AppUser
            {
                Code = code, Name = name, Username = username, Password = PasswordHasher.Hash("1234"), Email = email,
            });
            store.UpdateUser(created.Id, u =>
            {
                u.Role = role;
                u.Wallet = wallet;
                u.Blocked = blocked;
                // Seeded accounts are pre-verified for email; identity level mirrors the Verified flag, which
                // is what the purchase gate reads (0 = registered only, 2 = fully verified).
                u.Verified = verified;
                u.EmailVerified = true;
                u.VerificationLevel = verified ? 2 : 0;
            });
        }
    }

    // Writes the fixture as a legacy store.json at `path`, which is what the API's one-time import reads on
    // first boot. The integration tests need the app itself to come up seeded, and this is the same door a
    // real pre-SQLite install walks through — so the import path gets exercised on every run too.
    public static void WriteLegacyFile(string path)
    {
        var scratch = Path.Combine(Path.GetTempPath(), "phonix-tests", Guid.NewGuid() + ".db");
        var seeded = (SqliteDataStore)Apply(new SqliteDataStore(scratch));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, seeded.SerializeSnapshot());
    }

    private static ProductPlan Plan(string type, int months, long price, int discount = 0) =>
        new() { Type = type, Months = months, Price = price, DiscountPercent = discount, IsActive = true };
}

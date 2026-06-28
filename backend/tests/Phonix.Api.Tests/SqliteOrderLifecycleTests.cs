using Phonix.Api.Data;
using Phonix.Api.Models;
using Xunit;

namespace Phonix.Api.Tests;

// Verifies the atomic order-lifecycle writes on SqliteDataStore: referral commission on completion, and the
// stock-restore + penalised refund on cancellation.
public class SqliteOrderLifecycleTests
{
    private static SqliteDataStore FreshStore()
    {
        var dir = Path.Combine(Path.GetTempPath(), "phonix-sqlite-tests");
        Directory.CreateDirectory(dir);
        return new SqliteDataStore(Path.Combine(dir, Guid.NewGuid() + ".db"));
    }

    [Fact]
    public void Completing_a_referred_buyers_order_credits_the_referrer()
    {
        var store = FreshStore();
        store.UpdateSettings(new PricingSettings { ReferralCommissionPercent = 10m, VatPercent = 0m });

        var referrer = store.RegisterUser(new AppUser { Username = "ref", Name = "Referrer" });
        var buyer = store.RegisterUser(new AppUser { Username = "buy", Name = "Buyer", VerificationLevel = 1, ReferredBy = referrer.Id });
        var product = store.AddProduct(new Product { Name = "P", CategoryId = 1, Price = 100_000, Stock = 5, RequiredLevel = 1, IsActive = true });

        var placed = store.PlaceOrder(buyer, new[] { (product.Id, 1, (int?)null) }, "test", fromWallet: false);
        Assert.Null(placed.Error);

        var completed = store.SetOrderStatus(placed.Order!.Id, OrderStatus.Completed);
        Assert.NotNull(completed);
        Assert.Equal(OrderStatus.Completed, completed!.Status);

        // 10% of the 100,000 order total = 10,000 credited to the referrer's wallet.
        Assert.Equal(10_000, store.GetUser(referrer.Id)!.Wallet);
    }

    [Fact]
    public void Cancelling_a_paid_order_restores_stock_and_refunds_minus_penalty()
    {
        var store = FreshStore();
        store.UpdateSettings(new PricingSettings { CancellationPenaltyPercent = 10m, VatPercent = 0m });

        var buyer = store.RegisterUser(new AppUser { Username = "buy", Name = "Buyer", VerificationLevel = 1, Wallet = 500_000 });
        var product = store.AddProduct(new Product { Name = "P", CategoryId = 1, Price = 100_000, Stock = 3, RequiredLevel = 1, IsActive = true });

        // Fully wallet-paid → order goes straight to Preparing; wallet 500k → 400k, stock 3 → 2.
        var placed = store.PlaceOrder(buyer, new[] { (product.Id, 1, (int?)null) }, "test", fromWallet: true);
        Assert.Null(placed.Error);
        Assert.Equal(OrderStatus.Preparing, placed.Order!.Status);
        Assert.Equal(400_000, store.GetUser(buyer.Id)!.Wallet);
        Assert.Equal(2, store.GetProduct(product.Id)!.Stock);

        var cancelled = store.CancelOrder(placed.Order.Id);
        Assert.Null(cancelled.Error);
        Assert.Equal(OrderStatus.Cancelled, cancelled.Order!.Status);

        // collected = full total (100k, since it was Preparing); penalty 10% = 10k; refund 90k.
        Assert.Equal(490_000, store.GetUser(buyer.Id)!.Wallet); // 400k + 90k refund
        Assert.Equal(3, store.GetProduct(product.Id)!.Stock);   // stock restored
    }
}

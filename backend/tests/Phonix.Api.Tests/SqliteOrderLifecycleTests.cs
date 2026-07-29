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

    // The multi-inventory engine and the waiting queue must behave identically on the production (SQLite) store,
    // not just the in-memory JSON one — the reservation/upsert path runs inside an IMMEDIATE transaction here.
    [Fact]
    public void Sqlite_reserves_seats_across_accounts_and_parks_the_shortfall_as_waiting()
    {
        var store = FreshStore();
        var product = store.AddProduct(new Product { Name = "Shared", CategoryId = 1, Price = 50_000, Stock = 99, RequiredLevel = 1, IsActive = true });
        StockAccount Acc(string u) => store.AddStockAccount(new StockAccount { ProductId = product.Id, Username = u, Password = "p", Plan = "Prem", Capacity = 2, Months = 3 });

        // First account (2 seats) can't cover a 4-seat unit: 2 held, unit parks in the queue.
        Acc("first@mail.com");
        var partial = store.ReserveSeatsAcrossAccounts(product.Id, months: 3, planType: "", count: 4, orderId: 42, unitId: 1);
        Assert.False(partial.Complete);
        Assert.Equal(2, partial.Held);

        // A second matching account arrives → the top-up completes the 4 seats across both accounts (idempotent).
        Acc("second@mail.com");
        var full = store.ReserveSeatsAcrossAccounts(product.Id, months: 3, planType: "", count: 4, orderId: 42, unitId: 1);
        Assert.True(full.Complete);
        Assert.Equal(4, full.Held);
        Assert.Equal(2, full.Groups.Count); // seats span both accounts
        Assert.Equal(4, store.GetStockAccounts(product.Id).SelectMany(a => a.Slots)
            .Count(s => s.Status == StockItemStatus.Reserved && s.OrderId == 42 && s.UnitId == 1));

        // Wrong subscription length is never seated: a 1-month account can't serve a 3-month order.
        var other = store.AddProduct(new Product { Name = "Other", CategoryId = 1, Price = 50_000, Stock = 9, RequiredLevel = 1, IsActive = true });
        store.AddStockAccount(new StockAccount { ProductId = other.Id, Username = "wrong", Password = "p", Plan = "P", Capacity = 5, Months = 1 });
        Assert.False(store.ReserveSeatsAcrossAccounts(other.Id, months: 3, planType: "", count: 1, orderId: 7, unitId: 1).Complete);
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

    [Fact]
    public void Cancelling_refunds_only_the_seats_not_yet_delivered()
    {
        var store = FreshStore();
        store.UpdateSettings(new PricingSettings { CancellationPenaltyPercent = 0m, VatPercent = 0m });

        var buyer = store.RegisterUser(new AppUser { Username = "buy", Name = "Buyer", VerificationLevel = 1, Wallet = 500_000 });
        var product = store.AddProduct(new Product { Name = "P", CategoryId = 1, Price = 100_000, Stock = 5, RequiredLevel = 1, IsActive = true });

        // Two accounts, fully wallet-paid → Preparing. Wallet 500k → 300k, stock 5 → 3.
        var placed = store.PlaceOrder(buyer, new[] { (product.Id, 2, (int?)null) }, "test", fromWallet: true);
        Assert.Null(placed.Error);
        Assert.Equal(300_000, store.GetUser(buyer.Id)!.Wallet);

        // Deliver the first account; the second is still pending.
        var order = placed.Order!;
        var first = order.Units[0];
        Assert.NotNull(store.DeliverUnit(order.Id, first.Id, "acc-one", "admin").order);

        var cancelled = store.CancelOrder(order.Id);
        Assert.Null(cancelled.Error);

        // Only the undelivered account is refunded (100k) and its stock (1) returned; the delivered one is kept.
        Assert.Equal(400_000, store.GetUser(buyer.Id)!.Wallet); // 300k + 100k
        Assert.Equal(4, store.GetProduct(product.Id)!.Stock);   // 3 + 1 undelivered
    }

    [Fact]
    public void An_order_with_every_account_delivered_cannot_be_cancelled()
    {
        var store = FreshStore();
        store.UpdateSettings(new PricingSettings { VatPercent = 0m });
        var buyer = store.RegisterUser(new AppUser { Username = "buy", Name = "Buyer", VerificationLevel = 1, Wallet = 500_000 });
        var product = store.AddProduct(new Product { Name = "P", CategoryId = 1, Price = 100_000, Stock = 5, RequiredLevel = 1, IsActive = true });

        var order = store.PlaceOrder(buyer, new[] { (product.Id, 1, (int?)null) }, "test", fromWallet: true).Order!;
        Assert.NotNull(store.DeliverUnit(order.Id, order.Units[0].Id, "acc", "admin").order); // → Completed

        var cancelled = store.CancelOrder(order.Id);
        Assert.NotNull(cancelled.Error); // a fully delivered order is off-limits
    }

    // Regression for a real double-refund/double-restock bug: RejectUnit already refunds and restores stock
    // for the unit it rejects. Cancelling the REST of the same order later must not touch that unit again —
    // it used to, because the cancel path only excluded Delivered units, not Rejected ones.
    [Fact]
    public void Cancelling_the_rest_of_an_order_does_not_re_refund_or_re_restock_a_rejected_unit()
    {
        var store = FreshStore();
        store.UpdateSettings(new PricingSettings { CancellationPenaltyPercent = 0m, VatPercent = 0m });

        var buyer = store.RegisterUser(new AppUser { Username = "buy", Name = "Buyer", VerificationLevel = 1, Wallet = 500_000 });
        var product = store.AddProduct(new Product { Name = "P", CategoryId = 1, Price = 100_000, Stock = 5, RequiredLevel = 1, IsActive = true });

        // Three seats, fully wallet-paid → Preparing. Wallet 500k → 200k, stock 5 → 2.
        var placed = store.PlaceOrder(buyer, new[] { (product.Id, 3, (int?)null) }, "test", fromWallet: true);
        Assert.Null(placed.Error);
        Assert.Equal(200_000, store.GetUser(buyer.Id)!.Wallet);
        Assert.Equal(2, store.GetProduct(product.Id)!.Stock);

        var order = placed.Order!;

        // Reject one seat: refunds its 100k and restores its 1 seat of stock immediately.
        var rejected = store.RejectUnit(order.Id, order.Units[0].Id, "bad credentials", "admin");
        Assert.Null(rejected.error);
        Assert.Equal(100_000, rejected.refunded);
        Assert.Equal(300_000, store.GetUser(buyer.Id)!.Wallet); // 200k + 100k
        Assert.Equal(3, store.GetProduct(product.Id)!.Stock);   // 2 + 1

        // Cancel the remaining two (still-pending) seats. This must refund exactly those two seats (200k) and
        // restore exactly two more stock units (→ 5 total) — the already-settled rejected seat must not be
        // counted again in either direction.
        var cancelled = store.CancelOrder(order.Id);
        Assert.Null(cancelled.Error);
        Assert.Equal(500_000, store.GetUser(buyer.Id)!.Wallet); // 300k + 200k for the 2 still-pending seats
        Assert.Equal(5, store.GetProduct(product.Id)!.Stock);   // all 5 seats accounted for exactly once
    }

    // Regression: an order that completes because its LAST outstanding unit was rejected (the other unit(s)
    // already delivered) used to skip the referral credit entirely — every other way an order reaches
    // Completed (SetOrderStatus, DeliverOrder, DeliverUnit) already paid it.
    [Fact]
    public void Completing_via_a_rejection_that_settles_the_last_unit_still_credits_the_referrer()
    {
        var store = FreshStore();
        store.UpdateSettings(new PricingSettings { ReferralCommissionPercent = 10m, VatPercent = 0m });

        var referrer = store.RegisterUser(new AppUser { Username = "ref2", Name = "Referrer" });
        var buyer = store.RegisterUser(new AppUser { Username = "buy2", Name = "Buyer", VerificationLevel = 1, ReferredBy = referrer.Id });
        var product = store.AddProduct(new Product { Name = "P", CategoryId = 1, Price = 100_000, Stock = 5, RequiredLevel = 1, IsActive = true });

        var order = store.PlaceOrder(buyer, new[] { (product.Id, 2, (int?)null) }, "test", fromWallet: false).Order!;
        Assert.NotNull(store.DeliverUnit(order.Id, order.Units[0].Id, "acc-one", "admin").order); // still Preparing, 1 of 2 left

        var rejected = store.RejectUnit(order.Id, order.Units[1].Id, "bad credentials", "admin"); // settles the order → Completed
        Assert.Null(rejected.error);
        Assert.Equal(OrderStatus.Completed, rejected.order!.Status);

        // 10% of the 200,000 order total = 20,000 credited to the referrer's wallet.
        Assert.Equal(20_000, store.GetUser(referrer.Id)!.Wallet);
    }
}

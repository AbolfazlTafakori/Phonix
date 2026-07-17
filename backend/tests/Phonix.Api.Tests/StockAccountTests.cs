using Microsoft.Extensions.Logging.Abstractions;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Services;
using Xunit;

namespace Phonix.Api.Tests;

// Multi-seat stock accounts: the slot labels are generated (never typed), a purchase always lands on
// CONSECUTIVE slots of ONE account, and the customer-facing message keeps the exact legacy shape.
// Seed reference: product 1 = Netflix, user 5 = reza (wallet 920,000 — enough to pay in full).
public class StockAccountTests
{
    private static StoreData NewStore() => TestStore.Create();

    private static StockFulfillmentService Fulfillment(StoreData store) =>
        new(store, NullLogger<StockFulfillmentService>.Instance);

    private static StockAccount Account(StoreData store, int capacity, string username = "acc@mail.com",
        string plan = "Premium", int months = 3) =>
        store.AddStockAccount(new StockAccount
        {
            ProductId = 1,
            Username = username,
            Password = SensitiveField.Protect("p@ss"),
            Plan = plan,
            Capacity = capacity,
            Months = months,
            AddedBy = "admin",
        });

    private static Order PaidSlotOrder(StoreData store, int qty)
    {
        var product = store.GetProduct(1)!;
        product.SlotFulfillment = true;
        store.UpdateProduct(product);
        var placed = store.PlaceOrder(store.GetUser(5)!, new[] { (1, qty, (int?)null) }, "wallet", fromWallet: true);
        Assert.Null(placed.Error);
        Assert.Equal(OrderStatus.Preparing, placed.Order!.Status);
        return placed.Order;
    }

    // ── Slot generation ────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, "A0")]
    [InlineData(9, "A9")]
    [InlineData(10, "B0")]
    [InlineData(14, "B4")]
    [InlineData(29, "C9")]
    [InlineData(259, "Z9")]
    [InlineData(260, "AA0")] // past Z9 the letters roll, so ANY capacity stays uniquely labelled
    public void Slot_labels_follow_the_letter_block_scheme(int index, string expected) =>
        Assert.Equal(expected, StockAccount.SlotLabel(index));

    [Theory]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(37)] // capacity is dynamic — nothing about the generator knows the "usual" sizes
    public void Creating_an_account_generates_every_slot(int capacity)
    {
        var store = NewStore();
        var acc = Account(store, capacity);

        Assert.Equal(capacity, acc.Slots.Count);
        Assert.Equal(Enumerable.Range(0, capacity).Select(StockAccount.SlotLabel), acc.Slots.Select(s => s.Label));
        Assert.All(acc.Slots, s => Assert.Equal(StockItemStatus.Available, s.Status));
    }

    // ── Consecutive allocation ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Reservation_takes_consecutive_slots_from_one_account()
    {
        var store = NewStore();
        var acc = Account(store, 10);

        var r = store.ReserveStockSlots(1, 3, orderId: 7, unitId: 1);

        Assert.NotNull(r);
        Assert.Equal(acc.Id, r!.Value.Account.Id);
        Assert.Equal(new[] { "A0", "A1", "A2" }, r.Value.Slots.Select(s => s.Label));
        Assert.All(r.Value.Slots, s => Assert.Equal(StockItemStatus.Reserved, s.Status));
    }

    [Fact]
    public void A_fragmented_account_is_skipped_for_the_next_one_with_a_large_enough_run()
    {
        var store = NewStore();
        var first = Account(store, 5);
        // burn A2 → the first account only offers runs of 2 (A0-A1) and 2 (A3-A4).
        Assert.True(store.SetStockSlotStatus(first.Id, first.Slots[2].Id, StockItemStatus.Disabled));
        var second = Account(store, 5, username: "second@mail.com");

        var r = store.ReserveStockSlots(1, 3, orderId: 7, unitId: 1);

        Assert.Equal(second.Id, r!.Value.Account.Id);
        Assert.Equal(new[] { "A0", "A1", "A2" }, r.Value.Slots.Select(s => s.Label));
    }

    [Fact]
    public void Reservation_fails_when_no_account_has_enough_consecutive_slots()
    {
        var store = NewStore();
        var acc = Account(store, 4);
        Assert.True(store.SetStockSlotStatus(acc.Id, acc.Slots[1].Id, StockItemStatus.Disabled));

        Assert.Null(store.ReserveStockSlots(1, 3, orderId: 7, unitId: 1));
    }

    [Fact]
    public void A_disabled_account_is_never_allocated_from()
    {
        var store = NewStore();
        var acc = Account(store, 10);
        Assert.True(store.SetStockAccountDisabled(acc.Id, true));

        Assert.Null(store.ReserveStockSlots(1, 1, orderId: 7, unitId: 1));
    }

    // ── Delivery ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Serving_a_unit_delivers_the_exact_legacy_message()
    {
        var store = NewStore();
        Account(store, 10, username: "netacc@mail.com", plan: "Premium", months: 3);
        var order = PaidSlotOrder(store, qty: 2);
        var unit = order.Units.Single(); // a slot line is ONE deliverable no matter the quantity

        var served = Fulfillment(store).ServeUnit(order, unit, "انبار مجازی");

        Assert.NotNull(served);
        var delivered = served!.Value.order.Units.Single();
        Assert.True(delivered.Delivered);
        Assert.Equal(string.Join("\n", new[]
        {
            delivered.Name,
            "2 Connection",
            "3 Month",
            "",
            "User :",
            "netacc@mail.com",
            "",
            "Pass :",
            "p@ss",
            "",
            "Plan :",
            "Premium",
            "",
            "User",
            "A0",
            "A1",
        }), delivered.DeliveryContent);

        var slots = store.GetStockAccounts(1).Single().Slots;
        Assert.Equal(2, slots.Count(s => s.Status == StockItemStatus.Delivered));
        Assert.True(served.Value.justCompleted); // the single unit was the whole order
    }

    [Fact]
    public void A_fixed_user_count_plan_seats_the_whole_count_not_the_cart_quantity()
    {
        var store = NewStore();
        Account(store, 10);
        var product = store.GetProduct(1)!;
        product.SlotFulfillment = true;
        product.Plans.Clear();
        product.Plans.Add(new ProductPlan { Type = "اشتراکی", Months = 3, Price = 50_000, IsActive = true, UserCount = 6 });
        store.UpdateProduct(product);
        var planId = store.GetProduct(1)!.Plans.Single().Id; // renumbered by the store on save

        // The buyer takes ONE «۶ کاربر» plan (quantity 1) — it must seat all six users, not just one.
        var placed = store.PlaceOrder(store.GetUser(5)!, new[] { (1, 1, (int?)planId) }, "wallet", fromWallet: true);
        Assert.True(placed.Error is null, placed.Error);
        var served = Fulfillment(store).ServeUnit(placed.Order!, placed.Order!.Units.Single(), "انبار مجازی");

        Assert.NotNull(served);
        Assert.Contains("6 Connection", served!.Value.order.Units.Single().DeliveryContent);
        Assert.Equal(6, store.GetStockAccounts(1).Single().Slots.Count(s => s.Status == StockItemStatus.Delivered));
    }

    [Fact]
    public void Serving_the_same_unit_twice_never_burns_a_second_run()
    {
        var store = NewStore();
        Account(store, 10);
        var order = PaidSlotOrder(store, qty: 2);
        var unit = order.Units.Single();
        var svc = Fulfillment(store);

        Assert.NotNull(svc.ServeUnit(order, unit, "انبار مجازی"));
        // a stale retry: the unit is already delivered, so nothing more may be taken.
        var fresh = store.GetOrder(order.Id)!;
        Assert.Null(svc.ServeUnit(fresh, fresh.Units.Single(), "انبار مجازی"));

        var slots = store.GetStockAccounts(1).Single().Slots;
        Assert.Equal(2, slots.Count(s => s.Status == StockItemStatus.Delivered));
        Assert.Equal(8, slots.Count(s => s.Status == StockItemStatus.Available));
    }

    [Fact]
    public void An_empty_pool_degrades_to_manual_fulfillment()
    {
        var store = NewStore();
        var order = PaidSlotOrder(store, qty: 1);

        Assert.Null(Fulfillment(store).ServeUnit(order, order.Units.Single(), "انبار مجازی"));
        Assert.False(store.GetOrder(order.Id)!.Units.Single().Delivered);
    }

    // ── Money & stock on the single-unit line ──────────────────────────────────────────────────────

    [Fact]
    public void Rejecting_a_slot_line_refunds_the_whole_quantity_and_restores_stock()
    {
        var store = NewStore();
        Account(store, 10);
        var order = PaidSlotOrder(store, qty: 2);
        var stockBefore = store.GetProduct(1)!.Stock;
        var walletBefore = store.GetUser(5)!.Wallet;
        var lineTotal = order.Items.Single().LineTotal;

        var (updated, refunded, error) = store.RejectUnit(order.Id, order.Units.Single().Id, "تست", "admin");

        Assert.Null(error);
        Assert.Equal(lineTotal, refunded); // both seats' money, not one
        Assert.Equal(walletBefore + lineTotal, store.GetUser(5)!.Wallet);
        Assert.Equal(stockBefore + 2, store.GetProduct(1)!.Stock);
        Assert.Equal(OrderStatus.Cancelled, updated!.Status);
    }
}

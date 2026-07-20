using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Services;
using Xunit;

namespace Phonix.Api.Tests;

// Multi-inventory fulfillment: one purchase draws seats across as many compatible accounts as it takes
// (product + plan type + subscription months), oldest account first; when the pool can't cover the whole
// unit the seats it got stay reserved and the order waits — never a partial delivery — completing itself the
// moment matching stock is added, in FIFO order.
// Seed reference: product 1 = Netflix, user 5 = reza (wallet 920,000).
public class MultiInventoryFulfillmentTests
{
    private static IDataStore NewStore() => TestStore.Create();

    private static StockFulfillmentService Fulfillment(IDataStore store) =>
        new(store, NullLogger<StockFulfillmentService>.Instance);

    private static StockAccount Account(IDataStore store, int capacity, string username, int months = 3, string planType = "") =>
        store.AddStockAccount(new StockAccount
        {
            ProductId = 1,
            Username = username,
            Password = SensitiveField.Protect("p@ss"),
            Plan = "Premium",
            PlanType = planType,
            Capacity = capacity,
            Months = months,
            AddedBy = "admin",
        });

    // A cheap, fully-wallet-paid (→ Preparing) slot order whose single unit needs `users` seats. Driving the
    // seat count through a fixed-user-count plan (qty 1) keeps the order affordable, so it lands in Preparing
    // where the waiting-for-inventory queue lives — unlike a large qty that would need a receipt.
    private static Order PaidSlotOrder(IDataStore store, int users, int months = 3)
    {
        var product = store.GetProduct(1)!;
        product.SlotFulfillment = true;
        product.AutoDeliverStock = true;
        product.Plans.Clear();
        product.Plans.Add(new ProductPlan { Type = "اشتراکی", Months = months, Price = 50_000, IsActive = true, UserCount = users });
        store.UpdateProduct(product);
        var planId = store.GetProduct(1)!.Plans.Single().Id;
        var placed = store.PlaceOrder(store.GetUser(5)!, new[] { (1, 1, (int?)planId) }, "wallet", fromWallet: true);
        Assert.Null(placed.Error);
        Assert.Equal(OrderStatus.Preparing, placed.Order!.Status);
        return placed.Order!;
    }

    private static int ReservedSeats(IDataStore store, int orderId, int unitId) =>
        store.GetStockAccounts(1).SelectMany(a => a.Slots)
            .Count(s => s.Status == StockItemStatus.Reserved && s.OrderId == orderId && s.UnitId == unitId);

    private static int DeliveredSeats(IDataStore store, int orderId, int unitId) =>
        store.GetStockAccounts(1).SelectMany(a => a.Slots)
            .Count(s => s.Status == StockItemStatus.Delivered && s.OrderId == orderId && s.UnitId == unitId);

    // ── Feature 3: a unit spans several accounts ────────────────────────────────────────────────────

    [Fact]
    public void A_unit_is_seated_across_several_accounts_when_none_alone_has_enough()
    {
        var store = NewStore();
        Account(store, capacity: 2, username: "one@mail.com");
        Account(store, capacity: 2, username: "two@mail.com");
        var order = PaidSlotOrder(store, users: 4); // needs 4 seats, no single account has them

        var served = Fulfillment(store).ServeUnit(order, order.Units.Single(), "انبار مجازی");

        Assert.NotNull(served);
        var content = served!.Value.order.Units.Single().DeliveryContent;
        Assert.Equal(4, Regex.Matches(content, "1 Connection").Count);   // four seats delivered as four blocks
        Assert.Contains("User : one@mail.com", content);                 // both accounts' credentials present
        Assert.Contains("User : two@mail.com", content);
        Assert.Equal(4, DeliveredSeats(store, order.Id, order.Units.Single().Id));
        Assert.All(store.GetStockAccounts(1), a => Assert.Equal(2, a.Slots.Count(s => s.Status == StockItemStatus.Delivered)));
    }

    [Fact]
    public void Allocation_takes_the_oldest_account_first()
    {
        var store = NewStore();
        var older = Account(store, capacity: 2, username: "older@mail.com");
        var newer = Account(store, capacity: 2, username: "newer@mail.com");
        var order = PaidSlotOrder(store, users: 3); // 2 from older, 1 from newer

        Assert.NotNull(Fulfillment(store).ServeUnit(order, order.Units.Single(), "انبار مجازی"));

        Assert.Equal(2, store.GetStockAccount(older.Id)!.Slots.Count(s => s.Status == StockItemStatus.Delivered));
        Assert.Equal(1, store.GetStockAccount(newer.Id)!.Slots.Count(s => s.Status == StockItemStatus.Delivered));
    }

    // ── Feature 4: the waiting-for-inventory queue ──────────────────────────────────────────────────

    [Fact]
    public void A_unit_the_pool_cannot_cover_waits_and_holds_its_seats_without_delivering()
    {
        var store = NewStore();
        Account(store, capacity: 2, username: "only@mail.com"); // only 2 of the 4 needed
        var order = PaidSlotOrder(store, users: 4);
        var unitId = order.Units.Single().Id;

        Assert.Null(Fulfillment(store).ServeUnit(order, order.Units.Single(), "انبار مجازی")); // no delivery

        var after = store.GetOrder(order.Id)!.Units.Single();
        Assert.False(after.Delivered);                    // never a partial delivery
        Assert.True(after.WaitingForInventory);            // parked in the queue
        Assert.Equal(2, ReservedSeats(store, order.Id, unitId)); // the seats it DID get stay reserved
        Assert.Contains(store.GetOrdersWaitingForInventory(), o => o.Id == order.Id);
    }

    [Fact]
    public void Adding_matching_inventory_completes_a_waiting_order_automatically()
    {
        var store = NewStore();
        Account(store, capacity: 2, username: "first@mail.com");
        var order = PaidSlotOrder(store, users: 4);
        var unitId = order.Units.Single().Id;
        Assert.Null(Fulfillment(store).ServeUnit(order, order.Units.Single(), "انبار مجازی")); // waits at 2/4

        // New compatible stock arrives → the pool drains the queue with no manual step.
        Account(store, capacity: 2, username: "second@mail.com");
        var filled = Fulfillment(store).FulfillWaitingOrders();

        Assert.Equal(1, filled);
        var after = store.GetOrder(order.Id)!.Units.Single();
        Assert.True(after.Delivered);
        Assert.False(after.WaitingForInventory);
        Assert.Equal(4, DeliveredSeats(store, order.Id, unitId));
        Assert.Empty(store.GetOrdersWaitingForInventory());
    }

    [Fact]
    public void Waiting_orders_are_filled_in_fifo_order()
    {
        var store = NewStore();
        var first = PaidSlotOrder(store, users: 2);  // placed first
        var second = PaidSlotOrder(store, users: 2); // placed second
        var svc = Fulfillment(store);
        Assert.Null(svc.ServeUnit(first, first.Units.Single(), "انبار مجازی"));   // both wait (empty pool)
        Assert.Null(svc.ServeUnit(second, second.Units.Single(), "انبار مجازی"));

        // Only enough for ONE order arrives → the one that waited longest is completed first.
        Account(store, capacity: 2, username: "single@mail.com");
        Assert.Equal(1, svc.FulfillWaitingOrders());

        Assert.True(store.GetOrder(first.Id)!.Units.Single().Delivered);
        Assert.False(store.GetOrder(second.Id)!.Units.Single().Delivered);
    }

    [Fact]
    public void An_account_of_the_wrong_subscription_length_does_not_seat_the_order()
    {
        var store = NewStore();
        var product = store.GetProduct(1)!;
        product.SlotFulfillment = true;
        product.Plans.Clear();
        product.Plans.Add(new ProductPlan { Type = "اشتراکی", Months = 3, Price = 50_000, IsActive = true, UserCount = 2 });
        store.UpdateProduct(product);
        var planId = store.GetProduct(1)!.Plans.Single().Id;

        Account(store, capacity: 5, username: "wrong@mail.com", months: 1); // 1-month stock, order is 3-month
        var placed = store.PlaceOrder(store.GetUser(5)!, new[] { (1, 1, (int?)planId) }, "wallet", fromWallet: true);
        Assert.Null(placed.Error);
        var order = placed.Order!;

        Assert.Null(Fulfillment(store).ServeUnit(order, order.Units.Single(), "انبار مجازی"));
        Assert.True(store.GetOrder(order.Id)!.Units.Single().WaitingForInventory); // parked, not seated on wrong months

        // The matching 3-month stock lets it through.
        Account(store, capacity: 2, username: "right@mail.com", months: 3);
        Assert.Equal(1, Fulfillment(store).FulfillWaitingOrders());
        Assert.True(store.GetOrder(order.Id)!.Units.Single().Delivered);
    }
}

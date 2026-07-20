using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;
using Phonix.Api.Services;
using Xunit;

namespace Phonix.Api.Tests;

// The stock pool only pays off if EVERY approval path reaches it and the orders group never sees an account
// the pool already handled. These drive the real service (and the real bot, against a fake Telegram) rather
// than the store, because that is exactly where the wiring used to be missing.
// Seed reference: product 1 = Netflix, user 5 = reza (wallet 920,000 — enough to pay in full).
public class StockFulfillmentTests
{
    private const string Token = "123456:test-order-token";
    private const string Chat = "-1001234567890";

    // A fake Telegram that answers getUpdates once with the supplied updates and records every call.
    private sealed class BotHandler : HttpMessageHandler
    {
        private readonly string _updates;
        private bool _served;
        public List<(string method, string body)> Calls = new();

        public BotHandler(string updates = "[]") => _updates = updates;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var method = request.RequestUri!.AbsolutePath.Split('/').Last();
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Calls.Add((method, Uri.UnescapeDataString(body.Replace('+', ' '))));
            var payload = method == "getUpdates" && !_served
                ? $"{{\"ok\":true,\"result\":{_updates}}}"
                : "{\"ok\":true,\"result\":true}";
            if (method == "getUpdates") _served = true;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload) };
        }
    }

    private sealed class StubFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubFactory(HttpMessageHandler h) => _handler = h;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class NoopMailer : IUserMailer
    {
        public Task WelcomeAsync(AppUser user) => Task.CompletedTask;
        public Task LoginNoticeAsync(AppUser user, string ip, string device) => Task.CompletedTask;
        public Task OrderPlacedAsync(Order order) => Task.CompletedTask;
        public Task OrderUnitDeliveredAsync(Order order, int unitId) => Task.CompletedTask;
        public Task OrderCompletedAsync(Order order) => Task.CompletedTask;
        public Task TransactionDecidedAsync(Transaction tx) => Task.CompletedTask;
        public Task TicketRepliedAsync(Ticket ticket) => Task.CompletedTask;
        public Task TicketOpenedByStaffAsync(Ticket ticket) => Task.CompletedTask;
        public Task CardDecidedAsync(BankCard card) => Task.CompletedTask;
        public Task KycDecidedAsync(KycRequest kyc) => Task.CompletedTask;
    }

    private static StockFulfillmentService Fulfillment(IDataStore store) =>
        new(store, NullLogger<StockFulfillmentService>.Instance);

    private static TelegramOrderService Bot(IDataStore store, BotHandler handler)
    {
        store.UpdateTelegramSettings(new TelegramSettings
        {
            OrderBotEnabled = true, OrderBotToken = Token, OrderChatId = Chat,
        });
        return new TelegramOrderService(store, new NoopMailer(), Fulfillment(store), new StubFactory(handler),
            NullLogger<TelegramOrderService>.Instance);
    }

    // A paid order for `qty` ready-made accounts of product 1 (its plans collect nothing from the buyer).
    private static Order PaidOrder(IDataStore store, int qty = 1, bool autoDeliver = false)
    {
        var product = store.GetProduct(1)!;
        product.AutoDeliverStock = autoDeliver;
        store.UpdateProduct(product);
        var placed = store.PlaceOrder(store.GetUser(5)!, new[] { (1, qty, (int?)null) }, "wallet", fromWallet: true);
        Assert.Null(placed.Error);
        Assert.Equal(OrderStatus.Preparing, placed.Order!.Status);
        return placed.Order;
    }

    private static void Stock(IDataStore store, params string[] contents) =>
        store.AddStockItems(1, contents.Select(SensitiveField.Protect), "admin");

    // One staff tap on an account's «✅ تأیید», as Telegram would deliver it.
    private static string ApproveUpdate(int orderId, int unitId) =>
        "[{\"update_id\":1,\"callback_query\":{\"id\":\"cb1\",\"data\":\"ordr:ok:" + orderId + ":" + unitId + "\","
        + "\"from\":{\"id\":555},\"message\":{\"message_id\":77,\"chat\":{\"id\":" + Chat + "}}}}]";

    // ── The orders bot's approve button ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Approving_a_ready_made_account_delivers_it_from_the_pool_without_asking()
    {
        var store = TestStore.Create();
        var order = PaidOrder(store);
        Stock(store, "user@phonix.ir / P@ssw0rd");
        var handler = new BotHandler(ApproveUpdate(order.Id, order.Units[0].Id));

        await Bot(store, handler).ProcessUpdatesAsync(0);

        // Delivered straight from the pool — the buyer has the account and the item is spent.
        var unit = store.GetOrder(order.Id)!.Units[0];
        Assert.True(unit.Delivered);
        Assert.Equal("user@phonix.ir / P@ssw0rd", unit.DeliveryContent);
        Assert.Equal(StockItemStatus.Delivered, store.GetStockItems(1).Single().Status);

        // …and nobody was asked for anything: no ForceReply prompt went out.
        Assert.DoesNotContain(handler.Calls, c => c.body.Contains("force_reply"));
        Assert.DoesNotContain(handler.Calls, c => c.body.Contains("#ACC:"));
    }

    [Fact]
    public async Task Approving_a_ready_made_account_with_an_empty_pool_asks_staff_for_it()
    {
        var store = TestStore.Create();
        var order = PaidOrder(store);
        Assert.Empty(store.GetStockItems(1)); // pool never filled
        var handler = new BotHandler(ApproveUpdate(order.Id, order.Units[0].Id));

        await Bot(store, handler).ProcessUpdatesAsync(0);

        var prompt = Assert.Single(handler.Calls, c => c.body.Contains("force_reply"));
        Assert.Contains("انبار این محصول خالی است", prompt.body);
        Assert.Contains($"#ACC:{order.Id}:{order.Units[0].Id}:77", prompt.body);
        Assert.False(store.GetOrder(order.Id)!.Units[0].Delivered); // still waiting on the reply
    }

    // ── What the group is told ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_auto_delivered_account_is_posted_as_an_fyi_and_only_the_rest_are_actionable()
    {
        var store = TestStore.Create();
        var order = PaidOrder(store, qty: 2, autoDeliver: true);
        Stock(store, "account-one"); // covers the first account only
        var handler = new BotHandler();

        Fulfillment(store).AutoDeliverOrder(order);
        await Bot(store, handler).NotifyOrderAsync(store.GetOrder(order.Id)!);

        var posts = handler.Calls.Where(c => c.method == "sendMessage").ToList();
        Assert.Equal(2, posts.Count); // both accounts are posted, but they read differently

        // The pool-delivered one is an FYI: the auto-delivery badge, no approve/reject buttons.
        var auto = Assert.Single(posts, c => c.body.Contains("اکانت: 1 از 2"));
        Assert.Contains("تحویل خودکار سرویس انجام شد", auto.body);
        Assert.DoesNotContain("ordr:ok:", auto.body);
        Assert.DoesNotContain("ordr:no:", auto.body);

        // The one the pool could not cover still carries the real decision buttons.
        var actionable = Assert.Single(posts, c => c.body.Contains("اکانت: 2 از 2"));
        Assert.Contains("ordr:ok:", actionable.body);
        Assert.Contains("ordr:no:", actionable.body);

        Assert.True(store.GetOrder(order.Id)!.Units[0].Delivered);
    }

    // ── Every approval path reaches the pool ──────────────────────────────────────────────────────────────

    // The receipt bot and the transactions page both approve the payment through SetTransactionStatus, which
    // is where auto-delivery used to be missed entirely — the order sat in «آماده‌سازی» with a full pool.
    [Fact]
    public void Approving_the_payment_transaction_delivers_pool_enabled_accounts()
    {
        var store = TestStore.Create();
        var product = store.GetProduct(1)!;
        product.AutoDeliverStock = true;
        store.UpdateProduct(product);
        Stock(store, "paid-by-receipt");

        // A real customer checkout with a card-to-card remainder — the only path that files a payment receipt.
        var card = store.AddCard(5, "6037991234567893", "رضا رضایی", "/uploads/card.png").Card!;
        store.SetCardStatus(card.Id, BankCardStatus.Approved, null);
        var planId = store.GetProduct(1)!.Plans.First(p => p.IsActive).Id;
        var placed = store.PlaceOrder(store.GetUser(5)!, new[] { (1, 1, (int?)planId) }, "کارت به کارت", fromWallet: false,
            paymentMethodId: 3, payment: new RemainderPayment(card.Id, "/uploads/r.png", "TRK-1", "1403/03/22", null),
            customerCheckout: true);
        Assert.Null(placed.Error);
        var order = placed.Order!;
        Assert.Equal(OrderStatus.PendingApproval, order.Status);
        var tx = store.GetTransactions().First(t => t.OrderCode == order.Code && t.Type == TxTypes.OrderPayment);

        store.SetTransactionStatus(tx.Id, TxStatus.Approved, "telegram", null);
        Fulfillment(store).AutoDeliverForTransaction(store.GetTransaction(tx.Id)!);

        var settled = store.GetOrder(order.Id)!;
        Assert.True(settled.Units[0].Delivered);
        Assert.Equal("paid-by-receipt", settled.Units[0].DeliveryContent);
        Assert.Equal(OrderStatus.Completed, settled.Status); // its only account is done
    }

    [Fact]
    public void An_empty_pool_leaves_the_account_for_manual_fulfillment()
    {
        var store = TestStore.Create();
        var order = PaidOrder(store, autoDeliver: true); // pool empty

        Fulfillment(store).AutoDeliverOrder(order);

        var after = store.GetOrder(order.Id)!;
        Assert.False(after.Units[0].Delivered);
        Assert.Equal(OrderStatus.Preparing, after.Status); // clean degrade, not a failure
    }

    [Fact]
    public void A_product_that_did_not_opt_in_is_not_auto_delivered()
    {
        var store = TestStore.Create();
        var order = PaidOrder(store); // AutoDeliverStock off
        Stock(store, "should-stay-put");

        Fulfillment(store).AutoDeliverOrder(order);

        Assert.False(store.GetOrder(order.Id)!.Units[0].Delivered);
        Assert.Equal(StockItemStatus.Available, store.GetStockItems(1).Single().Status);
    }

    // ── Reserved items ────────────────────────────────────────────────────────────────────────────────────

    // An item pulled for a unit but never delivered (an admin opened the deliver modal and walked away) must be
    // reused by that same unit, not burned a second time.
    [Fact]
    public void A_reserved_item_is_reused_by_its_own_unit_instead_of_burning_another()
    {
        var store = TestStore.Create();
        var order = PaidOrder(store);
        Stock(store, "first", "second");
        var reserved = store.PullStockItem(1, order.Id, order.Units[0].Id)!; // the panel's «برداشت از انبار»
        Assert.Equal(StockItemStatus.Reserved, store.GetStockItem(reserved.Id)!.Status);

        var served = Fulfillment(store).ServeUnit(order, order.Units[0], "انبار مجازی");

        Assert.NotNull(served);
        Assert.Equal("first", served!.Value.order.Units[0].DeliveryContent);
        Assert.Equal(StockItemStatus.Delivered, store.GetStockItem(reserved.Id)!.Status);
        Assert.Equal(StockItemStatus.Available, store.GetStockItems(1).Single(s => s.Id != reserved.Id).Status);
    }

    // If the store refuses the delivery, the account must go back in the pool rather than be lost as Reserved.
    [Fact]
    public void An_item_pulled_for_a_delivery_the_store_refuses_goes_back_to_the_pool()
    {
        var store = TestStore.Create();
        var order = PaidOrder(store);
        Stock(store, "released");
        var ghost = new Order { Id = 99999, Code = "GONE" }; // no such order → DeliverUnit refuses

        var served = Fulfillment(store).ServeUnit(ghost, order.Units[0], "انبار مجازی");

        Assert.Null(served);
        var item = store.GetStockItems(1).Single();
        Assert.Equal(StockItemStatus.Available, item.Status);
        Assert.Null(item.OrderId);
    }

    // A rejected account has already been refunded; the pool must never hand it an item afterwards.
    [Fact]
    public void A_rejected_account_is_never_served_from_the_pool()
    {
        var store = TestStore.Create();
        var order = PaidOrder(store, qty: 2, autoDeliver: true);
        store.RejectUnit(order.Id, order.Units[0].Id, "موجود نبود", "telegram");
        Stock(store, "only-one");

        Fulfillment(store).AutoDeliverOrder(store.GetOrder(order.Id)!);

        var after = store.GetOrder(order.Id)!;
        Assert.True(after.Units[0].Rejected);
        Assert.False(after.Units[0].Delivered);
        Assert.True(after.Units[1].Delivered);           // the item went to the account still waiting
        Assert.Equal("only-one", after.Units[1].DeliveryContent);
    }
}

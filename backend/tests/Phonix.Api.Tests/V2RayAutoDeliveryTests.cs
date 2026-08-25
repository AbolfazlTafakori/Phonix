using Microsoft.Extensions.Logging.Abstractions;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Services;
using Xunit;

namespace Phonix.Api.Tests;

// A V2Ray purchase fulfils itself: the payment is approved, the system creates the account on the panel, and
// the buyer has their service without anyone in the orders group touching it.
//
// What the group gets is the point of these tests. Nobody there CAN fulfil one of these — the account is made
// by calling a panel — so an approve/reject prompt would be asking for something impossible, and it would be
// wrong within the minute anyway. The account is held back until the service exists and then posted once, as
// a delivery notice. The only time it asks for a person is when the panel could not be made to answer at all.
public class V2RayAutoDeliveryTests
{
    private const string Token = "12345:AA";
    private const string Chat = "-1001";

    // ── Fakes ─────────────────────────────────────────────────────────────────────────────────────────────

    private sealed class BotHandler : HttpMessageHandler
    {
        public List<(string method, string body)> Calls = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var method = request.RequestUri!.AbsolutePath.Split('/').Last();
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Calls.Add((method, Uri.UnescapeDataString(body.Replace('+', ' '))));
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true,\"result\":{}}"),
            };
        }

        public List<string> Posts => Calls.Where(c => c.method == "sendMessage").Select(c => c.body).ToList();
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
        public Task SeatInfoRejectedAsync(SeatSubmission submission) => Task.CompletedTask;
    }

    // Hands the fulfillment service the one bot these tests are watching.
    private sealed class BotProvider : IServiceProvider
    {
        private readonly ITelegramOrderService _bot;
        public BotProvider(ITelegramOrderService bot) => _bot = bot;
        public object? GetService(Type serviceType) =>
            serviceType == typeof(ITelegramOrderService) ? _bot : null;
    }

    // A panel that refuses every account, so the give-up path can be walked without waiting on a real server.
    private static FakeV2RayPanel DeadPanel() =>
        new() { AddResult = new V2RayClientResult(false, "پنل در دسترس نیست", "", "", 0) };

    // ── Fixture ───────────────────────────────────────────────────────────────────────────────────────────

    // A shop with one self-provisioning V2Ray product, and the orders bot switched on.
    private static (IDataStore store, int productId, int planId) Seed()
    {
        var store = TestStore.Create();
        store.UpdateTelegramSettings(new TelegramSettings
        {
            OrderBotEnabled = true, OrderBotToken = Token, OrderChatId = Chat,
        });
        var category = store.AddV2RayCategory(new V2RayCategory { Name = "ماهانه", Active = true });
        store.AddV2RayPanel(new V2RayPanel
        {
            Url = "https://nl.example.com:8080", Name = "هلند", Flag = "NL",
            SubDomain = "sub.example.com", SubPath = "sub", SubHttps = true,
        });
        var plan = store.AddV2RayPlan(new V2RayPlan
        {
            CategoryId = category.Id, Title = "۲۰ گیگ", PanelId = 1, InboundIds = new() { 1 },
            Protocol = "vless", Network = "ws",
            VolumeGb = 20, DurationDays = 30, IpLimit = 2, Price = 300_000, Active = true,
        });
        var product = store.AddProduct(new Product
        {
            Name = "خرید اشتراک V2Ray", CategoryId = 1, IsActive = true,
            V2RayCategoryId = category.Id, Plans = new(),
        });
        return (store, product.Id, plan.Id);
    }

    private static (TelegramOrderService bot, V2RayFulfillmentService fulfil) Wire(
        IDataStore store, BotHandler handler, IV2RayPanelConnector panel)
    {
        // Built in this order because each needs the other: the bot asks the fulfillment service what
        // self-provisions, and the fulfillment service resolves the bot to post with. In the app the same
        // knot is tied by the container.
        V2RayFulfillmentService? fulfil = null;
        var bot = new TelegramOrderService(store, new NoopMailer(),
            new StockFulfillmentService(store, NullLogger<StockFulfillmentService>.Instance),
            new LazyFulfil(() => fulfil!), new StubFactory(handler), NullLogger<TelegramOrderService>.Instance);
        fulfil = new V2RayFulfillmentService(store, panel, new BotProvider(bot),
            NullLogger<V2RayFulfillmentService>.Instance);
        return (bot, fulfil);
    }

    private sealed class LazyFulfil : IV2RayFulfillmentService
    {
        private readonly Func<IV2RayFulfillmentService> _inner;
        public LazyFulfil(Func<IV2RayFulfillmentService> inner) => _inner = inner;
        public bool Handles(OrderUnit unit) => _inner().Handles(unit);
        public Task<bool> ProvisionAsync(Order o, OrderUnit u, CancellationToken ct = default) => _inner().ProvisionAsync(o, u, ct);
        public Task ProvisionOrderAsync(Order o, CancellationToken ct = default) => _inner().ProvisionOrderAsync(o, ct);
        public Task ProvisionForTransactionAsync(Transaction t, CancellationToken ct = default) => _inner().ProvisionForTransactionAsync(t, ct);
    }

    private static Order Buy(IDataStore store, int productId, int planId) =>
        store.PlaceOrder(store.GetUser(5)!, new[] { (productId, 1, (int?)planId) }, "wallet", fromWallet: true).Order!;

    // ── The group is never asked to fulfil what it cannot ─────────────────────────────────────────────────

    [Fact]
    public async Task An_unprovisioned_service_is_held_back_from_the_group()
    {
        var (store, productId, planId) = Seed();
        var order = Buy(store, productId, planId);
        var handler = new BotHandler();
        var (bot, _) = Wire(store, handler, new FakeV2RayPanel());

        await bot.NotifyOrderAsync(store.GetOrder(order.Id)!);

        // Nothing at all: the account does not exist yet, and there is no decision for anyone to make.
        Assert.Empty(handler.Posts);
    }

    [Fact]
    public async Task Once_the_service_exists_the_group_is_told_it_was_delivered()
    {
        var (store, productId, planId) = Seed();
        var order = Buy(store, productId, planId);
        var handler = new BotHandler();
        var (_, fulfil) = Wire(store, handler, new FakeV2RayPanel());

        await fulfil.ProvisionOrderAsync(order);

        var post = Assert.Single(handler.Posts);
        Assert.Contains("سرویس ساخته و تحویل شد", post);
        // A delivery notice, not a work item: neither decision button is on it.
        Assert.DoesNotContain("ordr:ok:", post);
        Assert.DoesNotContain("ordr:no:", post);
        Assert.True(store.GetOrder(order.Id)!.Units[0].Delivered);
    }

    // The announce and the provisioning run on their own schedules and both want to post this account. The
    // claim is what stops the group getting the same service twice.
    [Fact]
    public async Task A_service_is_posted_exactly_once_however_many_paths_reach_it()
    {
        var (store, productId, planId) = Seed();
        var order = Buy(store, productId, planId);
        var handler = new BotHandler();
        var (bot, fulfil) = Wire(store, handler, new FakeV2RayPanel());

        await fulfil.ProvisionOrderAsync(order);                       // posts the delivery notice
        await bot.NotifyOrderAsync(store.GetOrder(order.Id)!);         // the announce arrives late
        await fulfil.ProvisionOrderAsync(store.GetOrder(order.Id)!);   // and the worker sweeps again

        Assert.Single(handler.Posts);
    }

    // ── Everything that is NOT V2Ray keeps the old flow ──────────────────────────────────────────────────

    // The gate is one condition — the product is linked to a V2Ray category — and nothing else in the shop
    // matches it. An ordinary product is announced to the group with real decision buttons, exactly as before.
    [Fact]
    public async Task An_ordinary_service_still_goes_to_the_group_for_a_person()
    {
        var (store, _, _) = Seed();
        // Product 1 of the fixture shop: a ready-made account, no V2Ray category, nothing self-provisioning.
        var order = store.PlaceOrder(store.GetUser(5)!, new[] { (1, 1, (int?)null) }, "wallet", fromWallet: true).Order!;
        var handler = new BotHandler();
        var (bot, _) = Wire(store, handler, new FakeV2RayPanel());

        await bot.NotifyOrderAsync(store.GetOrder(order.Id)!);

        var post = Assert.Single(handler.Posts);
        Assert.Contains("ordr:ok:", post);   // ✅ تأیید — still a decision for staff
        Assert.Contains("ordr:no:", post);   // ❌ رد
        Assert.DoesNotContain("سرویس ساخته و تحویل شد", post);
    }

    // The decisive one: both kinds in a SINGLE order. The V2Ray service is held back and the ordinary account
    // is handed to the group untouched — the change is scoped to the product, not to the order it arrived in.
    [Fact]
    public async Task In_a_mixed_order_only_the_v2ray_service_is_held_back()
    {
        var (store, productId, planId) = Seed();
        var order = store.PlaceOrder(store.GetUser(5)!,
            new[] { (productId, 1, (int?)planId), (1, 1, (int?)null) }, "wallet", fromWallet: true).Order!;
        Assert.Equal(2, order.Units.Count);
        var handler = new BotHandler();
        var (bot, fulfil) = Wire(store, handler, new FakeV2RayPanel());

        await bot.NotifyOrderAsync(store.GetOrder(order.Id)!);

        // Only the ordinary account is posted, and it is posted the way it always was.
        var manual = Assert.Single(handler.Posts);
        Assert.Contains("ordr:ok:", manual);

        // The V2Ray one arrives on its own, once the panel has actually made it.
        await fulfil.ProvisionOrderAsync(store.GetOrder(order.Id)!);
        Assert.Equal(2, handler.Posts.Count);
        Assert.Contains("سرویس ساخته و تحویل شد", handler.Posts[1]);
        Assert.DoesNotContain("ordr:ok:", handler.Posts[1]);
    }

    // Provisioning is just as narrow: asked to fulfil an order of ordinary accounts, it does nothing at all
    // rather than touching a panel that has nothing to do with them.
    [Fact]
    public async Task Provisioning_leaves_ordinary_services_alone()
    {
        var (store, _, _) = Seed();
        var order = store.PlaceOrder(store.GetUser(5)!, new[] { (1, 1, (int?)null) }, "wallet", fromWallet: true).Order!;
        var handler = new BotHandler();
        var (_, fulfil) = Wire(store, handler, DeadPanel());   // would fail loudly if it were ever called

        await fulfil.ProvisionOrderAsync(order);

        var unit = store.GetOrder(order.Id)!.Units[0];
        Assert.False(unit.Delivered);
        Assert.Null(unit.V2Ray);        // no provisioning record was written
        Assert.Empty(handler.Posts);    // and the group was told nothing
    }

    // ── When it cannot be done automatically, a person is asked ───────────────────────────────────────────

    [Fact]
    public async Task A_service_the_panel_never_accepts_is_handed_to_the_group_once()
    {
        var (store, productId, planId) = Seed();
        var order = Buy(store, productId, planId);
        var handler = new BotHandler();
        var (_, fulfil) = Wire(store, handler, DeadPanel());

        // Every retry the worker would make, in one go.
        for (var i = 0; i < V2RayFulfillmentService.MaxAttempts + 3; i++)
            await fulfil.ProvisionOrderAsync(store.GetOrder(order.Id)!);

        var post = Assert.Single(handler.Posts);
        Assert.Contains("ساخت خودکار سرویس ناموفق بود", post);
        Assert.Contains("پنل در دسترس نیست", post);   // the panel's own words, so staff can act on them
        Assert.False(store.GetOrder(order.Id)!.Units[0].Delivered);
    }

    // Silence while it is still being retried: a service that is one failed attempt in is not staff's problem
    // yet, and saying so every 45 seconds would bury the group.
    [Fact]
    public async Task A_service_still_being_retried_says_nothing()
    {
        var (store, productId, planId) = Seed();
        var order = Buy(store, productId, planId);
        var handler = new BotHandler();
        var (_, fulfil) = Wire(store, handler, DeadPanel());

        await fulfil.ProvisionOrderAsync(order);
        await fulfil.ProvisionOrderAsync(store.GetOrder(order.Id)!);

        Assert.Empty(handler.Posts);
    }

    // ── The path a real purchase takes ────────────────────────────────────────────────────────────────────

    // The whole point: approving the receipt is the last human step. What follows — the account on the panel,
    // the delivery to the customer, the line in the orders channel — happens without anyone being asked.
    [Fact]
    public async Task Approving_the_receipt_builds_and_delivers_the_service()
    {
        var (store, productId, planId) = Seed();
        var card = store.AddCard(5, "6037991234567893", "رضا رضایی", "/uploads/card.png").Card!;
        store.SetCardStatus(card.Id, BankCardStatus.Approved, null);
        var placed = store.PlaceOrder(store.GetUser(5)!, new[] { (productId, 1, (int?)planId) }, "کارت به کارت",
            fromWallet: false, paymentMethodId: 3,
            payment: new RemainderPayment(card.Id, "/uploads/r.png", "TRK-1", "1403/03/22", null),
            customerCheckout: true);
        Assert.Null(placed.Error);
        Assert.Equal(OrderStatus.PendingApproval, placed.Order!.Status);   // waiting on the receipt

        var handler = new BotHandler();
        var (_, fulfil) = Wire(store, handler, new FakeV2RayPanel());
        var tx = store.GetTransactions().First(t => t.OrderCode == placed.Order.Code && t.Type == TxTypes.OrderPayment);

        store.SetTransactionStatus(tx.Id, TxStatus.Approved, "telegram", null);
        await fulfil.ProvisionForTransactionAsync(store.GetTransaction(tx.Id)!);

        var settled = store.GetOrder(placed.Order.Id)!;
        Assert.True(settled.Units[0].Delivered);
        Assert.Equal("uuid-0001", settled.Units[0].V2Ray!.Uuid);          // the account really was created
        Assert.Equal(OrderStatus.Completed, settled.Status);              // its only service is done
        Assert.Contains("سرویس ساخته و تحویل شد", Assert.Single(handler.Posts));
    }

    // An order that is still waiting for its receipt must not provision anything: the customer has not paid.
    [Fact]
    public async Task An_unapproved_payment_provisions_nothing()
    {
        var (store, productId, planId) = Seed();
        var card = store.AddCard(5, "6037991234567893", "رضا رضایی", "/uploads/card.png").Card!;
        store.SetCardStatus(card.Id, BankCardStatus.Approved, null);
        var placed = store.PlaceOrder(store.GetUser(5)!, new[] { (productId, 1, (int?)planId) }, "کارت به کارت",
            fromWallet: false, paymentMethodId: 3,
            payment: new RemainderPayment(card.Id, "/uploads/r.png", "TRK-2", "1403/03/22", null),
            customerCheckout: true);
        var handler = new BotHandler();
        var (_, fulfil) = Wire(store, handler, new FakeV2RayPanel());
        var tx = store.GetTransactions().First(t => t.OrderCode == placed.Order!.Code && t.Type == TxTypes.OrderPayment);

        await fulfil.ProvisionForTransactionAsync(tx);   // still Pending

        Assert.False(store.GetOrder(placed.Order!.Id)!.Units[0].Delivered);
        Assert.Empty(handler.Posts);
    }
}

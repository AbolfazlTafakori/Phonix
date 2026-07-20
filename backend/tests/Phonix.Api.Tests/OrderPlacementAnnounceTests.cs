using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Phonix.Api.Controllers;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;
using Phonix.Api.Services;
using Xunit;

namespace Phonix.Api.Tests;

// A fully wallet-paid order needs no receipt approval, so checkout itself is the only place its accounts can
// reach the orders group. This guards that OrdersController.Place announces them right then — NOT only later
// when the buyer happens to open «سفارش‌های من» (which is a pure read and must never trigger a send).
public class OrderPlacementAnnounceTests
{
    private const string Token = "123456:test-order-token";
    private const string Chat = "-1001234567890";

    // Records every Telegram call so the test can see whether the announce actually went out.
    private sealed class BotHandler : HttpMessageHandler
    {
        public List<(string method, string body)> Calls = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var method = request.RequestUri!.AbsolutePath.Split('/').Last();
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            lock (Calls) Calls.Add((method, Uri.UnescapeDataString(body.Replace('+', ' '))));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true,\"result\":true}") };
        }
    }

    private sealed class StubFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _h;
        public StubFactory(HttpMessageHandler h) => _h = h;
        public HttpClient CreateClient(string name) => new(_h, disposeHandler: false);
    }

    private sealed class NoopEmail : IEmailSender
    {
        public Task<bool> SendAsync(string to, string subject, string body, string? htmlBody = null) => Task.FromResult(true);
    }

    private sealed class NoopReceiptBot : ITelegramReceiptService
    {
        public Task NotifyDepositAsync(Transaction tx, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> ProcessUpdatesAsync(long offset, CancellationToken ct = default) => Task.FromResult(offset);
        public Task<(bool ok, string? error)> SendTestAsync(CancellationToken ct = default) => Task.FromResult((true, (string?)null));
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

    private static OrdersController Controller(IDataStore store, BotHandler handler, int userId)
    {
        store.UpdateTelegramSettings(new TelegramSettings { OrderBotEnabled = true, OrderBotToken = Token, OrderChatId = Chat });
        var stock = new StockFulfillmentService(store, NullLogger<StockFulfillmentService>.Instance);
        var orderBot = new TelegramOrderService(store, new NoopMailer(), stock, new StubFactory(handler),
            NullLogger<TelegramOrderService>.Instance);
        var controller = new OrdersController(store, new NoopEmail(), new NoopReceiptBot(), orderBot, stock, new NoopMailer())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "test")),
                },
            },
        };
        return controller;
    }

    // The announce is fire-and-forget, so give the detached task a moment to reach the fake Telegram.
    private static async Task<bool> WaitForSend(BotHandler handler)
    {
        for (var i = 0; i < 40; i++)
        {
            lock (handler.Calls)
                if (handler.Calls.Any(c => c.method == "sendMessage")) return true;
            await Task.Delay(50);
        }
        return false;
    }

    [Fact]
    public async Task A_fully_wallet_paid_order_is_announced_at_checkout()
    {
        var store = TestStore.Create();
        store.UpdateUser(5, u => { u.EmailVerified = true; u.Wallet = 100_000_000; u.VerificationLevel = 2; });
        // A manual (non-auto-deliver), plan-less product so the unit stays pending and must be announced.
        var product = store.AddProduct(new Product
        {
            Name = "Manual", CategoryId = 1, Price = 50_000, Stock = 10, RequiredLevel = 1, IsActive = true,
        });

        var handler = new BotHandler();
        var controller = Controller(store, handler, userId: 5);
        var result = controller.Place(new PlaceOrderInput(
            new List<OrderLineInput> { new(product.Id, 1, null) },
            PaymentMethod: "کیف پول", FromWallet: true, DiscountCode: null, PaymentMethodId: null,
            CardId: null, ReceiptUrl: null, TrackingNumber: null, PaymentDate: null, Description: null));

        Assert.NotNull(result.Value);
        Assert.Equal(OrderStatus.Preparing, result.Value!.Status); // wallet covered it → no receipt approval

        Assert.True(await WaitForSend(handler),
            "A wallet-paid order must be announced to the orders group at checkout, not only when «سفارش‌های من» is opened.");
    }
}

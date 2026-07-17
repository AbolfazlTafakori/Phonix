using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Phonix.Api.Models;
using Phonix.Api.Services;
using Xunit;
using Xunit.Abstractions;

namespace Phonix.Api.Tests;

// The receipt send is fire-and-forget and only logs on failure, so a break here is invisible in production.
// These drive NotifyDepositAsync against a fake Telegram and assert the call is actually attempted.
public class ReceiptSendTests
{
    private readonly ITestOutputHelper _out;
    public ReceiptSendTests(ITestOutputHelper output) => _out = output;

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<(string url, string body)> Calls = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Calls.Add((request.RequestUri!.ToString(), body));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") };
        }
    }

    private sealed class StubFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubFactory(HttpMessageHandler h) => _handler = h;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class NoopOrderBot : ITelegramOrderService
    {
        public Task NotifyOrderAsync(Order order, CancellationToken ct = default) => Task.CompletedTask;
        public Task AnnounceApprovedOrderAsync(Transaction tx, CancellationToken ct = default) => Task.CompletedTask;
        public Task<(bool ok, string? error)> SendTestAsync(CancellationToken ct = default) => Task.FromResult((true, (string?)null));
        public Task<long> ProcessUpdatesAsync(long offset, CancellationToken ct = default) => Task.FromResult(offset);
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

    [Fact]
    public async Task A_configured_receipt_bot_actually_sends_the_deposit_to_the_group()
    {
        var store = TestStore.Create();
        store.UpdateTelegramSettings(new TelegramSettings
        {
            ReceiptBotEnabled = true,
            ReceiptBotToken = "123456:test-receipt-token",
            ReceiptChatId = "-1001234567890",
        });

        var handler = new CapturingHandler();
        var svc = new TelegramReceiptService(store, null!, new NoopMailer(), new NoopOrderBot(),
            new StubFactory(handler), NullLogger<TelegramReceiptService>.Instance);

        // No receipt image → the text path, which is what a wallet top-up without a photo uses.
        var tx = store.AddTransaction(new Transaction
        {
            UserId = 1, Type = TxTypes.WalletTopUp, Amount = 500_000, Status = TxStatus.Pending,
            SourceCard = "6037991234567893", SourceHolder = "علی محمدی", TrackingNumber = "999",
        });

        await svc.NotifyDepositAsync(tx);

        foreach (var (url, body) in handler.Calls)
            _out.WriteLine($"CALL {url}\n{body}\n");

        var call = Assert.Single(handler.Calls);
        Assert.Contains("/bot123456:test-receipt-token/sendMessage", call.url);
        Assert.Contains("-1001234567890", call.body);
    }

    [Fact]
    public async Task A_disabled_receipt_bot_sends_nothing()
    {
        var store = TestStore.Create();
        var handler = new CapturingHandler();
        var svc = new TelegramReceiptService(store, null!, new NoopMailer(), new NoopOrderBot(),
            new StubFactory(handler), NullLogger<TelegramReceiptService>.Instance);

        var tx = store.AddTransaction(new Transaction { UserId = 1, Type = TxTypes.WalletTopUp, Amount = 1, Status = TxStatus.Pending });
        await svc.NotifyDepositAsync(tx);

        Assert.Empty(handler.Calls);
    }
}

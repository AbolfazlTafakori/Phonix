using Microsoft.Extensions.Logging.Abstractions;
using Phonix.Api.Controllers;
using Phonix.Api.Models;
using Phonix.Api.Services;
using Xunit;

namespace Phonix.Api.Tests;

// A staff decision on a receipt must reach the customer. Rejection used to be silent — no email and no in-app
// notification — so the buyer only saw money that never arrived. These lock that shut, and pin the two things
// that are easy to regress: the reason must travel with the rejection, and a repeated decision must not mail twice.
public class TransactionEmailTests
{
    private sealed class CapturingSender : IEmailSender
    {
        public List<(string to, string subject, string body)> Sent = new();
        public Task<bool> SendAsync(string to, string subject, string body, string? htmlBody = null)
        {
            Sent.Add((to, subject, body));
            return Task.FromResult(true);
        }
    }

    [Fact]
    public async Task Rejecting_a_deposit_emails_the_owner_the_reason_once()
    {
        var store = TestStore.Create();
        var sender = new CapturingSender();
        var mailer = new UserMailer(store, sender, NullLogger<UserMailer>.Instance);
        var controller = new TransactionsController(store, null!, new NoopReceiptBot(), new NoopOrderBot(), mailer);

        var tx = store.AddTransaction(new Transaction
        {
            UserId = 1, Type = "شارژ کیف پول", Amount = 500_000, Status = TxStatus.Pending,
        });

        controller.Reject(tx.Id, new TxActionInput("شماره پیگیری با رسید مطابقت ندارد."));
        await Task.Delay(200); // the send is fire-and-forget

        var mail = Assert.Single(sender.Sent);
        Assert.Equal(store.GetUser(1)!.Email, mail.to);
        Assert.Contains("تأیید نشد", mail.subject);
        Assert.Contains("شماره پیگیری با رسید مطابقت ندارد.", mail.body);

        // a repeated reject on an already-decided transaction must not mail again
        controller.Reject(tx.Id, new TxActionInput("دوباره"));
        await Task.Delay(200);
        Assert.Single(sender.Sent);
    }

    [Fact]
    public async Task Approving_a_topup_emails_the_new_balance()
    {
        var store = TestStore.Create();
        var sender = new CapturingSender();
        var mailer = new UserMailer(store, sender, NullLogger<UserMailer>.Instance);
        var controller = new TransactionsController(store, null!, new NoopReceiptBot(), new NoopOrderBot(), mailer);

        var tx = store.AddTransaction(new Transaction
        {
            UserId = 1, Type = "شارژ کیف پول", Amount = 500_000, Status = TxStatus.Pending,
        });
        controller.Approve(tx.Id, null);
        await Task.Delay(200);

        var mail = Assert.Single(sender.Sent);
        Assert.Contains("کیف پول", mail.subject);
        // the balance quoted must be the post-credit balance the store actually holds
        Assert.Contains(JalaliDate.ToPersianDigits(store.GetUser(1)!.Wallet.ToString("N0")), mail.body);
    }

    [Fact]
    public async Task Each_delivered_account_mails_separately_and_completion_mails_the_full_list()
    {
        var store = TestStore.Create();
        var sender = new CapturingSender();
        var mailer = new UserMailer(store, sender, NullLogger<UserMailer>.Instance);

        // Two accounts of one product → two independent units to deliver.
        var order = store.PlaceOrder(store.GetUser(1)!, new[] { (1, 2, (int?)null) }, "کارت", fromWallet: false).Order!;
        Assert.Equal(2, order.Units.Count);

        // First account delivered: its own mail, naming which account it is. The order is not complete yet.
        var (afterFirst, firstCompleted) = store.DeliverUnit(order.Id, order.Units[0].Id, "اکانت اول");
        Assert.False(firstCompleted);
        await mailer.OrderUnitDeliveredAsync(afterFirst!, order.Units[0].Id);
        var first = Assert.Single(sender.Sent);
        Assert.Contains("اکانت اول", first.body);
        Assert.Contains("۱ از ۲", JalaliDate.ToPersianDigits(first.body));

        // Second account completes the order → its own mail, plus one summary listing the whole order.
        var (afterSecond, nowCompleted) = store.DeliverUnit(order.Id, order.Units[1].Id, "اکانت دوم");
        Assert.True(nowCompleted);
        await mailer.OrderUnitDeliveredAsync(afterSecond!, order.Units[1].Id);
        await mailer.OrderCompletedAsync(afterSecond!);

        Assert.Equal(3, sender.Sent.Count);
        Assert.Contains("اکانت دوم", sender.Sent[1].body);

        var summary = sender.Sent[2];
        Assert.Contains("تکمیل شد", summary.subject);
        Assert.Contains(afterSecond!.Items[0].Name, summary.body);       // the exact list
        Assert.Contains(afterSecond.InvoiceNumber!, summary.body);       // and the invoice issued on completion
    }

    private sealed class NoopReceiptBot : ITelegramReceiptService
    {
        public Task NotifyDepositAsync(Transaction tx, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> ProcessUpdatesAsync(long offset, CancellationToken ct = default) => Task.FromResult(offset);
    }

    private sealed class NoopOrderBot : ITelegramOrderService
    {
        public Task NotifyOrderAsync(Order order, CancellationToken ct = default) => Task.CompletedTask;
        public Task AnnounceApprovedOrderAsync(Transaction tx, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> ProcessUpdatesAsync(long offset, CancellationToken ct = default) => Task.FromResult(offset);
    }
}

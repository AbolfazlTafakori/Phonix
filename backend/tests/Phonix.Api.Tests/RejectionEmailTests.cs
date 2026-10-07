using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Phonix.Api.Controllers;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Services;
using Xunit;

namespace Phonix.Api.Tests;

// When staff turn an order down, the reason they type has to reach the customer in full — in the bell and by
// email. Before this it only went into the order's internal history.
public class RejectionEmailTests
{
    private sealed class Outbox : IEmailSender
    {
        public List<(string To, string Subject, string Text, string? Html)> Sent { get; } = new();
        public Task<bool> SendAsync(string to, string subject, string body, string? htmlBody = null)
        {
            Sent.Add((to, subject, body, htmlBody));
            return Task.FromResult(true);
        }
    }

    private sealed class NoopReceiptBot : ITelegramReceiptService
    {
        public Task NotifyDepositAsync(Transaction tx, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyCardAsync(BankCard card, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyKycAsync(KycRequest kyc, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> ProcessUpdatesAsync(long offset, CancellationToken ct = default) => Task.FromResult(offset);
        public Task<(bool ok, string? error)> SendTestAsync(CancellationToken ct = default) => Task.FromResult((true, (string?)null));
        public Task ShowTransactionDecisionAsync(Transaction tx, CancellationToken ct = default) => Task.CompletedTask;
        public Task ShowCardDecisionAsync(BankCard card, CancellationToken ct = default) => Task.CompletedTask;
        public Task ShowKycDecisionAsync(KycRequest kyc, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoopOrderBot : ITelegramOrderService
    {
        public Task NotifyOrderAsync(Order order, CancellationToken ct = default) => Task.CompletedTask;
        public Task AnnounceApprovedOrderAsync(Transaction tx, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyUnitAsync(Order order, OrderUnit unit, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> ProcessUpdatesAsync(long offset, CancellationToken ct = default) => Task.FromResult(offset);
        public Task<(bool ok, string? error)> SendTestAsync(CancellationToken ct = default) => Task.FromResult((true, (string?)null));
        public Task ShowUnitDecisionAsync(Order order, int unitId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private const string Reason = "رسید ارسالی ناخواناست و شماره پیگیری با واریزی ما مطابقت ندارد.\nلطفاً رسید واضح‌تری بفرستید یا با پشتیبانی تماس بگیرید.";

    private static ClaimsPrincipal As(AppUser u) => new(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.NameIdentifier, u.Id.ToString()),
        new Claim(ClaimTypes.Role, u.Role.ToString()),
        new Claim(ClaimTypes.Name, u.Username),
    }, "test"));

    private static OrdersController Orders(IDataStore store, Outbox outbox, AppUser caller) =>
        new(store, outbox, new NoopReceiptBot(), new NoopOrderBot(),
            new StockFulfillmentService(store, NullLogger<StockFulfillmentService>.Instance),
            new UserMailer(store, outbox, NullLogger<UserMailer>.Instance), new LocalFileStorageService(), TestPriceLock.Create())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = As(caller) } },
        };

    private static AppUser Admin(IDataStore s) => s.GetUsers().First(u => u.Role == UserRole.Admin);
    private static AppUser Buyer(IDataStore s) => s.GetUser(1)!; // ali, a customer with an email on file

    private static Order Place(IDataStore s) =>
        s.PlaceOrder(Buyer(s), new[] { (1, 1, (int?)null) }, "کارت به کارت", fromWallet: false).Order!;

    [Fact]
    public void Rejecting_a_receipt_emails_the_whole_reason_and_puts_it_in_the_bell()
    {
        var store = TestStore.Create();
        var outbox = new Outbox();
        var order = Place(store);

        Orders(store, outbox, Admin(store)).Reject(order.Id, new RejectOrderInput(Reason));

        var mail = Assert.Single(outbox.Sent, m => m.To == Buyer(store).Email);
        Assert.Contains(order.Code, mail.Subject);
        Assert.Contains(Reason, mail.Text);                       // every line, as typed
        Assert.Contains("ناخواناست", mail.Html);
        Assert.Contains("<br>لطفاً رسید واضح‌تری", mail.Html);    // line break kept in the HTML
        Assert.Contains(store.GetUserNotifications(Buyer(store).Id), n => n.Body.Contains("ناخواناست"));
    }

    [Fact]
    public void Cancelling_from_the_panel_emails_the_reason()
    {
        var store = TestStore.Create();
        var outbox = new Outbox();
        var order = Place(store);

        Orders(store, outbox, Admin(store)).Cancel(order.Id, new CancelOrderInput("این سرویس فعلاً موجود نیست."));

        Assert.Contains(outbox.Sent, m => m.To == Buyer(store).Email && m.Text.Contains("این سرویس فعلاً موجود نیست."));
    }

    [Fact]
    public void A_customer_cancelling_their_own_order_gets_no_email()
    {
        var store = TestStore.Create();
        var outbox = new Outbox();
        var order = Place(store);

        Orders(store, outbox, Buyer(store)).Cancel(order.Id, null);

        Assert.DoesNotContain(outbox.Sent, m => m.Subject.Contains("لغو شد"));
    }

    [Fact]
    public void Rejecting_one_account_emails_its_reason_and_refund()
    {
        var store = TestStore.Create();
        var outbox = new Outbox();
        var order = store.SetOrderStatus(Place(store).Id, OrderStatus.Preparing)!;
        var unit = order.Units.First();

        Orders(store, outbox, Admin(store)).RejectUnit(order.Id, unit.Id, new RejectUnitInput("ظرفیت این پلن تمام شده است."));

        var mail = Assert.Single(outbox.Sent, m => m.Subject.Contains("رد شد"));
        Assert.Equal(Buyer(store).Email, mail.To);
        Assert.Contains("ظرفیت این پلن تمام شده است.", mail.Text);
        Assert.Contains("کیف پول", mail.Text);
        Assert.Contains(store.GetUserNotifications(Buyer(store).Id), n => n.Body.Contains("دلیل: ظرفیت این پلن تمام شده است."));
    }

    [Fact]
    public void A_reason_is_escaped_not_rendered()
    {
        var (_, html) = EmailTemplates.OrderCancelled("PX-1", "<img src=x onerror=alert(1)>", false, "https://shop/account/orders");

        Assert.DoesNotContain("<img src=x", html);
        Assert.Contains("&lt;img src=x", html);
    }
}

// Messages staff send from the notifications section also go out by email.
public class StaffMessageEmailTests
{
    private sealed class Outbox : IEmailSender
    {
        public List<(string To, string Subject, string Text)> Sent { get; } = new();
        public Task<bool> SendAsync(string to, string subject, string body, string? htmlBody = null)
        {
            lock (Sent) Sent.Add((to, subject, body));
            return Task.FromResult(true);
        }
    }

    private static NotificationsController Controller(IDataStore store, Outbox outbox)
    {
        var admin = store.GetUsers().First(u => u.Role == UserRole.Admin);
        return new NotificationsController(store, new UserMailer(store, outbox, NullLogger<UserMailer>.Instance))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, admin.Id.ToString()),
                        new Claim(ClaimTypes.Role, nameof(UserRole.Admin)),
                        new Claim(ClaimTypes.Name, admin.Username),
                    }, "test")),
                },
            },
        };
    }

    [Fact]
    public void A_private_message_is_emailed_to_that_customer_in_full()
    {
        var store = TestStore.Create();
        var outbox = new Outbox();
        var customer = store.GetUser(1)!;

        Controller(store, outbox).Send(new SendNotificationInput(customer.Id, "تمدید اشتراک", "اشتراک شما فردا تمام می‌شود.\nبرای تمدید اقدام کنید.", "/account/orders"));

        var mail = Assert.Single(outbox.Sent);
        Assert.Equal(customer.Email, mail.To);
        Assert.Equal("تمدید اشتراک", mail.Subject);
        Assert.Contains("اشتراک شما فردا تمام می‌شود.\nبرای تمدید اقدام کنید.", mail.Text);
        Assert.Contains("/account/orders", mail.Text);
    }

    [Fact]
    public void Staff_can_send_without_email()
    {
        var store = TestStore.Create();
        var outbox = new Outbox();

        Controller(store, outbox).Send(new SendNotificationInput(1, "فقط داخل سایت", "متن", null, SendEmail: false));

        Assert.Empty(outbox.Sent);
    }

    [Fact]
    public async Task A_broadcast_goes_only_to_verified_unblocked_customers()
    {
        var store = TestStore.Create();
        var customers = store.GetUsers().Where(u => u.Role == UserRole.Customer && u.Email.Length > 0).Take(3).ToList();
        Assert.Equal(3, customers.Count);
        store.UpdateUser(customers[0].Id, u => u.EmailVerified = true);
        store.UpdateUser(customers[1].Id, u => u.EmailVerified = false);
        store.UpdateUser(customers[2].Id, u => { u.EmailVerified = true; u.Blocked = true; });
        foreach (var other in store.GetUsers().Where(u => u.Role == UserRole.Customer).Skip(3))
            store.UpdateUser(other.Id, u => u.EmailVerified = false);
        var outbox = new Outbox();

        var sent = await new UserMailer(store, outbox, NullLogger<UserMailer>.Instance)
            .BroadcastStaffMessageAsync("حراج پاییزه", "تا ۴۰٪ تخفیف", "/products");

        Assert.Equal(1, sent);
        Assert.Equal(customers[0].Email, Assert.Single(outbox.Sent).To);
        Assert.DoesNotContain(outbox.Sent, m => store.GetUsers().Any(u => u.Role != UserRole.Customer && u.Email == m.To));
    }
}

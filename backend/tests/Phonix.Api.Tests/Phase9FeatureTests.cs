using Phonix.Api.Data;
using Phonix.Api.Models;
using Xunit;

namespace Phonix.Api.Tests;

// Covers the phase-9 features: reusable delivery templates, order audit trail, subscription renewal
// reminders, explicit rejection reasons, and instant+durable settings. Seed ref: user 1 = ali (level 2),
// user 6 = negar (level 0), product 1 = Netflix.
public class Phase9FeatureTests
{
    // ── 1) reusable delivery templates ──
    [Fact]
    public void Delivery_templates_add_list_and_delete_with_stable_ids()
    {
        var store = TestStore.Create();
        var t1 = store.AddDeliveryTemplate(1, "قالب اول", "متن یک")!;
        var t2 = store.AddDeliveryTemplate(1, "قالب دوم", "متن دو")!;
        Assert.Equal(2, store.GetDeliveryTemplates(1).Count);

        Assert.True(store.DeleteDeliveryTemplate(1, t1.Id));
        var remaining = store.GetDeliveryTemplates(1);
        Assert.Single(remaining);
        Assert.Equal(t2.Id, remaining[0].Id);                 // surviving id is unchanged by the delete
        Assert.False(store.DeleteDeliveryTemplate(1, 999));    // unknown id
        Assert.Null(store.AddDeliveryTemplate(99999, "x", "y")); // unknown product
    }

    // ── 2) order audit trail ──
    [Fact]
    public void Cancelling_an_order_records_an_audit_entry_with_actor_and_reason()
    {
        var store = TestStore.Create();
        var placed = store.PlaceOrder(store.GetUser(1)!, new[] { (1, 1, (int?)null) }, "کارت", fromWallet: false);

        store.CancelOrder(placed.Order!.Id, "reza", "موجودی نادرست بود");

        var entry = store.GetOrder(placed.Order.Id)!.History.Last();
        Assert.Equal(OrderStatus.PendingApproval, entry.FromStatus);
        Assert.Equal(OrderStatus.Cancelled, entry.ToStatus);
        Assert.Equal("reza", entry.ChangedByUsername);
        Assert.Equal("موجودی نادرست بود", entry.Reason);
        Assert.True(entry.ChangedAtUtc > DateTime.MinValue);
    }

    [Fact]
    public void Delivering_an_order_records_a_completed_transition()
    {
        var store = TestStore.Create();
        var placed = store.PlaceOrder(store.GetUser(1)!, new[] { (1, 1, (int?)null) }, "کارت", fromWallet: false);

        store.DeliverOrder(placed.Order!.Id, "اطلاعات سرویس", "support_user");

        var entry = store.GetOrder(placed.Order.Id)!.History.Last();
        Assert.Equal(OrderStatus.Completed, entry.ToStatus);
        Assert.Equal("support_user", entry.ChangedByUsername);
    }

    // ── 3) subscription renewal reminder worker logic ──
    [Fact]
    public void A_time_based_subscription_is_reminded_once_with_a_bell_notification()
    {
        var store = TestStore.Create();
        var user = store.GetUser(1)!; // level 2 — may buy anything
        var product = store.AddProduct(new Product
        {
            Name = "اشتراک تستی",
            CategoryId = 1,
            Price = 100_000,
            Stock = 10,
            RequiredLevel = 1,
            Plans = new() { new ProductPlan { Type = "ماهانه", Months = 1, Price = 100_000, IsActive = true } },
        });

        var placed = store.PlaceOrder(user, new[] { (product.Id, 1, (int?)1) }, "کارت", fromWallet: false);
        Assert.Null(placed.Error);
        store.DeliverOrder(placed.Order!.Id, "اطلاعات سرویس"); // stamps DeliveredAtUtc + completes

        var unreadBefore = store.CountUnread(user.Id);

        // a huge window guarantees the ~1-month-away expiry falls inside it.
        var due = store.CollectDueRenewalReminders(100_000);
        Assert.Single(due);
        Assert.Equal(placed.Order.Code, due[0].OrderCode);
        Assert.Equal(user.Email, due[0].Email);
        Assert.True(store.CountUnread(user.Id) > unreadBefore); // in-app bell notification fired

        // once-only: the order is now flagged, so a second pass finds nothing.
        Assert.Empty(store.CollectDueRenewalReminders(100_000));
    }

    [Fact]
    public void A_non_subscription_order_is_never_reminded()
    {
        var store = TestStore.Create();
        var placed = store.PlaceOrder(store.GetUser(1)!, new[] { (1, 1, (int?)null) }, "کارت", fromWallet: false);
        store.DeliverOrder(placed.Order!.Id, "x"); // delivered, but no time-based plan
        Assert.Empty(store.CollectDueRenewalReminders(100_000));
    }

    [Fact]
    public void Reminders_are_disabled_when_the_threshold_is_zero()
    {
        var store = TestStore.Create();
        var product = store.AddProduct(new Product
        {
            Name = "اشتراک", CategoryId = 1, Price = 50_000, Stock = 5, RequiredLevel = 1,
            Plans = new() { new ProductPlan { Type = "ماهانه", Months = 1, Price = 50_000, IsActive = true } },
        });
        var placed = store.PlaceOrder(store.GetUser(1)!, new[] { (product.Id, 1, (int?)1) }, "کارت", fromWallet: false);
        store.DeliverOrder(placed.Order!.Id, "x");
        Assert.Empty(store.CollectDueRenewalReminders(0)); // 0 = disabled
    }

    // ── 4) explicit rejection reason ──
    [Fact]
    public void Rejecting_a_card_sets_an_explicit_reason_that_clears_on_approval()
    {
        var store = TestStore.Create();
        var card = store.AddCard(6, "6037991234567893", "نگار شریفی", "/uploads/c.png").Card!;

        store.SetCardStatus(card.Id, BankCardStatus.Rejected, "تصویر کارت ناخوانا است");
        Assert.Equal("تصویر کارت ناخوانا است", store.GetCard(card.Id)!.RejectionReason);

        store.SetCardStatus(card.Id, BankCardStatus.Approved, null);
        Assert.Null(store.GetCard(card.Id)!.RejectionReason); // cleared once approved
    }

    [Fact]
    public void Rejecting_a_kyc_sets_an_explicit_reason()
    {
        var store = TestStore.Create();
        var kyc = store.SubmitKyc(new KycRequest { UserId = 6, FullName = "نگار", NationalId = "001" });
        store.SetKycStatus(kyc.Id, KycStatus.Rejected, "کد ملی با مدرک هم‌خوانی ندارد");
        Assert.Equal("کد ملی با مدرک هم‌خوانی ندارد", store.GetKycForUser(6)!.RejectionReason);
    }

    // ── 5) instant + durable settings ──
    [Fact]
    public void Updating_settings_propagates_instantly_and_persists_to_disk()
    {
        var store = TestStore.Create();
        var settings = store.GetSettings();
        settings.SubscriptionReminderHoursBefore = 72;
        store.UpdateSettings(settings);

        // instant: the next read sees the new value.
        Assert.Equal(72, store.GetSettings().SubscriptionReminderHoursBefore);

        // durable: a brand-new store pointed at the same file (a "restart") reloads it identically.
        var reloaded = new StoreData();
        Assert.Equal(72, reloaded.GetSettings().SubscriptionReminderHoursBefore);
    }
}

using Phonix.Api.Data;
using Phonix.Api.Models;
using Xunit;

namespace Phonix.Api.Tests;

// Per-seat customer submissions: one entry per seat of a shared account, replaced in place on a re-send, and
// frozen once staff review it.
public class SeatSubmissionTests
{
    private static IDataStore NewStore() => TestStore.Create();

    private static SeatSubmission Input(int seatIndex, string text, string? imageId = null) => new()
    {
        UserId = 5,
        OrderId = 1,
        UnitId = 1,
        SeatIndex = seatIndex,
        SeatLabel = $"A - {seatIndex + 1}",
        ProductId = 1,
        ProductName = "Netflix",
        OrderCode = "ORD-1",
        UserName = "reza",
        ImageId = imageId,
        Text = text,
    };

    [Fact]
    public void Every_seat_of_one_purchase_keeps_its_own_submission()
    {
        var store = NewStore();
        // A five-user subscription: each person files their own details for their own profile.
        foreach (var i in Enumerable.Range(0, 5))
            Assert.NotNull(store.SaveSeatSubmission(Input(i, $"seat {i}")));

        var all = store.GetSeatSubmissionsForUnit(1, 1);
        Assert.Equal(5, all.Count);
        Assert.Equal(Enumerable.Range(0, 5), all.Select(s => s.SeatIndex));
        Assert.Equal("seat 3", all.Single(s => s.SeatIndex == 3).Text);
        // One seat's entry is entirely independent of the others.
        Assert.Equal(5, all.Select(s => s.Id).Distinct().Count());
    }

    [Fact]
    public void Re_sending_a_seat_replaces_that_seat_instead_of_piling_up()
    {
        var store = NewStore();
        var first = store.SaveSeatSubmission(Input(0, "first try", imageId: "img-1"))!;
        store.ReopenSeatSubmission(first.Id, null);
        var second = store.SaveSeatSubmission(Input(0, "corrected"))!;

        Assert.Equal(first.Id, second.Id);
        Assert.Single(store.GetSeatSubmissionsForUnit(1, 1));
        Assert.Equal("corrected", second.Text);
        // Sending no new picture keeps the one already on file rather than wiping it.
        Assert.Equal("img-1", second.ImageId);
    }

    [Fact]
    public void A_reviewed_seat_is_frozen_until_staff_reopen_it()
    {
        var store = NewStore();
        var saved = store.SaveSeatSubmission(Input(0, "mine"))!;
        Assert.False(saved.Editable);

        var reviewed = store.ReviewSeatSubmission(saved.Id, "admin", "همه چیز درست است")!;
        Assert.Equal(SeatSubmissionStatus.Reviewed, reviewed.Status);
        Assert.False(reviewed.Editable);
        Assert.Equal("admin", reviewed.ReviewedBy);

        // The customer can no longer change what's already being worked on…
        Assert.Null(store.SaveSeatSubmission(Input(0, "sneaky edit")));
        Assert.Equal("mine", store.GetSeatSubmission(saved.Id)!.Text);

        // …until staff hand it back for a correction.
        var reopened = store.ReopenSeatSubmission(saved.Id, "تصویر واضح‌تری بفرستید")!;
        Assert.True(reopened.Editable);
        Assert.Equal("تصویر واضح‌تری بفرستید", reopened.ReviewNote);
        Assert.Equal("fixed", store.SaveSeatSubmission(Input(0, "fixed"))!.Text);
    }

    // The hole this closes: a customer could change what they filed at any time before staff got to it, so the
    // device staff set up could differ from the one on record. Filing once is the customer's; every change after
    // that needs staff.
    [Fact]
    public void A_filed_seat_cannot_be_changed_before_review_either()
    {
        var store = NewStore();
        var saved = store.SaveSeatSubmission(Input(0, "Samsung A52", imageId: "img-1"))!;

        Assert.Equal(SeatSubmissionStatus.Pending, saved.Status);
        Assert.Null(store.SaveSeatSubmission(Input(0, "iPhone 13", imageId: "img-2")));
        var onFile = store.GetSeatSubmission(saved.Id)!;
        Assert.Equal("Samsung A52", onFile.Text);
        Assert.Equal("img-1", onFile.ImageId);
        Assert.Empty(onFile.History);
    }

    [Fact]
    public void A_reopen_allows_exactly_one_change()
    {
        var store = NewStore();
        var saved = store.SaveSeatSubmission(Input(0, "old device"))!;
        store.ReviewSeatSubmission(saved.Id, "admin", null);
        store.ReopenSeatSubmission(saved.Id, "دستگاه جدید را ثبت کنید", "admin");

        var changed = store.SaveSeatSubmission(Input(0, "new device"))!;
        Assert.Equal("new device", changed.Text);
        Assert.False(changed.Editable);

        Assert.Null(store.SaveSeatSubmission(Input(0, "one more")));
        Assert.Equal("new device", store.GetSeatSubmission(saved.Id)!.Text);
    }

    // Staff reopen, then decide the details were fine after all: approving locks the seat again, rather than
    // leaving an unused reopen behind for the customer to spend later.
    [Fact]
    public void Approving_a_reopened_seat_takes_the_reopen_back()
    {
        var store = NewStore();
        var saved = store.SaveSeatSubmission(Input(0, "mine"))!;
        store.ReopenSeatSubmission(saved.Id, null, "admin");
        store.ReviewSeatSubmission(saved.Id, "admin", null);

        Assert.False(store.GetSeatSubmission(saved.Id)!.Editable);
        Assert.Null(store.SaveSeatSubmission(Input(0, "late edit")));
    }

    // Seats reopened before the flag existed: the customer was already asked for new details, so they keep the
    // right to send them.
    [Fact]
    public void A_seat_reopened_before_the_flag_existed_is_still_open()
    {
        var legacy = new SeatSubmission { Status = SeatSubmissionStatus.Pending };
        legacy.Events.Add(new SeatSubmissionEvent { Action = "submitted" });
        legacy.Events.Add(new SeatSubmissionEvent { Action = "reopened", By = "admin" });
        Assert.True(legacy.Editable);

        legacy.Events.Add(new SeatSubmissionEvent { Action = "edited" });
        Assert.False(legacy.Editable);
    }

    [Fact]
    public void Reopening_one_seat_leaves_the_others_frozen()
    {
        var store = NewStore();
        var a = store.SaveSeatSubmission(Input(0, "seat a"))!;
        store.SaveSeatSubmission(Input(1, "seat b"));

        store.ReopenSeatSubmission(a.Id, null);

        Assert.Equal("seat a edited", store.SaveSeatSubmission(Input(0, "seat a edited"))!.Text);
        Assert.Null(store.SaveSeatSubmission(Input(1, "seat b edited")));
    }

    // The switch lives on the PLAN, so two plans of the SAME product can differ: one asks its buyers for setup
    // details, the other asks for nothing.
    [Fact]
    public void Whether_a_seat_collects_info_is_decided_by_the_plan_not_the_product()
    {
        var store = NewStore();
        var product = store.GetProduct(1)!;
        product.Plans.Clear();
        product.Plans.Add(new ProductPlan { Type = "اشتراکی", Months = 3, Price = 50_000, IsActive = true, CollectSeatInfo = true });
        product.Plans.Add(new ProductPlan { Type = "اختصاصی", Months = 3, Price = 90_000, IsActive = true, CollectSeatInfo = false });
        store.UpdateProduct(product);

        var saved = store.GetProduct(1)!.Plans;
        Assert.True(saved.Single(p => p.Type == "اشتراکی").CollectSeatInfo);
        Assert.False(saved.Single(p => p.Type == "اختصاصی").CollectSeatInfo);
    }

    // A plan used to be able to grant post-approval corrections the customer made on their own. That is gone:
    // an old allowance on a seat must not unlock it.
    [Fact]
    public void A_plan_allowance_does_not_let_the_buyer_edit_on_their_own()
    {
        var store = NewStore();
        var input = Input(0, "first");
        input.EditLimit = 3;
        var saved = store.SaveSeatSubmission(input)!;
        Assert.False(saved.Editable);
        Assert.Null(store.SaveSeatSubmission(Input(0, "before review")));

        store.ReviewSeatSubmission(saved.Id, "admin", null);
        Assert.False(store.GetSeatSubmission(saved.Id)!.Editable);
        Assert.Null(store.SaveSeatSubmission(Input(0, "after review")));
        Assert.Equal("first", store.GetSeatSubmission(saved.Id)!.Text);
    }

    [Fact]
    public void Without_an_allowance_approval_freezes_the_seat()
    {
        var store = NewStore();
        var saved = store.SaveSeatSubmission(Input(0, "mine"))!;  // EditLimit defaults to 0
        store.ReviewSeatSubmission(saved.Id, "admin", null);

        Assert.False(store.GetSeatSubmission(saved.Id)!.Editable);
        Assert.Null(store.SaveSeatSubmission(Input(0, "nope")));
    }

    [Fact]
    public void The_pending_queue_is_what_the_admin_badge_counts()
    {
        var store = NewStore();
        var a = store.SaveSeatSubmission(Input(0, "a"))!;
        store.SaveSeatSubmission(Input(1, "b"));
        store.ReviewSeatSubmission(a.Id, "admin", null);

        Assert.Single(store.GetSeatSubmissions(SeatSubmissionStatus.Pending));
        Assert.Single(store.GetSeatSubmissions(SeatSubmissionStatus.Reviewed));
        Assert.Equal(2, store.GetSeatSubmissions().Count);
        Assert.Equal(1, store.GetAdminBadgeCounts().PendingSeatInfo);
    }

    // Rejecting clears the entry on purpose: the buyer is being asked for these details AGAIN, so leaving the
    // old ones in place is how staff end up working from the picture that was already refused. What was refused
    // moves into History instead of disappearing.
    [Fact]
    public void Rejecting_a_seat_wipes_what_the_customer_sent()
    {
        var store = NewStore();
        var saved = store.SaveSeatSubmission(Input(0, "blurry", imageId: "img-1"))!;

        var rejection = store.RejectSeatSubmission(saved.Id, "admin", "تصویر واضح نیست")!;

        // The picture's id comes back out so the caller can delete the file it points at.
        Assert.Equal("img-1", rejection.RemovedImageId);
        var after = store.GetSeatSubmission(saved.Id)!;
        Assert.Equal(SeatSubmissionStatus.Rejected, after.Status);
        Assert.Null(after.ImageId);
        Assert.Equal("", after.Text);
        Assert.Equal("تصویر واضح نیست", after.ReviewNote);
        Assert.Equal("admin", after.ReviewedBy);
        var refused = Assert.Single(after.History);
        Assert.Equal("blurry", refused.Text);
        Assert.Equal("img-1", refused.ImageId);
        Assert.Equal(SeatSubmissionStatus.Rejected, refused.Status);
        Assert.Equal("تصویر واضح نیست", refused.ReviewNote);
    }

    [Fact]
    public void A_rejected_seat_is_the_customers_to_file_again()
    {
        var store = NewStore();
        var input = Input(0, "first", imageId: "img-1");
        input.EditLimit = 1;
        var saved = store.SaveSeatSubmission(input)!;
        store.RejectSeatSubmission(saved.Id, "admin", "دوباره بفرستید");

        Assert.True(store.GetSeatSubmission(saved.Id)!.Editable);

        var resent = store.SaveSeatSubmission(Input(0, "clearer", imageId: "img-2"))!;
        Assert.Equal(saved.Id, resent.Id);
        Assert.Equal("clearer", resent.Text);
        Assert.Equal("img-2", resent.ImageId);
        // Back in the staff queue, and the rejection reason is gone — it described details that no longer exist.
        Assert.Equal(SeatSubmissionStatus.Pending, resent.Status);
        Assert.Null(resent.ReviewNote);
        // Staff asked for the re-send, so it costs the buyer none of the plan's allowance.
        Assert.Equal(0, resent.EditsUsed);
        Assert.Equal(1, resent.EditsLeft);
        // One re-filing per rejection: the new details are frozen like any other.
        Assert.False(resent.Editable);
        Assert.Null(store.SaveSeatSubmission(Input(0, "and again")));
    }

    // Rejecting an ALREADY-APPROVED seat has to work too — an approval can turn out to be wrong — and it must
    // free the seat without charging the buyer, even when the plan granted no corrections at all.
    [Fact]
    public void Rejecting_an_approved_seat_frees_it_without_spending_an_allowance()
    {
        var store = NewStore();
        var saved = store.SaveSeatSubmission(Input(0, "mine"))!;  // EditLimit defaults to 0
        store.ReviewSeatSubmission(saved.Id, "admin", null);
        Assert.False(store.GetSeatSubmission(saved.Id)!.Editable);

        store.RejectSeatSubmission(saved.Id, "admin", null);

        var resent = store.SaveSeatSubmission(Input(0, "second attempt"))!;
        Assert.Equal("second attempt", resent.Text);
        Assert.Equal(SeatSubmissionStatus.Pending, resent.Status);
        Assert.Equal(0, resent.EditsUsed);
    }

    // The queue counts work waiting on STAFF. A rejected seat is waiting on the customer, so it must not sit in
    // the pending badge padding the number staff are meant to work through.
    [Fact]
    public void A_rejected_seat_leaves_the_staff_queue()
    {
        var store = NewStore();
        var a = store.SaveSeatSubmission(Input(0, "a"))!;
        store.SaveSeatSubmission(Input(1, "b"));

        store.RejectSeatSubmission(a.Id, "admin", null);

        Assert.Single(store.GetSeatSubmissions(SeatSubmissionStatus.Pending));
        Assert.Single(store.GetSeatSubmissions(SeatSubmissionStatus.Rejected));
        Assert.Equal(1, store.GetAdminBadgeCounts().PendingSeatInfo);
    }

    [Fact]
    public void Rejecting_a_seat_tells_the_customer_why()
    {
        var store = NewStore();
        var saved = store.SaveSeatSubmission(Input(0, "mine"))!;

        store.RejectSeatSubmission(saved.Id, "admin", "تصویر ناخواناست");

        var notice = store.GetUserNotifications(saved.UserId).First();
        Assert.Contains("تأیید نشد", notice.Title);
        Assert.Contains("تصویر ناخواناست", notice.Body);
        Assert.Contains("ORD-1", notice.Body);
    }

    // With no reason typed, "rejected" on its own tells the customer nothing they can act on — the copy has to
    // carry the instruction instead.
    [Fact]
    public void A_rejection_with_no_reason_still_says_what_to_do()
    {
        var store = NewStore();
        var saved = store.SaveSeatSubmission(Input(0, "mine"))!;

        var rejection = store.RejectSeatSubmission(saved.Id, "admin", "   ")!;

        Assert.Null(rejection.Submission.ReviewNote);   // whitespace is not a reason
        Assert.Contains("دوباره وارد کنید", store.GetUserNotifications(saved.UserId).First().Body);
    }

    [Fact]
    public void Rejecting_a_seat_that_does_not_exist_is_not_found()
    {
        var store = NewStore();
        Assert.Null(store.RejectSeatSubmission(4242, "admin", "nope"));
    }
}

// Reopening a seat for a correction hands it back with what was sent still on it, and the customer's new
// details must not erase the old ones: staff still need the device they set up last time.
public class SeatSubmissionHistoryTests
{
    private static SeatSubmission Input(string text, string? imageId = null) => new()
    {
        UserId = 5, OrderId = 1, UnitId = 1, SeatIndex = 0, SeatLabel = "A - 1", ProductId = 1,
        ProductName = "Windscribe", OrderCode = "ORD-1", UserName = "reza", ImageId = imageId, Text = text,
    };

    [Fact]
    public void New_details_after_a_reopen_keep_the_previous_device()
    {
        var store = TestStore.Create();
        var first = store.SaveSeatSubmission(Input("ASUS K550VX", imageId: "img-old"))!;
        store.ReviewSeatSubmission(first.Id, "admin", null);
        store.ReopenSeatSubmission(first.Id, "دستگاه جدید را ثبت کنید");

        // The reopened seat still carries what was sent, so the form comes back filled in.
        var reopened = store.GetSeatSubmission(first.Id)!;
        Assert.Equal("ASUS K550VX", reopened.Text);
        Assert.Equal("img-old", reopened.ImageId);
        Assert.True(reopened.Editable);

        var updated = store.SaveSeatSubmission(Input("Lenovo ThinkPad", imageId: "img-new"))!;

        Assert.Equal("Lenovo ThinkPad", updated.Text);
        Assert.Equal("img-new", updated.ImageId);
        Assert.Equal(SeatSubmissionStatus.Pending, updated.Status);
        var previous = Assert.Single(store.GetSeatSubmission(first.Id)!.History);
        Assert.Equal("ASUS K550VX", previous.Text);
        Assert.Equal("img-old", previous.ImageId);
        Assert.Equal(SeatSubmissionStatus.Pending, previous.Status);
        Assert.Equal("دستگاه جدید را ثبت کنید", previous.ReviewNote);
    }

    [Fact]
    public void Versions_pile_up_newest_first()
    {
        var store = TestStore.Create();
        var id = store.SaveSeatSubmission(Input("one"))!.Id;
        store.ReopenSeatSubmission(id, null);
        store.SaveSeatSubmission(Input("two"));
        store.ReopenSeatSubmission(id, null);
        store.SaveSeatSubmission(Input("three"));

        Assert.Equal(new[] { "two", "one" }, store.GetSeatSubmission(id)!.History.Select(v => v.Text));
    }

    [Fact]
    public void Saving_the_same_details_again_is_not_a_new_version()
    {
        var store = TestStore.Create();
        var id = store.SaveSeatSubmission(Input("same", imageId: "img"))!.Id;
        store.ReopenSeatSubmission(id, null);
        store.SaveSeatSubmission(Input("same"));

        Assert.Empty(store.GetSeatSubmission(id)!.History);
    }

    [Fact]
    public void Re_filing_after_a_rejection_keeps_the_refused_version_and_adds_no_empty_one()
    {
        var store = TestStore.Create();
        var first = store.SaveSeatSubmission(Input("blurry", imageId: "img"))!;
        store.RejectSeatSubmission(first.Id, "admin", "unreadable");

        var refiled = store.SaveSeatSubmission(Input("clear"))!;

        // The refused details, then the new ones as the current entry — nothing blank in between.
        var refused = Assert.Single(refiled.History);
        Assert.Equal("blurry", refused.Text);
        Assert.Equal("clear", refiled.Text);
    }

    [Fact]
    public void Every_action_is_logged_with_who_did_it()
    {
        var store = TestStore.Create();
        var first = store.SaveSeatSubmission(Input("Samsung A52"))!;
        store.ReviewSeatSubmission(first.Id, "maryam", null);
        store.ReopenSeatSubmission(first.Id, "دستگاه جدید را ثبت کنید", "maryam");
        store.SaveSeatSubmission(Input("iPhone 13"));
        store.RejectSeatSubmission(first.Id, "reza", "مدل دستگاه پشتیبانی نمی‌شود");
        store.SaveSeatSubmission(Input("Pixel 7"));

        var events = store.GetSeatSubmission(first.Id)!.Events;
        Assert.Equal(new[] { "submitted", "reviewed", "reopened", "edited", "rejected", "submitted" }, events.Select(e => e.Action));
        Assert.Equal(new string?[] { null, "maryam", "maryam", null, "reza", null }, events.Select(e => e.By));
        Assert.Equal("دستگاه جدید را ثبت کنید", events[2].Note);
        Assert.Equal("مدل دستگاه پشتیبانی نمی‌شود", events[4].Note);
        Assert.True(events.Zip(events.Skip(1)).All(p => p.First.AtUtc <= p.Second.AtUtc));
    }

    [Fact]
    public void Previous_and_new_devices_are_both_on_record_after_a_reopen()
    {
        var store = TestStore.Create();
        var first = store.SaveSeatSubmission(Input("Samsung A52", imageId: "img-a"))!;
        store.ReviewSeatSubmission(first.Id, "maryam", null);
        store.ReopenSeatSubmission(first.Id, "دستگاه جدید را ثبت کنید", "maryam");

        var now = store.SaveSeatSubmission(Input("iPhone 13", imageId: "img-b"))!;

        Assert.Equal("iPhone 13", now.Text);            // new
        Assert.Equal("img-b", now.ImageId);
        var before = Assert.Single(now.History);       // previous
        Assert.Equal("Samsung A52", before.Text);
        Assert.Equal("img-a", before.ImageId);
        Assert.Equal(SeatSubmissionStatus.Pending, now.Status); // back in the staff queue
    }

    [Fact]
    public void History_is_capped()
    {
        var store = TestStore.Create();
        var id = 0;
        for (var i = 0; i < SeatSubmissionRules.MaxHistory + 5; i++)
        {
            if (i > 0) store.ReopenSeatSubmission(id, null);
            id = store.SaveSeatSubmission(Input($"v{i}"))!.Id;
        }

        var history = store.GetSeatSubmission(id)!.History;
        Assert.Equal(SeatSubmissionRules.MaxHistory, history.Count);
        Assert.Equal($"v{SeatSubmissionRules.MaxHistory + 3}", history[0].Text);
    }
}

// Reopening a seat only helps if the customer finds out: the admin's message goes to them in the bell and by
// email, and an empty message falls back to the standard request for the new device's details.
public class SeatReopenNotifyTests
{
    private sealed class Outbox : Phonix.Api.Services.IEmailSender
    {
        public List<(string To, string Subject, string Text, string? Html)> Sent { get; } = new();
        public Task<bool> SendAsync(string to, string subject, string body, string? htmlBody = null)
        {
            Sent.Add((to, subject, body, htmlBody));
            return Task.FromResult(true);
        }
    }

    private static (Phonix.Api.Controllers.SeatSubmissionsController Controller, Outbox Outbox, IDataStore Store, SeatSubmission Seat) Setup()
    {
        var store = TestStore.Create();
        var buyer = store.GetUser(1)!; // ali, has an email on file
        var seat = store.SaveSeatSubmission(new SeatSubmission
        {
            UserId = buyer.Id, OrderId = 1, UnitId = 1, SeatIndex = 0, SeatLabel = "A - 1", ProductId = 1,
            ProductName = "Netflix", OrderCode = "PX-1", UserName = buyer.Name, Text = "Samsung A52",
        })!;
        store.ReviewSeatSubmission(seat.Id, "maryam", null);
        var admin = store.GetUsers().First(u => u.Role == UserRole.Admin);
        var outbox = new Outbox();
        var controller = new Phonix.Api.Controllers.SeatSubmissionsController(store, new Phonix.Api.Services.LocalFileStorageService(),
            new Phonix.Api.Services.UserMailer(store, outbox, Microsoft.Extensions.Logging.Abstractions.NullLogger<Phonix.Api.Services.UserMailer>.Instance))
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
                {
                    User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]
                    {
                        new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, admin.Id.ToString()),
                        new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, nameof(UserRole.Admin)),
                        new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, admin.Username),
                    }, "test")),
                },
            },
        };
        return (controller, outbox, store, seat);
    }

    [Fact]
    public void The_admins_message_reaches_the_customer_by_email_and_in_the_bell()
    {
        var (c, outbox, store, seat) = Setup();
        const string message = "دستگاه قبلی از اکانت خارج شد.\nلطفاً تا ۲۴ ساعت مشخصات دستگاه جدید را ثبت کنید؛ در غیر این صورت اکانت از گارانتی خارج می‌شود.";

        c.Reopen(seat.Id, new Phonix.Api.Controllers.SeatReviewInput(message));

        var buyer = store.GetUser(1)!;
        var mail = Assert.Single(outbox.Sent);
        Assert.Equal(buyer.Email, mail.To);
        Assert.Contains("PX-1", mail.Subject);
        Assert.Contains(message, mail.Text);
        Assert.Contains("<br>لطفاً تا ۲۴ ساعت", mail.Html);
        var notice = Assert.Single(store.GetUserNotifications(buyer.Id), n => n.Title.Contains("مشخصات دستگاه"));
        Assert.Contains("از گارانتی خارج می‌شود", notice.Body);
        Assert.Equal(SeatSubmissionStatus.Pending, store.GetSeatSubmission(seat.Id)!.Status);
    }

    [Fact]
    public void An_empty_message_sends_the_standard_warranty_request()
    {
        var (c, outbox, store, seat) = Setup();

        c.Reopen(seat.Id, new Phonix.Api.Controllers.SeatReviewInput("  "));

        Assert.Contains(Phonix.Api.Data.OrderNotices.SeatInfoReopenDefault, Assert.Single(outbox.Sent).Text);
        Assert.Contains(store.GetUserNotifications(1), n => n.Body.Contains("گارانتی"));
    }
}

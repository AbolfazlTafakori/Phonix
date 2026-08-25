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
        Assert.True(saved.Editable);

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

    [Fact]
    public void Reviewing_one_seat_leaves_the_others_editable()
    {
        var store = NewStore();
        var a = store.SaveSeatSubmission(Input(0, "seat a"))!;
        store.SaveSeatSubmission(Input(1, "seat b"));

        store.ReviewSeatSubmission(a.Id, "admin", null);

        Assert.Null(store.SaveSeatSubmission(Input(0, "blocked")));
        Assert.Equal("seat b edited", store.SaveSeatSubmission(Input(1, "seat b edited"))!.Text);
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

    // The plan may grant post-approval corrections. Each one costs an allowance and sends the seat back to the
    // queue, so staff always re-approve what they're actually working from.
    [Fact]
    public void A_granted_allowance_lets_the_buyer_correct_an_approved_seat()
    {
        var store = NewStore();
        var input = Input(0, "first");
        input.EditLimit = 1;
        var saved = store.SaveSeatSubmission(input)!;
        store.ReviewSeatSubmission(saved.Id, "admin", null);

        // One correction is allowed: it lands, spends the allowance, and re-enters the review queue.
        var corrected = store.SaveSeatSubmission(Input(0, "corrected"))!;
        Assert.Equal("corrected", corrected.Text);
        Assert.Equal(SeatSubmissionStatus.Pending, corrected.Status);
        Assert.Equal(1, corrected.EditsUsed);
        Assert.Equal(0, corrected.EditsLeft);
        Assert.Null(corrected.ReviewedAtUtc);

        // Editing again before the re-review is still free — the allowance pays for changing an APPROVED seat.
        Assert.NotNull(store.SaveSeatSubmission(Input(0, "again")));
        Assert.Equal(1, store.GetSeatSubmission(saved.Id)!.EditsUsed);

        // Once approved a second time, the spent allowance leaves it frozen for good.
        store.ReviewSeatSubmission(saved.Id, "admin", null);
        Assert.False(store.GetSeatSubmission(saved.Id)!.Editable);
        Assert.Null(store.SaveSeatSubmission(Input(0, "blocked")));
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

    // Rejecting is destructive on purpose: the buyer is being asked for these details AGAIN, so leaving the
    // old ones in place is how staff end up working from the picture that was already refused.
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

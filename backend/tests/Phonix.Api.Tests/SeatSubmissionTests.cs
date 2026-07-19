using Phonix.Api.Data;
using Phonix.Api.Models;
using Xunit;

namespace Phonix.Api.Tests;

// Per-seat customer submissions: one entry per seat of a shared account, replaced in place on a re-send, and
// frozen once staff review it.
public class SeatSubmissionTests
{
    private static StoreData NewStore() => TestStore.Create();

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
}

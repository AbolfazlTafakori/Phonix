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

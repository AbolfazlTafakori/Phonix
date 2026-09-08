using Phonix.Api.Data;
using Phonix.Api.Models;
using Xunit;

namespace Phonix.Api.Tests;

// A ticket stays open only while the conversation is alive. After 24 hours with no message from either side
// the sweep closes it; the customer can still reopen it by replying.
public class TicketAutoCloseTests
{
    private static Ticket Open(IDataStore store) =>
        store.CreateTicket(1, "ali", "اکانت کار نمی‌کند", "فنی", "سلام، مشکل دارم");

    [Fact]
    public void A_ticket_with_no_message_for_the_idle_window_is_closed()
    {
        var store = TestStore.Create();
        var t = Open(store);

        // Zero window = "everything said before this instant is old", which is the same comparison the
        // 24-hour sweep makes without having to wait a day for it.
        var closed = store.CloseIdleTickets(TimeSpan.Zero);

        Assert.Contains(closed, c => c.Id == t.Id);
        var after = store.GetTicket(t.Id)!;
        Assert.Equal(TicketStatus.Closed, after.Status);
        Assert.NotNull(after.AutoClosedAtUtc);
    }

    [Fact]
    public void A_ticket_someone_just_wrote_on_is_left_alone()
    {
        var store = TestStore.Create();
        var t = Open(store);

        var closed = store.CloseIdleTickets(TimeSpan.FromHours(24));

        Assert.Empty(closed);
        Assert.Equal(TicketStatus.Open, store.GetTicket(t.Id)!.Status);
    }

    [Fact]
    public void A_reply_restarts_the_clock()
    {
        var store = TestStore.Create();
        var t = Open(store);
        store.ReplyTicket(t.Id, "پشتیبانی", "در حال بررسی است", isAdmin: true);

        Assert.Empty(store.CloseIdleTickets(TimeSpan.FromHours(24)));
        Assert.Equal(TicketStatus.Answered, store.GetTicket(t.Id)!.Status);
    }

    [Fact]
    public void The_customer_reopens_an_auto_closed_ticket_by_replying()
    {
        var store = TestStore.Create();
        var t = Open(store);
        store.CloseIdleTickets(TimeSpan.Zero);
        Assert.Equal(TicketStatus.Closed, store.GetTicket(t.Id)!.Status);

        store.ReplyTicket(t.Id, "ali", "هنوز حل نشده", isAdmin: false);

        var after = store.GetTicket(t.Id)!;
        Assert.Equal(TicketStatus.Open, after.Status);
        Assert.Null(after.AutoClosedAtUtc);   // no longer an automatic close — it is a live ticket again
        Assert.Empty(store.CloseIdleTickets(TimeSpan.FromHours(24)));
    }

    // Tickets written before the timestamp existed carry nothing to measure from. The first sweep must stamp
    // them and leave them open — closing the whole backlog the moment this shipped would have been the worst
    // possible first impression of the feature.
    [Fact]
    public void A_ticket_from_before_the_timestamp_existed_is_stamped_not_closed()
    {
        var store = TestStore.Create();
        var t = Open(store);

        // Round-trip the ticket through a snapshot with the field cleared — the shape a pre-upgrade row has.
        var snapshot = store.DeserializeSnapshot(store.SerializeSnapshot())!;
        foreach (var legacy in snapshot.Tickets) { legacy.LastMessageAtUtc = null; legacy.Status = TicketStatus.Open; }
        store.LoadSnapshot(snapshot);
        Assert.Null(store.GetTicket(t.Id)!.LastMessageAtUtc);

        var closed = store.CloseIdleTickets(TimeSpan.FromHours(24));

        Assert.Empty(closed);
        var after = store.GetTicket(t.Id)!;
        Assert.Equal(TicketStatus.Open, after.Status);
        Assert.NotNull(after.LastMessageAtUtc);   // stamped, so its 24 hours start now
    }
}

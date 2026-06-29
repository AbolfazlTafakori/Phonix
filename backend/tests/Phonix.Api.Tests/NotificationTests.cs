using Phonix.Api.Data;
using Phonix.Api.Models;
using Xunit;

namespace Phonix.Api.Tests;

// Bell notifications: private feeds are per-user, public broadcasts reach everyone, read state is
// per-user, and key events (order delivery) auto-notify the owner.
public class NotificationTests
{
    [Fact]
    public void Private_and_public_feeds_are_scoped_correctly()
    {
        var store = TestStore.Create();
        store.AddNotification(1, "خصوصی یک", "");
        store.AddNotification(null, "عمومی", "");
        store.AddNotification(2, "خصوصی دو", "");

        var u1 = store.GetUserNotifications(1);
        Assert.Contains(u1, n => n.Title == "خصوصی یک");
        Assert.Contains(u1, n => n.Title == "عمومی");
        Assert.DoesNotContain(u1, n => n.Title == "خصوصی دو");

        var u2 = store.GetUserNotifications(2);
        Assert.Contains(u2, n => n.Title == "عمومی");
        Assert.DoesNotContain(u2, n => n.Title == "خصوصی یک");
    }

    [Fact]
    public void Unread_count_drops_to_zero_after_mark_read_then_rises_for_new_ones()
    {
        var store = TestStore.Create();
        var baseU1 = store.CountUnread(1); // seed may create some (e.g. a ticket reply)
        store.AddNotification(1, "a", "");
        store.AddNotification(null, "b", "");
        Assert.Equal(baseU1 + 2, store.CountUnread(1));

        store.MarkNotificationsRead(1);
        Assert.Equal(0, store.CountUnread(1));

        // a new broadcast after reading is unread again for everyone.
        store.AddNotification(null, "c", "");
        Assert.Equal(1, store.CountUnread(1));
    }

    [Fact]
    public void New_users_do_not_receive_broadcasts_sent_before_they_registered()
    {
        var store = TestStore.Create();
        store.AddNotification(null, "قبل از عضویت", ""); // broadcast to the users who exist now

        var newcomer = store.RegisterUser(new AppUser { Username = "newbie", Name = "Newbie", Email = "newbie@example.com" });

        // the earlier broadcast is NOT in the newcomer's feed (and not counted as unread)...
        Assert.DoesNotContain(store.GetUserNotifications(newcomer.Id), n => n.Title == "قبل از عضویت");
        Assert.Equal(0, store.CountUnread(newcomer.Id));

        // ...but a broadcast sent AFTER they joined does reach them.
        store.AddNotification(null, "بعد از عضویت", "");
        Assert.Contains(store.GetUserNotifications(newcomer.Id), n => n.Title == "بعد از عضویت");

        // existing users still see both broadcasts.
        var u1 = store.GetUserNotifications(1).Select(n => n.Title).ToList();
        Assert.Contains("قبل از عضویت", u1);
        Assert.Contains("بعد از عضویت", u1);
    }

    [Fact]
    public void Sqlite_broadcasts_skip_users_who_registered_afterwards()
    {
        var path = Path.Combine(Path.GetTempPath(), "phonix-sqlite-tests", Guid.NewGuid() + ".db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var store = new SqliteDataStore(path);

        var early = store.RegisterUser(new AppUser { Username = "early", Name = "Early" });
        store.AddNotification(null, "broadcast", "");
        var late = store.RegisterUser(new AppUser { Username = "late", Name = "Late" });

        Assert.Contains(store.GetUserNotifications(early.Id), n => n.Title == "broadcast");
        Assert.DoesNotContain(store.GetUserNotifications(late.Id), n => n.Title == "broadcast");
        Assert.Equal(0, store.CountUnread(late.Id));
    }

    [Fact]
    public void Delivering_an_order_notifies_the_buyer()
    {
        var store = TestStore.Create();
        var user = store.GetUser(1)!;
        var order = store.PlaceOrder(user, new[] { (1, 1, (int?)null) }, "کارت بانکی", fromWallet: false).Order!;
        var before = store.GetUserNotifications(1).Count;

        store.DeliverOrder(order.Id, "اطلاعات اکانت");

        var after = store.GetUserNotifications(1);
        Assert.True(after.Count > before);
        Assert.Contains(after, n => n.Title.Contains("سفارش"));
    }

    [Fact]
    public void Approving_a_card_privately_congratulates_only_that_user()
    {
        var store = TestStore.Create();
        var card = store.AddCard(6, "6037991234567893", "نگار شریفی", "/uploads/c.png").Card!; // user 6 = negar, level 0
        var before6 = store.GetUserNotifications(6).Count;
        var before1 = store.GetUserNotifications(1).Count;

        store.SetCardStatus(card.Id, BankCardStatus.Approved, null);

        var after6 = store.GetUserNotifications(6);
        Assert.True(after6.Count > before6);
        Assert.Contains(after6, n => n.Title.Contains("سطح ۱"));
        // the congratulation is private — another user's feed is untouched (no broadcast).
        Assert.Equal(before1, store.GetUserNotifications(1).Count);
    }
}

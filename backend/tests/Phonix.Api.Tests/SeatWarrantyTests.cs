using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Services;
using Xunit;

namespace Phonix.Api.Tests;

// Chasing the details buyers never filed, and closing the warranty on individual seats.
//
// The two things these tests pin down: a seat of a shared account is addressed by its POSITION in the
// delivered content (so the panel and the buyer mean the same seat), and every decision is per seat — closing
// one user of an account must leave everyone else on the same credentials untouched.
// Seed reference: product 1 = Netflix, user 5 = reza.
public class SeatWarrantyTests
{
    private static IDataStore NewStore() => TestStore.Create();

    private static StockFulfillmentService Fulfillment(IDataStore store) =>
        new(store, NullLogger<StockFulfillmentService>.Instance);

    private static void Account(IDataStore store, int capacity, string username, int months = 3) =>
        store.AddStockAccount(new StockAccount
        {
            ProductId = 1,
            Username = username,
            Password = SensitiveField.Protect("p@ss"),
            Plan = "Premium",
            Capacity = capacity,
            Months = months,
            AddedBy = "admin",
        });

    // A delivered shared account: `users` seats on one inventory account, sold under a plan that asks the
    // buyer for per-seat details.
    private static (Order Order, OrderUnit Unit) Delivered(IDataStore store, int users)
    {
        Account(store, capacity: users, username: "shared@mail.com");
        var product = store.GetProduct(1)!;
        product.SlotFulfillment = true;
        product.AutoDeliverStock = true;
        product.Plans.Clear();
        product.Plans.Add(new ProductPlan
        {
            Type = "اشتراکی", Months = 3, Price = 50_000, IsActive = true, UserCount = users,
            CollectSeatInfo = true, SeatInfoHint = "تصویر دستگاه", SeatInfoEditLimit = 1,
        });
        store.UpdateProduct(product);
        var planId = store.GetProduct(1)!.Plans.Single().Id;
        var placed = store.PlaceOrder(store.GetUser(5)!, new[] { (1, 1, (int?)planId) }, "wallet", fromWallet: true);
        Assert.Null(placed.Error);
        var served = Fulfillment(store).ServeUnit(placed.Order!, placed.Order!.Units.Single(), "انبار مجازی");
        Assert.NotNull(served);
        var order = served!.Value.order;
        return (order, order.Units.Single());
    }

    // What the panel (or the buyer) hands the store for one seat. SeatSubmission is a plain class, so each
    // call builds its own.
    private static SeatSubmission Stub(Order order, OrderUnit unit, int seatIndex, string label, string text = "") => new()
    {
        UserId = order.UserId, OrderId = order.Id, UnitId = unit.Id, SeatIndex = seatIndex, SeatLabel = label,
        ProductId = unit.ProductId, ProductName = unit.Name, OrderCode = order.Code, UserName = order.UserName,
        EditLimit = 1, Text = text,
    };

    [Fact]
    public void Every_delivered_seat_is_listed_with_its_own_block_and_its_account()
    {
        var store = NewStore();
        var (order, unit) = Delivered(store, users: 3);

        // Scoped to this order: the seeded shop already has delivered orders of its own.
        var rows = new SeatRosterService(store).All().Where(r => r.OrderId == order.Id).ToList();

        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { 0, 1, 2 }, rows.Select(r => r.SeatIndex));
        // Each row says which block it is and which shared account that block belongs to — the two things
        // staff need to answer "this one is which user of which account".
        Assert.All(rows, r => Assert.False(string.IsNullOrWhiteSpace(r.SeatLabel)));
        Assert.Equal(3, rows.Select(r => r.SeatLabel).Distinct().Count());
        Assert.All(rows, r => Assert.Equal("shared@mail.com", r.AccountUsername));
        Assert.All(rows, r => Assert.True(r.CollectsInfo));
        Assert.All(rows, r => Assert.Equal(order.Code, r.OrderCode));
        Assert.All(rows, r => Assert.Equal(unit.Id, r.UnitId));
    }

    [Fact]
    public void Seats_waiting_on_the_buyer_are_the_ones_never_filed_or_handed_back()
    {
        var store = NewStore();
        var (order, unit) = Delivered(store, users: 2);
        var roster = new SeatRosterService(store);

        Assert.Equal(2, roster.Unregistered().Count);   // nothing filed for either seat yet

        var filed = store.SaveSeatSubmission(Stub(order, unit, 0, "A - 1", "my device"))!;
        Assert.Single(roster.Unregistered());           // seat 0 is now the staff's problem, not the buyer's

        // Handing it back for a correction puts it back on the buyer, even though the old text is still there.
        store.ReopenSeatSubmission(filed.Id, "تصویر واضح‌تر بفرستید");
        Assert.Equal(2, roster.Unregistered().Count);

        // …and their next save takes it off the list again.
        store.SaveSeatSubmission(Stub(order, unit, 0, "A - 1", "clearer"));
        Assert.Single(roster.Unregistered());
    }

    [Fact]
    public void A_staff_stub_carries_decisions_without_entering_the_review_queue()
    {
        var store = NewStore();
        var (order, unit) = Delivered(store, users: 2);

        var first = store.EnsureSeatSubmission(Stub(order, unit, 1, "A - 2"));
        var again = store.EnsureSeatSubmission(Stub(order, unit, 1, "A - 2"));

        Assert.Equal(first.Id, again.Id);               // one row per seat, whatever staff do twice
        Assert.Equal(SeatSubmissionStatus.NotFiled, first.Status);
        Assert.Empty(store.GetSeatSubmissions(SeatSubmissionStatus.Pending));
        // The buyer may still file for it — a stub is not a submission.
        Assert.True(first.Editable);
        var saved = store.SaveSeatSubmission(Stub(order, unit, 1, "A - 2", "sent at last"));
        Assert.NotNull(saved);
        Assert.Equal(SeatSubmissionStatus.Pending, saved!.Status);
        Assert.Equal(first.Id, saved.Id);
        Assert.Equal(1, saved.EditLimit);               // the plan's allowance arrives with the first real save
        Assert.Equal(0, saved.EditsUsed);
    }

    [Fact]
    public void Each_warning_level_goes_out_once_and_tells_the_buyer()
    {
        var store = NewStore();
        var (order, unit) = Delivered(store, users: 1);
        var row = store.EnsureSeatSubmission(Stub(order, unit, 0, "A - 1"));

        var first = store.WarnSeat(row.Id, 1, "admin", "اطلاعات را ثبت کنید");
        Assert.NotNull(first);
        Assert.Equal(1, first!.WarningsSent);
        Assert.Null(store.WarnSeat(row.Id, 1, "admin", "اطلاعات را ثبت کنید"));   // not a second time

        var second = store.WarnSeat(row.Id, 2, "admin", "آخرین هشدار");
        Assert.NotNull(second);
        Assert.Equal(2, second!.WarningsSent);
        Assert.Equal("آخرین هشدار", second.LastWarningText);

        // Both warnings reached the buyer's bell, in the same write that recorded them.
        var notices = store.GetUserNotifications(order.UserId);
        Assert.Equal(2, notices.Count(n => n.Body.Contains("اطلاعات را ثبت کنید") || n.Body.Contains("آخرین هشدار")));
    }

    [Fact]
    public void Closing_one_seat_of_a_shared_account_leaves_the_others_alone()
    {
        var store = NewStore();
        var (order, unit) = Delivered(store, users: 3);
        var roster = new SeatRosterService(store);
        var mine = store.SaveSeatSubmission(Stub(order, unit, 1, "A - 2", "device"))!;

        var revoked = store.RevokeSeat(mine.Id, "admin", "تغییر گذرواژه اکانت")!;

        Assert.True(revoked.Revoked);
        Assert.Equal("تغییر گذرواژه اکانت", revoked.RevokeReason);
        Assert.False(revoked.Editable);
        // A closed seat has no service to set up, so the buyer can no longer file anything for it.
        Assert.Null(store.SaveSeatSubmission(Stub(order, unit, 1, "A - 2", "another try")));
        // The buyer was told, with the reason.
        Assert.Contains(store.GetUserNotifications(order.UserId), n => n.Body.Contains("تغییر گذرواژه اکانت"));

        // Only this seat: the other two are untouched and still waiting on the buyer, not closed.
        Assert.Single(roster.Revoked());
        Assert.Equal(1, roster.Revoked().Single().SeatIndex);
        Assert.Equal(new[] { 0, 2 }, roster.Unregistered().Select(r => r.SeatIndex));
    }

    [Fact]
    public void Reopening_the_warranty_gives_the_seat_back_exactly_as_it_was()
    {
        var store = NewStore();
        var (order, unit) = Delivered(store, users: 2);
        var mine = store.SaveSeatSubmission(Stub(order, unit, 0, "A - 1", "device"))!;
        store.RevokeSeat(mine.Id, "admin", "دلیل");

        var restored = store.RestoreSeat(mine.Id, "admin")!;

        Assert.False(restored.Revoked);
        Assert.Null(restored.RevokeReason);
        Assert.NotNull(restored.RestoredAtUtc);
        Assert.Equal("device", restored.Text);          // the buyer's own entry was never touched
        Assert.Empty(new SeatRosterService(store).Revoked());
        // "Exactly as it was" includes the freeze: details already filed stay frozen, so the buyer gets their
        // seat back in the state they left it, and changing it still needs staff to reopen the entry.
        Assert.False(restored.Editable);
        Assert.Null(store.SaveSeatSubmission(Stub(order, unit, 0, "A - 1", "device v2")));
        store.ReopenSeatSubmission(restored.Id, "دستگاه جدید را بفرستید", "admin");
        Assert.NotNull(store.SaveSeatSubmission(Stub(order, unit, 0, "A - 1", "device v2")));
    }

    [Fact]
    public void A_closed_seat_keeps_its_label_but_its_credentials_leave_the_payload()
    {
        var store = NewStore();
        var (_, unit) = Delivered(store, users: 2);
        var seats = SeatContent.Parse(unit.DeliveryContent);
        Assert.Equal(2, seats.Count);

        var masked = SeatContent.MaskRevoked(unit.DeliveryContent, new[] { 1 });

        // The profile is still there — the buyer has to be able to see WHICH one was closed — but nothing in
        // its block gets anyone into the account any more.
        var after = SeatContent.Parse(masked);
        Assert.Equal(2, after.Count);
        Assert.Equal(seats[1].Label, after[1].Label);
        Assert.Equal("—", after[1].AccountUsername);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(masked, @"Pass\s*:"));
        // The other seat is delivered exactly as before.
        Assert.Equal(seats[0].AccountUsername, after[0].AccountUsername);
    }

    [Fact]
    public void A_one_user_delivery_is_addressed_as_a_single_seat()
    {
        // No seat blocks at all (a plain delivery): index 0 means "this whole account", which is how the panel
        // closes a service that has no profiles to choose between.
        const string plain = "Netflix\nUser : someone@mail.com\nPass : secret";
        var seats = SeatContent.ParseOrWhole(plain);

        Assert.Single(seats);
        Assert.Equal(0, seats[0].Index);
        Assert.Empty(SeatContent.Parse(plain));
        Assert.Equal("", SeatContent.MaskRevoked(plain, new[] { 0 }));
        Assert.Equal(plain, SeatContent.MaskRevoked(plain, Array.Empty<int>()));
    }

    // ── Through the controller ────────────────────────────────────────────────────────────────────

    private sealed class Outbox : IEmailSender
    {
        public List<(string To, string Subject, string Text, string? Html)> Sent { get; } = new();
        public Task<bool> SendAsync(string to, string subject, string body, string? htmlBody = null)
        {
            Sent.Add((to, subject, body, htmlBody));
            return Task.FromResult(true);
        }
    }

    // What an Ok(...) action actually hands back: the payload lives on Result, not on Value.
    private static List<Phonix.Api.Controllers.SeatSubmissionDto> Payload(
        ActionResult<IEnumerable<Phonix.Api.Controllers.SeatSubmissionDto>> result) =>
        ((IEnumerable<Phonix.Api.Controllers.SeatSubmissionDto>)((OkObjectResult)result.Result!).Value!).ToList();

    // The panel's own path: an admin session, with the seats addressed by position exactly as the UI sends them.
    private static (Phonix.Api.Controllers.SeatSubmissionsController Controller, Outbox Outbox) Panel(IDataStore store)
    {
        var outbox = new Outbox();
        var admin = store.GetUsers().First(u => u.Role == UserRole.Admin);
        var controller = new Phonix.Api.Controllers.SeatSubmissionsController(store, new LocalFileStorageService(),
            new UserMailer(store, outbox, NullLogger<UserMailer>.Instance), new SeatRosterService(store))
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
        return (controller, outbox);
    }

    [Fact]
    public void The_panel_closes_the_chosen_seats_and_refuses_anything_that_is_not_a_seat()
    {
        var store = NewStore();
        var (order, unit) = Delivered(store, users: 3);
        var (panel, outbox) = Panel(store);

        // A reason is the whole message to the buyer, so an empty one is refused before anything is written.
        Assert.IsType<BadRequestObjectResult>(panel.Revoke(
            new Phonix.Api.Controllers.SeatRevokeInput(order.Id, unit.Id, new List<int> { 0 }, "   ")).Result);
        // So is a seat the delivery does not have, and an empty selection.
        Assert.IsType<BadRequestObjectResult>(panel.Revoke(
            new Phonix.Api.Controllers.SeatRevokeInput(order.Id, unit.Id, new List<int> { 99 }, "دلیل")).Result);
        Assert.IsType<BadRequestObjectResult>(panel.Revoke(
            new Phonix.Api.Controllers.SeatRevokeInput(order.Id, unit.Id, new List<int>(), "دلیل")).Result);
        Assert.Empty(store.GetSeatSubmissionsForUnit(order.Id, unit.Id));   // nothing was created by a refusal

        var done = Payload(panel.Revoke(new Phonix.Api.Controllers.SeatRevokeInput(
            order.Id, unit.Id, new List<int> { 0, 2 }, "تغییر گذرواژه اکانت")));

        Assert.Equal(2, done.Count);
        Assert.All(done, d => Assert.True(d.Revoked));
        Assert.All(done, d => Assert.Equal("تغییر گذرواژه اکانت", d.RevokeReason));
        // Seats the buyer never filed anything for exist now only because staff acted on them.
        Assert.All(done, d => Assert.Equal(SeatSubmissionStatus.NotFiled, d.Status));
        Assert.Equal(new[] { 0, 2 }, new SeatRosterService(store).Revoked().Select(r => r.SeatIndex));

        // Handing one back leaves the other closed.
        Assert.Single(Payload(panel.Restore(new Phonix.Api.Controllers.SeatTargetInput(
            order.Id, unit.Id, new List<int> { 0 }))));
        Assert.Equal(new[] { 2 }, new SeatRosterService(store).Revoked().Select(r => r.SeatIndex));
        Assert.Contains(outbox.Sent, m => m.Subject.Contains("خروج از گارانتی"));
        Assert.Contains(outbox.Sent, m => m.Subject.Contains("بازگشت گارانتی"));
    }

    [Fact]
    public void The_panel_sends_each_warning_once_and_mails_the_text_it_was_given()
    {
        var store = NewStore();
        var (order, unit) = Delivered(store, users: 2);
        var (panel, outbox) = Panel(store);
        var target = new List<int> { 1 };

        Assert.Single(Payload(panel.Warn(new Phonix.Api.Controllers.SeatWarnInput(
            order.Id, unit.Id, target, 1, "مشخصات دستگاه را ثبت کنید"))));
        // The same level again has nothing left to send, and says so rather than mailing the buyer twice.
        Assert.IsType<BadRequestObjectResult>(panel.Warn(new Phonix.Api.Controllers.SeatWarnInput(
            order.Id, unit.Id, target, 1, "مشخصات دستگاه را ثبت کنید")).Result);
        Assert.IsType<BadRequestObjectResult>(panel.Warn(new Phonix.Api.Controllers.SeatWarnInput(
            order.Id, unit.Id, target, 1, "   ")).Result);          // an empty message is not a warning
        Assert.IsType<BadRequestObjectResult>(panel.Warn(new Phonix.Api.Controllers.SeatWarnInput(
            order.Id, unit.Id, target, 3, "متن")).Result);          // there are only two levels

        var second = Payload(panel.Warn(new Phonix.Api.Controllers.SeatWarnInput(
            order.Id, unit.Id, target, 2, "آخرین هشدار"))).Single();
        Assert.Equal(2, second.WarningsSent);
        // The buyer got the staff's own words, by email as well as in the bell.
        Assert.Contains(outbox.Sent, m => m.Text.Contains("مشخصات دستگاه را ثبت کنید"));
        Assert.Contains(outbox.Sent, m => m.Text.Contains("آخرین هشدار"));
        Assert.Equal(2, store.GetUserNotifications(order.UserId).Count(n => n.Title.Contains("هشدار")));
    }

    [Fact]
    public void Search_finds_a_seat_by_order_code_account_or_what_the_buyer_sent()
    {
        var store = NewStore();
        var (order, unit) = Delivered(store, users: 2);
        store.SaveSeatSubmission(Stub(order, unit, 0, "A - 1", "iPhone 13 Pro"));
        var roster = new SeatRosterService(store);

        Assert.Equal(2, roster.Search(order.Code).Count);          // the whole order comes up
        Assert.Equal(2, roster.Search("shared@mail.com").Count);   // the shared account's login
        Assert.Single(roster.Search("iPhone 13"));                 // what the buyer filed
        // The service name reaches this order too (the seeded shop has orders of its own for it).
        Assert.Contains(roster.Search(unit.Name), r => r.OrderId == order.Id);
        Assert.Empty(roster.Search("nothing-like-this"));
        Assert.Empty(roster.Search("   "));
    }
}

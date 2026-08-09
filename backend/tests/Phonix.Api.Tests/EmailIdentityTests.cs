using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Controllers;
using Phonix.Api.Data;
using Phonix.Api.Dtos;
using Phonix.Api.Models;
using Phonix.Api.Services;
using Xunit;

namespace Phonix.Api.Tests;

// Staff must be able to REMOVE an address, not just correct it — an email can be a typo, belong to somebody
// else entirely, or simply have to go. The rule that a malformed one is refused stays; only "empty" changes
// meaning, from "invalid" to "clear it".
public class AdminClearsEmailTests
{
    private static UsersController Controller(IDataStore store, int callerId)
    {
        var caller = store.GetUser(callerId)!;
        return new UsersController(store, new LocalFileStorageService())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, callerId.ToString()),
                        new Claim(ClaimTypes.Role, nameof(UserRole.Admin)),
                        new Claim(ClaimTypes.Name, caller.Username),
                    }, "test")),
                },
            },
        };
    }

    // The DTO is positional and every field is optional in meaning, so tests name only what they exercise.
    private static UserUpdateInput Update(string? email = null, string? note = null) =>
        new(null, email, null, null, null, null, note, null);

    // An Admin who is not the owner, so GuardTarget lets ordinary customers through.
    private static int AdminId(IDataStore store) =>
        store.GetUsers().First(u => u.Role == UserRole.Admin).Id;

    private static int CustomerId(IDataStore store) =>
        store.GetUsers().First(u => u.Role == UserRole.Customer && u.Email.Length > 0).Id;

    [Fact]
    public void Clearing_an_email_empties_it_and_withdraws_the_verified_flag()
    {
        var store = TestStore.Create();
        var id = CustomerId(store);
        store.UpdateUser(id, u => u.EmailVerified = true);

        var result = Controller(store, AdminId(store)).Update(id, Update(email: ""));

        Assert.IsNotType<BadRequestObjectResult>(result.Result);
        var after = store.GetUser(id)!;
        Assert.Equal("", after.Email);
        // An account with no address has proven nothing, so it must not keep a verified badge — and the
        // checkout's verified-email gate has to close behind it rather than stay open on a stale flag.
        Assert.False(after.EmailVerified);
    }

    [Fact]
    public void Clearing_an_email_keeps_the_rest_of_the_account_intact()
    {
        var store = TestStore.Create();
        var id = CustomerId(store);
        var before = store.GetUser(id)!;

        Controller(store, AdminId(store)).Update(id, Update(email: ""));

        var after = store.GetUser(id)!;
        Assert.Equal(before.Username, after.Username);
        Assert.Equal(before.Wallet, after.Wallet);
        Assert.Equal(before.Role, after.Role);
        Assert.False(after.Blocked);
    }

    [Fact]
    public void A_malformed_email_is_still_refused()
    {
        var store = TestStore.Create();
        var id = CustomerId(store);
        var original = store.GetUser(id)!.Email;

        var result = Controller(store, AdminId(store)).Update(id, Update(email: "not-an-email"));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(original, store.GetUser(id)!.Email);
    }

    [Fact]
    public void An_omitted_email_field_leaves_the_address_alone()
    {
        var store = TestStore.Create();
        var id = CustomerId(store);
        var original = store.GetUser(id)!.Email;
        store.UpdateUser(id, u => u.EmailVerified = true);

        // null means "not supplied" — editing someone's note must not silently reset their email or
        // un-verify them.
        Controller(store, AdminId(store)).Update(id, Update(note: "بررسی شد"));

        var after = store.GetUser(id)!;
        Assert.Equal(original, after.Email);
        Assert.True(after.EmailVerified);
    }

    [Fact]
    public void Several_accounts_can_sit_with_no_email_at_once()
    {
        var store = TestStore.Create();
        var admin = AdminId(store);
        var customers = store.GetUsers().Where(u => u.Role == UserRole.Customer).Take(2).Select(u => u.Id).ToList();
        Assert.Equal(2, customers.Count);

        var controller = Controller(store, admin);
        foreach (var id in customers)
            Assert.IsNotType<BadRequestObjectResult>(controller.Update(id, Update(email: "")).Result);

        // Blank is an absence, not a value, so the uniqueness rule must not treat the second one as a
        // duplicate of the first.
        Assert.All(customers, id => Assert.Equal("", store.GetUser(id)!.Email));
    }

    [Fact]
    public void An_empty_login_identifier_matches_nobody()
    {
        var store = TestStore.Create();
        Controller(store, AdminId(store)).Update(CustomerId(store), Update(email: ""));

        // Username, Email and Phone are all compared against the identifier, and all three are routinely
        // blank — so an empty box must not resolve to whichever row happens to have one.
        Assert.Null(store.FindByLogin(""));
        Assert.Null(store.FindByLogin("   "));
    }
}

// Re-requesting the verification link has to be possible — the first one goes missing to a typo'd address, a
// full mailbox, or an outage on our side — without becoming a way to flood an inbox.
public class VerificationResendLimitTests
{
    private const int PerHour = 5;

    private static int UserId(IDataStore store) => store.GetUsers().First(u => u.Role == UserRole.Customer).Id;

    [Fact]
    public void The_allowance_is_spent_and_then_refused()
    {
        var store = TestStore.Create();
        var id = UserId(store);

        for (var i = 1; i <= PerHour; i++)
            Assert.True(store.TryConsumeVerificationSend(id, PerHour).Allowed, $"send {i} should be allowed");

        var (allowed, retryAt) = store.TryConsumeVerificationSend(id, PerHour);
        Assert.False(allowed);
        // The caller turns this into "try again in N minutes", so a refusal without it would leave the
        // customer clicking blindly.
        Assert.NotNull(retryAt);
        Assert.InRange(retryAt!.Value, DateTime.UtcNow.AddMinutes(58), DateTime.UtcNow.AddMinutes(61));
    }

    [Fact]
    public void The_allowance_is_per_account_not_shared()
    {
        var store = TestStore.Create();
        var customers = store.GetUsers().Where(u => u.Role == UserRole.Customer).Take(2).Select(u => u.Id).ToList();

        for (var i = 0; i < PerHour; i++) store.TryConsumeVerificationSend(customers[0], PerHour);

        Assert.False(store.TryConsumeVerificationSend(customers[0], PerHour).Allowed);
        // One customer exhausting theirs must not lock out anybody else.
        Assert.True(store.TryConsumeVerificationSend(customers[1], PerHour).Allowed);
    }

    [Fact]
    public void The_allowance_survives_a_restart()
    {
        var store = TestStore.Create(out var dbPath);
        var id = UserId(store);
        for (var i = 0; i < PerHour; i++) store.TryConsumeVerificationSend(id, PerHour);

        // An in-memory counter would hand out five more on every deploy, which is no limit at all.
        Assert.False(TestStore.Reopen(dbPath).TryConsumeVerificationSend(id, PerHour).Allowed);
    }

    [Fact]
    public void Sends_that_have_aged_out_of_the_window_free_up_their_slots()
    {
        var store = TestStore.Create();
        var id = UserId(store);

        // Four sends from just over an hour ago plus one recent: only the recent one still counts.
        var old = DateTime.UtcNow.AddMinutes(-61);
        store.UpdateUser(id, u => u.VerificationSendsUtc = new List<DateTime> { old, old, old, old });
        Assert.True(store.TryConsumeVerificationSend(id, PerHour).Allowed);

        // A rolling window, not a fixed hour — so the whole allowance is available again rather than the
        // user being stuck until some arbitrary clock boundary.
        for (var i = 0; i < PerHour - 1; i++)
            Assert.True(store.TryConsumeVerificationSend(id, PerHour).Allowed);
        Assert.False(store.TryConsumeVerificationSend(id, PerHour).Allowed);
    }

    [Fact]
    public void A_refusal_still_prunes_so_the_record_cannot_grow_without_bound()
    {
        var store = TestStore.Create();
        var id = UserId(store);
        var old = DateTime.UtcNow.AddHours(-5);
        store.UpdateUser(id, u => u.VerificationSendsUtc = Enumerable.Repeat(old, 50).ToList());

        Assert.True(store.TryConsumeVerificationSend(id, PerHour).Allowed);
        Assert.Single(store.GetUser(id)!.VerificationSendsUtc);
    }

    [Fact]
    public void A_limit_of_zero_leaves_the_gate_open()
    {
        var store = TestStore.Create();
        var id = UserId(store);
        // Nothing configures this to 0 today, but a "limit" that silently locked every account out of ever
        // verifying would be the worst possible reading of it.
        for (var i = 0; i < 20; i++)
            Assert.True(store.TryConsumeVerificationSend(id, 0).Allowed);
    }
}

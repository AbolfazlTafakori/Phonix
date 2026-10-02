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
    private static UsersController Controller(IDataStore store, int callerId, RecordingEmailSender? sender = null)
    {
        var caller = store.GetUser(callerId)!;
        return new UsersController(store, new LocalFileStorageService(), sender ?? new RecordingEmailSender())
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
    public async Task Clearing_an_email_empties_it_and_withdraws_the_verified_flag()
    {
        var store = TestStore.Create();
        var id = CustomerId(store);
        store.UpdateUser(id, u => u.EmailVerified = true);

        var result = await Controller(store, AdminId(store)).Update(id, Update(email: ""));

        Assert.IsNotType<BadRequestObjectResult>(result.Result);
        var after = store.GetUser(id)!;
        Assert.Equal("", after.Email);
        // An account with no address has proven nothing, so it must not keep a verified badge — and the
        // checkout's verified-email gate has to close behind it rather than stay open on a stale flag.
        Assert.False(after.EmailVerified);
    }

    [Fact]
    public async Task Clearing_an_email_keeps_the_rest_of_the_account_intact()
    {
        var store = TestStore.Create();
        var id = CustomerId(store);
        var before = store.GetUser(id)!;

        await Controller(store, AdminId(store)).Update(id, Update(email: ""));

        var after = store.GetUser(id)!;
        Assert.Equal(before.Username, after.Username);
        Assert.Equal(before.Wallet, after.Wallet);
        Assert.Equal(before.Role, after.Role);
        Assert.False(after.Blocked);
    }

    [Fact]
    public async Task A_malformed_email_is_still_refused()
    {
        var store = TestStore.Create();
        var id = CustomerId(store);
        var original = store.GetUser(id)!.Email;

        var result = await Controller(store, AdminId(store)).Update(id, Update(email: "not-an-email"));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(original, store.GetUser(id)!.Email);
    }

    [Fact]
    public async Task An_omitted_email_field_leaves_the_address_alone()
    {
        var store = TestStore.Create();
        var id = CustomerId(store);
        var original = store.GetUser(id)!.Email;
        store.UpdateUser(id, u => u.EmailVerified = true);

        // null means "not supplied" — editing someone's note must not silently reset their email or
        // un-verify them.
        await Controller(store, AdminId(store)).Update(id, Update(note: "بررسی شد"));

        var after = store.GetUser(id)!;
        Assert.Equal(original, after.Email);
        Assert.True(after.EmailVerified);
    }

    [Fact]
    public async Task Several_accounts_can_sit_with_no_email_at_once()
    {
        var store = TestStore.Create();
        var admin = AdminId(store);
        var customers = store.GetUsers().Where(u => u.Role == UserRole.Customer).Take(2).Select(u => u.Id).ToList();
        Assert.Equal(2, customers.Count);

        var controller = Controller(store, admin);
        foreach (var id in customers)
            Assert.IsNotType<BadRequestObjectResult>((await controller.Update(id, Update(email: ""))).Result);

        // Blank is an absence, not a value, so the uniqueness rule must not treat the second one as a
        // duplicate of the first.
        Assert.All(customers, id => Assert.Equal("", store.GetUser(id)!.Email));
    }

    [Fact]
    public async Task An_empty_login_identifier_matches_nobody()
    {
        var store = TestStore.Create();
        await Controller(store, AdminId(store)).Update(CustomerId(store), Update(email: ""));

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

internal sealed class RecordingEmailSender : IEmailSender
{
    public List<(string To, string Subject, string Body)> Sent { get; } = new();
    public Task<bool> SendAsync(string to, string subject, string body, string? htmlBody = null)
    {
        Sent.Add((to, subject, body));
        return Task.FromResult(true);
    }
}

// Staff set a customer's email from the panel whatever state the old one was in. The new address applies at
// once and its verification link goes out by itself; a verified old address is also told about the change.
public class AdminSetsEmailTests
{
    private static UsersController Controller(IDataStore store, RecordingEmailSender sender)
    {
        var admin = store.GetUsers().First(u => u.Role == UserRole.Admin);
        return new UsersController(store, new LocalFileStorageService(), sender)
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

    private static UserUpdateInput Update(string email) => new(null, email, null, null, null, null, null, null);

    private static AppUser Customer(IDataStore store) =>
        store.GetUsers().First(u => u.Role == UserRole.Customer && u.Email.Length > 0);

    [Fact]
    public async Task Replacing_a_verified_email_mails_the_link_to_the_new_one_and_tells_the_old_one()
    {
        var store = TestStore.Create();
        var customer = Customer(store);
        store.UpdateUser(customer.Id, u => u.EmailVerified = true);
        var sender = new RecordingEmailSender();

        var result = await Controller(store, sender).Update(customer.Id, Update("fresh.inbox@example.com"));

        Assert.IsNotType<BadRequestObjectResult>(result.Result);
        var after = store.GetUser(customer.Id)!;
        Assert.Equal("fresh.inbox@example.com", after.Email);
        Assert.False(after.EmailVerified);
        Assert.Contains(sender.Sent, m => m.To == "fresh.inbox@example.com" && m.Body.Contains("/verify-email?token="));
        Assert.Contains(sender.Sent, m => m.To == customer.Email && !m.Body.Contains("/verify-email"));
        Assert.Equal(2, sender.Sent.Count);
    }

    [Fact]
    public async Task Replacing_an_unverified_email_only_mails_the_new_one()
    {
        var store = TestStore.Create();
        var customer = Customer(store);
        store.UpdateUser(customer.Id, u => u.EmailVerified = false);
        var sender = new RecordingEmailSender();

        await Controller(store, sender).Update(customer.Id, Update("fresh.inbox@example.com"));

        Assert.Equal("fresh.inbox@example.com", store.GetUser(customer.Id)!.Email);
        // The old address was never proven to be theirs, so it gets nothing.
        var only = Assert.Single(sender.Sent);
        Assert.Equal("fresh.inbox@example.com", only.To);
        Assert.Contains("/verify-email?token=", only.Body);
    }

    [Fact]
    public async Task The_link_is_bound_to_the_address_it_was_sent_to()
    {
        var store = TestStore.Create();
        var customer = Customer(store);
        var sender = new RecordingEmailSender();

        await Controller(store, sender).Update(customer.Id, Update("fresh.inbox@example.com"));

        var token = sender.Sent.Single(m => m.To == "fresh.inbox@example.com").Body.Split("token=")[1].Split(new[] { '\n', ' ' })[0].Trim();
        Assert.Equal((customer.Id, "fresh.inbox@example.com"), store.ConsumeTokenWithData(token, "verify"));
    }

    [Fact]
    public async Task Saving_the_same_email_again_sends_nothing_and_keeps_it_verified()
    {
        var store = TestStore.Create();
        var customer = Customer(store);
        store.UpdateUser(customer.Id, u => u.EmailVerified = true);
        var sender = new RecordingEmailSender();

        // The drawer posts every field on save, so editing just the name re-sends the unchanged email.
        await Controller(store, sender).Update(customer.Id, Update(customer.Email.ToUpperInvariant()));

        Assert.Empty(sender.Sent);
        Assert.True(store.GetUser(customer.Id)!.EmailVerified);
    }

    [Fact]
    public async Task Staff_can_resend_the_link_to_an_unverified_address()
    {
        var store = TestStore.Create();
        var customer = Customer(store);
        store.UpdateUser(customer.Id, u => u.EmailVerified = false);
        var sender = new RecordingEmailSender();

        var result = await Controller(store, sender).SendVerification(customer.Id);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(customer.Email, Assert.Single(sender.Sent).To);
    }

    [Fact]
    public async Task Resending_to_a_verified_address_is_refused()
    {
        var store = TestStore.Create();
        var customer = Customer(store);
        store.UpdateUser(customer.Id, u => u.EmailVerified = true);
        var sender = new RecordingEmailSender();

        Assert.IsType<BadRequestObjectResult>(await Controller(store, sender).SendVerification(customer.Id));
        Assert.Empty(sender.Sent);
    }
}

// A customer with a payment on record is the money ledger's, not the panel's, to remove: the database refuses
// the delete by foreign key, and the panel has to say why instead of answering with a bare 500.
public class DeleteUserWithTransactionsTests
{
    private static UsersController Controller(IDataStore store)
    {
        var admin = store.GetUsers().First(u => u.Role == UserRole.Admin);
        return new UsersController(store, new LocalFileStorageService(), new RecordingEmailSender())
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
    public void A_customer_with_a_transaction_is_refused_with_a_reason_and_kept()
    {
        var store = TestStore.Create();
        var customer = store.GetUsers().First(u => u.Role == UserRole.Customer);
        store.AddTransaction(new Transaction
        {
            UserId = customer.Id, UserName = customer.Name, Type = TxTypes.AdminAdjustment, Amount = 1000,
            Status = TxStatus.Approved, Method = "test",
        });

        var result = Controller(store).Delete(customer.Id);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains("تراکنش", (string)conflict.Value!);
        Assert.NotNull(store.GetUser(customer.Id));
    }

    [Fact]
    public void A_customer_with_no_transactions_is_still_deleted()
    {
        var store = TestStore.Create();
        var customer = store.GetUsers().First(u => u.Role == UserRole.Customer && store.GetUserTransactions(u.Id).Count == 0);

        Assert.IsType<NoContentResult>(Controller(store).Delete(customer.Id));
        Assert.Null(store.GetUser(customer.Id));
    }
}

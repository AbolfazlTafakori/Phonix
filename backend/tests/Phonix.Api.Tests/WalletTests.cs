using Phonix.Api.Models;
using Xunit;

namespace Phonix.Api.Tests;

// Covers the wallet top-up money path: approving a "شارژ کیف پول" transaction must credit the
// owner's balance exactly once, and reversing it must debit it back.
public class WalletTests
{
    private const string TopUp = "شارژ کیف پول";

    [Fact]
    public void Approving_a_topup_credits_the_owner_once()
    {
        var store = TestStore.Create();
        var start = store.GetUser(1)!.Wallet;
        var tx = store.AddTransaction(new Transaction { UserId = 1, Type = TopUp, Amount = 150_000, Status = TxStatus.Pending });

        store.SetTransactionStatus(tx.Id, TxStatus.Approved, "site", null);
        Assert.Equal(start + 150_000, store.GetUser(1)!.Wallet);

        // re-approving the same transaction must not credit a second time
        store.SetTransactionStatus(tx.Id, TxStatus.Approved, "site", null);
        Assert.Equal(start + 150_000, store.GetUser(1)!.Wallet);
    }

    [Fact]
    public void Reversing_an_approved_topup_debits_it_back()
    {
        var store = TestStore.Create();
        var start = store.GetUser(1)!.Wallet;
        var tx = store.AddTransaction(new Transaction { UserId = 1, Type = TopUp, Amount = 80_000, Status = TxStatus.Pending });

        store.SetTransactionStatus(tx.Id, TxStatus.Approved, "site", null);
        store.SetTransactionStatus(tx.Id, TxStatus.Rejected, "site", null);

        Assert.Equal(start, store.GetUser(1)!.Wallet);
    }

    [Fact]
    public void A_topup_that_is_never_approved_does_not_change_the_balance()
    {
        var store = TestStore.Create();
        var start = store.GetUser(1)!.Wallet;
        var tx = store.AddTransaction(new Transaction { UserId = 1, Type = TopUp, Amount = 90_000, Status = TxStatus.Pending });

        store.SetTransactionStatus(tx.Id, TxStatus.Rejected, "site", null);

        Assert.Equal(start, store.GetUser(1)!.Wallet);
    }

    [Fact]
    public void User_transactions_are_scoped_by_user_id()
    {
        var store = TestStore.Create();
        store.AddTransaction(new Transaction { UserId = 1, Type = TopUp, Amount = 10_000, Status = TxStatus.Pending });
        store.AddTransaction(new Transaction { UserId = 2, Type = TopUp, Amount = 20_000, Status = TxStatus.Pending });

        var mine = store.GetUserTransactions(1);

        Assert.All(mine, t => Assert.Equal(1, t.UserId));
        Assert.Contains(mine, t => t.Amount == 10_000);
        Assert.DoesNotContain(mine, t => t.UserId == 2);
    }

    // Withdrawals are the inverse path: the balance is held (debited) the moment the request is filed,
    // approval just confirms the payout, and rejection returns the held funds.
    [Fact]
    public void Requesting_a_withdrawal_holds_the_funds_immediately()
    {
        var store = TestStore.Create();
        var start = store.GetUser(1)!.Wallet;

        var result = store.RequestWithdrawal(1, 100_000, "6037-0000-0000-0000");

        Assert.Null(result.Error);
        Assert.Equal(start - 100_000, store.GetUser(1)!.Wallet);
        Assert.Equal(TxStatus.Pending, result.Tx!.Status);
        Assert.Equal(-100_000, result.Tx.Amount);
    }

    [Fact]
    public void Approving_a_withdrawal_keeps_the_funds_debited()
    {
        var store = TestStore.Create();
        var start = store.GetUser(1)!.Wallet;
        var tx = store.RequestWithdrawal(1, 120_000, "6037-0000-0000-0000").Tx!;

        store.SetTransactionStatus(tx.Id, TxStatus.Approved, "site", null);

        // already held at request time — approval must not debit a second time
        Assert.Equal(start - 120_000, store.GetUser(1)!.Wallet);
    }

    [Fact]
    public void Rejecting_a_withdrawal_refunds_the_held_funds()
    {
        var store = TestStore.Create();
        var start = store.GetUser(1)!.Wallet;
        var tx = store.RequestWithdrawal(1, 90_000, "6037-0000-0000-0000").Tx!;

        store.SetTransactionStatus(tx.Id, TxStatus.Rejected, "site", null);
        Assert.Equal(start, store.GetUser(1)!.Wallet);

        // re-rejecting must not refund a second time
        store.SetTransactionStatus(tx.Id, TxStatus.Rejected, "site", null);
        Assert.Equal(start, store.GetUser(1)!.Wallet);
    }

    [Fact]
    public void A_withdrawal_over_the_balance_is_rejected_and_leaves_the_balance_untouched()
    {
        var store = TestStore.Create();
        var start = store.GetUser(1)!.Wallet;

        var result = store.RequestWithdrawal(1, start + 1, "6037-0000-0000-0000");

        Assert.NotNull(result.Error);
        Assert.Null(result.Tx);
        Assert.Equal(start, store.GetUser(1)!.Wallet);
    }
}

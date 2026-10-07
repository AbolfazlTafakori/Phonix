using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Services;
using Xunit;

namespace Phonix.Api.Tests;

// A receipt, a card, identity documents or an order's account is decided once — in the panel or in a Telegram bot,
// whichever is first. The other side never acts on it again, and says where it was decided.
public class DecisionSyncTests
{
    private static int ApprovedCard(IDataStore store, int userId)
    {
        var card = store.AddCard(userId, "6037991234567893", "علی محمدی", "/uploads/card.png").Card!;
        store.SetCardStatus(card.Id, BankCardStatus.Approved, null);
        return card.Id;
    }

    // A card-to-card order with its receipt still waiting for a decision.
    private static (Order Order, Transaction Payment) PendingOrder(IDataStore store, int accounts = 1)
    {
        var user = store.GetUser(1)!;
        var planId = store.GetProduct(1)!.Plans.First(p => p.IsActive).Id;
        var placed = store.PlaceOrder(user, new[] { (1, accounts, (int?)planId) }, "درگاه", fromWallet: false, paymentMethodId: 3,
            payment: new RemainderPayment(ApprovedCard(store, 1), "/uploads/r.png", "TRK-1", "1403/03/22", null), customerCheckout: true);
        var order = placed.Order!;
        return (order, store.GetTransactions().First(t => t.OrderCode == order.Code && t.Type == TxTypes.OrderPayment));
    }

    [Fact]
    public void Only_the_first_decision_on_a_receipt_counts()
    {
        var store = TestStore.Create();
        var tx = store.AddTransaction(new Transaction { UserId = 1, Type = TxTypes.WalletTopUp, Amount = 100_000, Status = TxStatus.Pending });
        var walletBefore = store.GetUser(1)!.Wallet;

        var first = store.DecideTransaction(tx.Id, TxStatus.Approved, DecisionVia.Telegram, null, "@maryam");
        var second = store.DecideTransaction(tx.Id, TxStatus.Rejected, DecisionVia.Site, "اشتباه", "reza");

        Assert.True(first.Applied);
        Assert.False(second.Applied);
        Assert.Equal(TxStatus.Approved, second.Item!.Status);
        Assert.Equal(DecisionVia.Telegram, second.Item.ApprovedVia);
        Assert.Equal("@maryam", second.Item.DecidedBy);
        Assert.Equal(walletBefore + 100_000, store.GetUser(1)!.Wallet);   // credited once, never taken back
        Assert.Equal("این تراکنش قبلاً از طریق تلگرام (@maryam) تأیید شده است.",
            DecisionVia.AlreadyDecided("تراکنش", DecisionVia.Outcome(second.Item.Status),
                DecisionVia.Describe(second.Item.ApprovedVia, second.Item.DecidedBy)));
    }

    [Fact]
    public void Only_the_first_decision_on_a_card_or_identity_documents_counts()
    {
        var store = TestStore.Create();
        var card = store.AddCard(1, "6037991234567893", "علی محمدی", "/uploads/card.png").Card!;

        Assert.True(store.DecideCard(card.Id, BankCardStatus.Rejected, "ناخوانا", DecisionVia.Site, "reza").Applied);
        var late = store.DecideCard(card.Id, BankCardStatus.Approved, null, DecisionVia.Telegram, "@maryam");

        Assert.False(late.Applied);
        Assert.Equal(BankCardStatus.Rejected, store.GetCard(card.Id)!.Status);
        Assert.Equal("reza", late.Item!.DecidedBy);
    }

    // Rejected means refunded — delivering it too would hand over both the money and the account.
    [Fact]
    public void A_rejected_account_is_never_delivered()
    {
        var store = TestStore.Create();
        // Two accounts, so rejecting one leaves the order open around the other.
        var (order, payment) = PendingOrder(store, accounts: 2);
        store.DecideTransaction(payment.Id, TxStatus.Approved, DecisionVia.Site, null, "reza");
        var unit = store.GetOrder(order.Id)!.Units.First(u => !u.Delivered);
        var (_, _, error) = store.RejectUnit(order.Id, unit.Id, "موجود نیست", DecisionVia.Telegram);
        Assert.Null(error);

        var (delivered, _) = store.DeliverUnit(order.Id, unit.Id, "user:pass", "reza");

        Assert.Null(delivered);
        var after = store.GetOrder(order.Id)!.Units.First(u => u.Id == unit.Id);
        Assert.True(after.Rejected);
        Assert.False(after.Delivered);
        // And trying to reject it again says where it was rejected.
        Assert.Contains("از طریق تلگرام", store.RejectUnit(order.Id, unit.Id, "x", "reza").error);
    }

    // The receipt was rejected in Telegram, which cancelled the order. «تأیید رسید» on the orders page must not
    // bring it back with no payment behind it.
    [Fact]
    public void The_orders_page_cannot_approve_a_receipt_rejected_in_telegram()
    {
        var store = TestStore.Create();
        var (order, payment) = PendingOrder(store);
        store.DecideTransaction(payment.Id, TxStatus.Rejected, DecisionVia.Telegram, "تکراری", "@maryam");

        var approval = store.ApproveOrderPayment(order.Id, "reza");

        Assert.Null(approval.Order);
        Assert.Contains("از طریق تلگرام (@maryam) رد شده", approval.Error);
        Assert.Equal(OrderStatus.Cancelled, store.GetOrder(order.Id)!.Status);
    }

    [Fact]
    public void Approving_from_the_orders_page_records_it_on_the_receipt()
    {
        var store = TestStore.Create();
        var (order, payment) = PendingOrder(store);

        var approval = store.ApproveOrderPayment(order.Id, "reza");

        Assert.Equal(OrderStatus.Preparing, approval.Order!.Status);
        Assert.Equal(payment.Id, approval.SettledPayment!.Id);
        var tx = store.GetTransaction(payment.Id)!;
        Assert.Equal(TxStatus.Approved, tx.Status);
        Assert.Equal(DecisionVia.Site, tx.ApprovedVia);
        Assert.Equal("reza", tx.DecidedBy);
        // …so the receipt bot, tapped afterwards, is told it is done.
        Assert.False(store.DecideTransaction(payment.Id, TxStatus.Rejected, DecisionVia.Telegram, "x", "@maryam").Applied);
    }

    // A cancelled order's receipt must not sit in the receipts group waiting to be approved for nothing.
    [Fact]
    public void Cancelling_an_order_closes_its_pending_receipt()
    {
        var store = TestStore.Create();
        var (order, payment) = PendingOrder(store);

        var cancel = store.CancelOrder(order.Id, "ali", "لغو توسط کاربر", onlyIfAwaitingPayment: true);

        Assert.Null(cancel.Error);
        Assert.Equal(payment.Id, cancel.SettledPayment!.Id);
        var tx = store.GetTransaction(payment.Id)!;
        Assert.Equal(TxStatus.Rejected, tx.Status);
        Assert.Contains("لغو توسط کاربر", tx.Note);
        Assert.False(store.DecideTransaction(payment.Id, TxStatus.Approved, DecisionVia.Telegram, null, "@maryam").Applied);
    }

    // The customer pressed cancel just after the receipt was approved in Telegram: the approval stands.
    [Fact]
    public void A_customer_cannot_cancel_once_the_payment_is_approved()
    {
        var store = TestStore.Create();
        var (order, payment) = PendingOrder(store);
        store.DecideTransaction(payment.Id, TxStatus.Approved, DecisionVia.Telegram, null, "@maryam");

        var cancel = store.CancelOrder(order.Id, "ali", "لغو توسط کاربر", onlyIfAwaitingPayment: true);

        Assert.NotNull(cancel.Error);
        Assert.Contains("از طریق تلگرام", cancel.Error);
        Assert.NotEqual(OrderStatus.Cancelled, store.GetOrder(order.Id)!.Status);
    }

    // Rejecting a receipt from its own Telegram message cancels the order; it must not also try to close that same
    // receipt a second time through the cancellation.
    [Fact]
    public void A_receipt_rejection_keeps_its_own_reason()
    {
        var store = TestStore.Create();
        var (order, payment) = PendingOrder(store);

        store.DecideTransaction(payment.Id, TxStatus.Rejected, DecisionVia.Telegram, "رسید تکراری است", "@maryam");

        var tx = store.GetTransaction(payment.Id)!;
        Assert.Equal("رسید تکراری است", tx.Note);
        Assert.Equal(DecisionVia.Telegram, tx.ApprovedVia);
        Assert.Equal(OrderStatus.Cancelled, store.GetOrder(order.Id)!.Status);
    }
}

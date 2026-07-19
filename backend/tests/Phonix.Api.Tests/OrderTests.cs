using Phonix.Api.Data;
using Phonix.Api.Models;
using Xunit;

namespace Phonix.Api.Tests;

// Exercises the critical money/stock paths against a fresh seeded store.
// Seed reference: product 1 = Netflix (stock 142), product 4 = Binance (stock 0),
// user 1 = ali (wallet 180,000), user 5 = reza (wallet 920,000).
public class OrderTests
{
    [Fact]
    public void PlaceOrder_decrements_stock_and_computes_total()
    {
        var store = TestStore.Create();
        var startStock = store.GetProduct(1)!.Stock;
        var unit = store.GetProduct(1)!.FinalPrice;
        var user = store.GetUser(1)!;
        var vat = Vat(store, unit * 2);

        var res = store.PlaceOrder(user, new[] { (1, 2, (int?)null) }, "کارت به کارت", fromWallet: false);

        Assert.Null(res.Error);
        Assert.NotNull(res.Order);
        Assert.Equal(vat, res.Order!.VatAmount);
        Assert.Equal(unit * 2 + vat, res.Order.Total);
        Assert.Equal(startStock - 2, store.GetProduct(1)!.Stock);
        Assert.Equal(OrderStatus.PendingApproval, res.Order.Status);
    }

    [Fact]
    public void PlaceOrder_rejects_when_stock_is_insufficient()
    {
        var store = TestStore.Create();
        Assert.Equal(0, store.GetProduct(4)!.Stock); // Binance starts out of stock
        var user = store.GetUser(1)!;

        var res = store.PlaceOrder(user, new[] { (4, 1, (int?)null) }, "کارت", fromWallet: false);

        Assert.NotNull(res.Error);
        Assert.Null(res.Order);
        Assert.Equal(0, store.GetProduct(4)!.Stock); // unchanged — no partial decrement
    }

    [Fact]
    public void PlaceOrder_fully_from_wallet_deducts_and_marks_preparing()
    {
        var store = TestStore.Create();
        var user = store.GetUser(5)!; // reza has enough balance
        var startWallet = user.Wallet;
        var price = store.GetProduct(1)!.FinalPrice;
        var payable = price + Vat(store, price);

        var res = store.PlaceOrder(user, new[] { (1, 1, (int?)null) }, "wallet", fromWallet: true);

        Assert.Null(res.Error);
        Assert.Equal(payable, res.Order!.WalletPaid);
        Assert.Equal(OrderStatus.Preparing, res.Order.Status); // no remainder → no approval needed
        Assert.Equal(startWallet - payable, store.GetUser(5)!.Wallet);
    }

    [Fact]
    public void PlaceOrder_applies_a_percent_discount_code()
    {
        var store = TestStore.Create();
        var user = store.GetUser(1)!;
        var unit = store.GetProduct(1)!.FinalPrice;
        var expectedDiscount = (long)Math.Round(unit * 10 / 100.0); // WELCOME10 = 10%

        var res = store.PlaceOrder(user, new[] { (1, 1, (int?)null) }, "کارت", fromWallet: false, discountCode: "WELCOME10");

        var goods = unit - expectedDiscount;
        Assert.Null(res.Error);
        Assert.Equal(expectedDiscount, res.Order!.DiscountAmount);
        Assert.Equal(goods + Vat(store, goods), res.Order.Total);
    }

    [Fact]
    public void CancelOrder_restores_stock_and_refunds_wallet()
    {
        var store = TestStore.Create();
        var user = store.GetUser(5)!;
        var startStock = store.GetProduct(1)!.Stock;

        var placed = store.PlaceOrder(user, new[] { (1, 2, (int?)null) }, "wallet", fromWallet: true);
        Assert.Equal(startStock - 2, store.GetProduct(1)!.Stock);
        var walletAfterOrder = store.GetUser(5)!.Wallet;

        var cancel = store.CancelOrder(placed.Order!.Id);

        Assert.Null(cancel.Error);
        Assert.Equal(OrderStatus.Cancelled, cancel.Order!.Status);
        Assert.Equal(startStock, store.GetProduct(1)!.Stock);     // stock restored
        Assert.True(store.GetUser(5)!.Wallet > walletAfterOrder);  // refund credited back
    }

    [Fact]
    public void CancelOrder_twice_is_rejected()
    {
        var store = TestStore.Create();
        var user = store.GetUser(1)!;
        var placed = store.PlaceOrder(user, new[] { (1, 1, (int?)null) }, "کارت", fromWallet: false);

        Assert.Null(store.CancelOrder(placed.Order!.Id).Error);
        Assert.NotNull(store.CancelOrder(placed.Order!.Id).Error); // already cancelled
    }

    [Fact]
    public void PlaceOrder_partial_wallet_charges_the_exact_remainder_plus_fee()
    {
        var store = TestStore.Create();
        var user = store.GetUser(1)!;          // ali — wallet is smaller than the order
        var startWallet = user.Wallet;
        var price = store.GetProduct(1)!.FinalPrice;

        // qty 2 guarantees the goods exceed the wallet, so a gateway remainder always exists.
        var res = store.PlaceOrder(user, new[] { (1, 2, (int?)null) }, "کیف پول + درگاه", fromWallet: true, paymentMethodId: 3); // method 3 = 3% fee

        Assert.Null(res.Error);
        var o = res.Order!;
        Assert.Equal(startWallet, o.WalletPaid);                       // whole wallet consumed, no more
        var goodsRemainder = price * 2 + Vat(store, price * 2) - startWallet;
        var expectedFee = (long)Math.Round(goodsRemainder * 3 / 100.0, MidpointRounding.AwayFromZero);
        Assert.Equal(expectedFee, o.FeeAmount);
        Assert.Equal(goodsRemainder + expectedFee, o.Total - o.WalletPaid); // the exact figure the buyer pays at the gateway
        Assert.Equal(0, store.GetUser(1)!.Wallet);                      // wallet emptied to the toman
        Assert.Equal(OrderStatus.PendingApproval, o.Status);           // remainder still owed
    }

    // There is no self-service "pay" path: an order with a gateway remainder stays PendingApproval
    // until an admin moves it forward. This guards against re-introducing the removed self-credit hole.
    [Fact]
    public void A_pending_order_is_settled_only_by_admin_approval()
    {
        var store = TestStore.Create();
        var user = store.GetUser(1)!;
        var placed = store.PlaceOrder(user, new[] { (1, 1, (int?)null) }, "درگاه", fromWallet: false, paymentMethodId: 3);
        Assert.Equal(OrderStatus.PendingApproval, placed.Order!.Status);

        var approved = store.SetOrderStatus(placed.Order.Id, OrderStatus.Preparing);
        Assert.Equal(OrderStatus.Preparing, approved!.Status);
    }

    // A real customer checkout that leaves a card-to-card remainder must pick a method and attach a
    // receipt BEFORE the order is filed — and a rejected checkout must not touch the wallet or stock.
    [Fact]
    public void Customer_checkout_with_a_remainder_and_no_method_is_rejected_without_side_effects()
    {
        var store = TestStore.Create();
        var user = store.GetUser(1)!;
        var startWallet = user.Wallet;
        var startStock = store.GetProduct(1)!.Stock;

        // wallet (180k) partially covers Netflix (290k) → a remainder is due, but no method chosen.
        var res = store.PlaceOrder(user, new[] { (1, 1, (int?)null) }, "کیف پول", fromWallet: true, paymentMethodId: null, customerCheckout: true);

        Assert.NotNull(res.Error);
        Assert.Null(res.Order);
        Assert.Equal(startWallet, store.GetUser(1)!.Wallet);   // wallet untouched
        Assert.Equal(startStock, store.GetProduct(1)!.Stock);  // stock untouched
    }

    [Fact]
    public void Customer_checkout_with_a_remainder_and_no_receipt_is_rejected()
    {
        var store = TestStore.Create();
        var user = store.GetUser(1)!;
        var cardId = ApprovedCard(store, 1);

        // card + tracking + date present but no receipt, while receipts are required (seed default).
        var res = store.PlaceOrder(user, new[] { (1, 1, (int?)null) }, "درگاه", fromWallet: false, paymentMethodId: 3,
            payment: new RemainderPayment(cardId, null, "TRK-1", "1403/03/22", null), customerCheckout: true);

        Assert.NotNull(res.Error);
        Assert.Null(res.Order);
    }

    [Fact]
    public void Customer_checkout_with_a_remainder_requires_an_approved_card()
    {
        var store = TestStore.Create();
        var user = store.GetUser(1)!;

        // method + tracking + date + receipt but no registered card → rejected.
        var res = store.PlaceOrder(user, new[] { (1, 1, (int?)null) }, "درگاه", fromWallet: false, paymentMethodId: 3,
            payment: new RemainderPayment(null, "/uploads/r.png", "TRK-1", "1403/03/22", null), customerCheckout: true);

        Assert.NotNull(res.Error);
        Assert.Null(res.Order);
    }

    [Fact]
    public void Customer_checkout_remainder_files_a_pending_order_payment_that_advances_the_order_on_approval()
    {
        var store = TestStore.Create();
        var user = store.GetUser(1)!;
        var cardId = ApprovedCard(store, 1);

        // A real customer checkout always carries the chosen plan — a plan-priced product can't be ordered without one.
        var planId = store.GetProduct(1)!.Plans.First(p => p.IsActive).Id;
        var placed = store.PlaceOrder(user, new[] { (1, 1, (int?)planId) }, "درگاه", fromWallet: false, paymentMethodId: 3,
            payment: new RemainderPayment(cardId, "/uploads/r.png", "TRK-1", "1403/03/22", null), customerCheckout: true);

        Assert.Null(placed.Error);
        Assert.Equal(OrderStatus.PendingApproval, placed.Order!.Status);
        Assert.Equal("/uploads/r.png", placed.Order.ReceiptUrl);

        // a pending "پرداخت سفارش" transaction is created for the remainder, linked to the order.
        var tx = store.GetTransactions().First(t => t.OrderCode == placed.Order.Code && t.Type == TxTypes.OrderPayment);
        Assert.Equal(TxStatus.Pending, tx.Status);
        Assert.Equal("/uploads/r.png", tx.ReceiptUrl);

        // approving that payment advances the order to Preparing.
        store.SetTransactionStatus(tx.Id, TxStatus.Approved, "site", null);
        Assert.Equal(OrderStatus.Preparing, store.GetOrder(placed.Order.Id)!.Status);
    }

    // Identity-level gate: level 0 (registered only) buys nothing; approving a card grants level 1;
    // a level-2 product needs level 2. (Seed: user 6 = negar level 0, user 1 = ali level 2,
    // product 7 = Instagram verify RequiredLevel 2 with stock.)
    [Fact]
    public void A_level_zero_user_cannot_purchase_anything()
    {
        var store = TestStore.Create();
        var res = store.PlaceOrder(store.GetUser(6)!, new[] { (1, 1, (int?)null) }, "کارت بانکی", fromWallet: false);
        Assert.NotNull(res.Error);
        Assert.Null(res.Order);
    }

    [Fact]
    public void Approving_a_card_raises_the_owner_to_level_one()
    {
        var store = TestStore.Create();
        Assert.Equal(0, store.GetUser(6)!.VerificationLevel);
        var card = store.AddCard(6, "6037991234567893", "نگار شریفی", "/uploads/c.png").Card!;
        store.SetCardStatus(card.Id, BankCardStatus.Approved, null);
        Assert.Equal(1, store.GetUser(6)!.VerificationLevel);
    }

    [Fact]
    public void A_level_two_product_is_blocked_for_level_one_and_allowed_for_level_two()
    {
        var store = TestStore.Create();
        var card = store.AddCard(6, "6037991234567893", "نگار شریفی", "/uploads/c.png").Card!;
        store.SetCardStatus(card.Id, BankCardStatus.Approved, null);   // user 6 → level 1

        var blocked = store.PlaceOrder(store.GetUser(6)!, new[] { (7, 1, (int?)null) }, "کارت بانکی", fromWallet: false);
        Assert.NotNull(blocked.Error);

        var ok = store.PlaceOrder(store.GetUser(1)!, new[] { (7, 1, (int?)null) }, "کارت بانکی", fromWallet: false); // ali = level 2
        Assert.Null(ok.Error);
    }

    // Admin revoke: lowering a user's level drops Verified and rejects the backing card/KYC so the user
    // must re-verify — and HealVerificationLevels (which only ever raises) must not undo the downgrade.
    [Fact]
    public void Admin_revoking_a_level_two_user_resets_the_level_and_rejects_the_evidence()
    {
        var store = TestStore.Create();
        var card = store.AddCard(6, "6037991234567893", "نگار شریفی", "/uploads/c.png").Card!;
        store.SetCardStatus(card.Id, BankCardStatus.Approved, null);                       // → level 1
        var kyc = store.SubmitKyc(new KycRequest { UserId = 6, FullName = "نگار", NationalId = "001" });
        store.SetKycStatus(kyc.Id, KycStatus.Approved, null);                              // → level 2
        Assert.Equal(2, store.GetUser(6)!.VerificationLevel);

        store.SetVerificationLevel(6, 0);

        var u = store.GetUser(6)!;
        Assert.Equal(0, u.VerificationLevel);
        Assert.False(u.Verified);
        Assert.Equal(BankCardStatus.Rejected, store.GetCard(card.Id)!.Status);
        Assert.Equal(KycStatus.Rejected, store.GetKycForUser(6)!.Status);

        store.HealVerificationLevels();
        Assert.Equal(0, store.GetUser(6)!.VerificationLevel); // stays down — no re-raise
    }

    [Fact]
    public void Admin_lowering_to_level_one_keeps_the_card_but_revokes_the_kyc()
    {
        var store = TestStore.Create();
        var card = store.AddCard(6, "6037991234567893", "نگار شریفی", "/uploads/c.png").Card!;
        store.SetCardStatus(card.Id, BankCardStatus.Approved, null);
        var kyc = store.SubmitKyc(new KycRequest { UserId = 6, FullName = "نگار", NationalId = "001" });
        store.SetKycStatus(kyc.Id, KycStatus.Approved, null);

        store.SetVerificationLevel(6, 1);

        var u = store.GetUser(6)!;
        Assert.Equal(1, u.VerificationLevel);
        Assert.False(u.Verified);
        Assert.Equal(BankCardStatus.Approved, store.GetCard(card.Id)!.Status); // card retained at level 1
        Assert.Equal(KycStatus.Rejected, store.GetKycForUser(6)!.Status);      // level-2 evidence revoked
    }

    [Fact]
    public void Invoice_number_is_issued_only_once_the_order_is_completed()
    {
        var store = TestStore.Create();
        var order = store.PlaceOrder(store.GetUser(1)!, new[] { (1, 1, (int?)null) }, "کارت به کارت", fromWallet: false).Order!;

        // Not delivered yet → no invoice exists for it.
        Assert.Null(order.InvoiceNumber);
        store.SetOrderStatus(order.Id, OrderStatus.Preparing);
        Assert.Null(store.GetOrder(order.Id)!.InvoiceNumber);

        store.SetOrderStatus(order.Id, OrderStatus.Completed);

        var issued = store.GetOrder(order.Id)!.InvoiceNumber;
        Assert.NotNull(issued);
        Assert.Equal(16, issued!.Length);
        Assert.All(issued, c => Assert.True(char.IsAsciiDigit(c)));
    }

    [Fact]
    public void Invoice_number_is_stable_and_unique_across_orders()
    {
        var store = TestStore.Create();
        var first = store.PlaceOrder(store.GetUser(1)!, new[] { (1, 1, (int?)null) }, "کارت", fromWallet: false).Order!;
        var second = store.PlaceOrder(store.GetUser(5)!, new[] { (1, 1, (int?)null) }, "کارت", fromWallet: false).Order!;

        store.SetOrderStatus(first.Id, OrderStatus.Completed);
        var firstNumber = store.GetOrder(first.Id)!.InvoiceNumber;

        // Completing again must never mint a second number for the same invoice.
        store.SetOrderStatus(first.Id, OrderStatus.Preparing);
        store.SetOrderStatus(first.Id, OrderStatus.Completed);
        Assert.Equal(firstNumber, store.GetOrder(first.Id)!.InvoiceNumber);

        store.SetOrderStatus(second.Id, OrderStatus.Completed);
        Assert.NotEqual(firstNumber, store.GetOrder(second.Id)!.InvoiceNumber);
    }

    // A customer checking out a plan-priced product without picking a plan would be charged the bare base
    // price AND skip the plan's "collect info from the customer" step, since both live on the plan.
    [Fact]
    public void A_customer_cannot_check_out_a_plan_priced_product_without_choosing_a_plan()
    {
        var store = TestStore.Create();
        Assert.Contains(store.GetProduct(1)!.Plans, p => p.IsActive);
        var startStock = store.GetProduct(1)!.Stock;

        var res = store.PlaceOrder(store.GetUser(1)!, new[] { (1, 1, (int?)null) }, "کارت", fromWallet: false,
            paymentMethodId: 3, payment: new RemainderPayment(ApprovedCard(store, 1), "/uploads/r.png", "TRK-1", "1403/03/22", null),
            customerCheckout: true);

        Assert.NotNull(res.Error);
        Assert.Null(res.Order);
        Assert.Equal(startStock, store.GetProduct(1)!.Stock); // rejected before any stock moved
    }

    // Staff/internal placement is deliberately unaffected — it may still order at the product's base price.
    [Fact]
    public void Internal_placement_may_still_order_at_the_base_price_without_a_plan()
    {
        var store = TestStore.Create();
        var res = store.PlaceOrder(store.GetUser(1)!, new[] { (1, 1, (int?)null) }, "کارت", fromWallet: false);

        Assert.Null(res.Error);
        Assert.NotNull(res.Order);
        Assert.Equal(store.GetProduct(1)!.FinalPrice, res.Order!.Items[0].UnitPrice);
    }

    // Rejecting a receipt means the money never arrived — so it must never be credited to the wallet. Only the
    // wallet portion the buyer really paid comes back, and there is no penalty: they didn't cancel, we rejected.
    [Fact]
    public void Rejecting_a_receipt_never_credits_the_receipt_amount_to_the_wallet()
    {
        var store = TestStore.Create();
        var user = store.GetUser(1)!;              // ali — wallet is smaller than the order, so a remainder is due
        var startWallet = user.Wallet;
        var startStock = store.GetProduct(1)!.Stock;
        var planId = store.GetProduct(1)!.Plans.First(p => p.IsActive).Id;

        var placed = store.PlaceOrder(user, new[] { (1, 2, (int?)planId) }, "کیف پول + درگاه", fromWallet: true,
            paymentMethodId: 3, payment: new RemainderPayment(ApprovedCard(store, 1), "/uploads/r.png", "TRK-1", "1403/03/22", null),
            customerCheckout: true);
        var order = placed.Order!;
        Assert.Equal(OrderStatus.PendingApproval, order.Status);
        Assert.Equal(startWallet, order.WalletPaid);        // the whole wallet went in
        Assert.Equal(0, store.GetUser(1)!.Wallet);

        var tx = store.GetTransactions().First(t => t.OrderCode == order.Code && t.Type == TxTypes.OrderPayment);
        store.SetTransactionStatus(tx.Id, TxStatus.Rejected, "site", "رسید جعلی است");

        // The wallet gets back exactly what it put in — not a toman of the rejected receipt, and no penalty.
        Assert.Equal(startWallet, store.GetUser(1)!.Wallet);
        Assert.Equal(OrderStatus.Cancelled, store.GetOrder(order.Id)!.Status);
        Assert.Equal(startStock, store.GetProduct(1)!.Stock);   // stock returned
    }

    // Each account is approved/rejected on its own, so rejecting one refunds only what was paid for THAT
    // account: its price minus its share of the order's discount (no VAT, no gateway fee).
    [Fact]
    public void Rejecting_one_account_refunds_everything_charged_for_it()
    {
        var store = TestStore.Create();
        var user = store.GetUser(5)!;              // reza — enough wallet to pay in full
        var planId = store.GetProduct(1)!.Plans.First(p => p.IsActive).Id;

        var placed = store.PlaceOrder(user, new[] { (1, 2, (int?)planId) }, "wallet", fromWallet: true, discountCode: "WELCOME10");
        var order = placed.Order!;
        Assert.Equal(OrderStatus.Preparing, order.Status);
        Assert.True(order.DiscountAmount > 0);

        var unitPrice = order.Items[0].UnitPrice;
        // Everything the buyer was charged for this one account: its discounted price PLUS its proportional
        // share of the VAT and gateway fee, which were levied across the whole order.
        long Share(long total) => total <= 0
            ? 0
            : (long)Math.Round(total * (double)unitPrice / order.Subtotal, MidpointRounding.AwayFromZero);
        var expectedRefund = unitPrice - Share(order.DiscountAmount) + Share(order.VatAmount) + Share(order.FeeAmount);
        var walletBefore = store.GetUser(5)!.Wallet;
        var stockBefore = store.GetProduct(1)!.Stock;

        var (after, refunded, error) = store.RejectUnit(order.Id, order.Units[0].Id, "موجود نبود", "telegram");

        Assert.Null(error);
        Assert.Equal(expectedRefund, refunded);                          // price after discount, plus tax + fee
        Assert.Equal(walletBefore + expectedRefund, store.GetUser(5)!.Wallet);
        Assert.Equal(stockBefore + 1, store.GetProduct(1)!.Stock);       // only that one account returned
        Assert.True(after!.Units[0].Rejected);
        Assert.Equal(expectedRefund, after.Units[0].RefundedAmount);
        Assert.Equal(OrderStatus.Preparing, after.Status);               // the other account is still pending

        // A replayed reject must never pay twice.
        var (_, _, second) = store.RejectUnit(order.Id, order.Units[0].Id, "دوباره", "telegram");
        Assert.NotNull(second);
        Assert.Equal(walletBefore + expectedRefund, store.GetUser(5)!.Wallet);
    }

    // One rejected account must not hold the order open: delivering the rest still completes it (and issues
    // the invoice), while rejecting every account cancels it.
    [Fact]
    public void An_order_settles_once_every_account_is_delivered_or_rejected()
    {
        var store = TestStore.Create();
        var planId = store.GetProduct(1)!.Plans.First(p => p.IsActive).Id;
        var order = store.PlaceOrder(store.GetUser(5)!, new[] { (1, 2, (int?)planId) }, "wallet", fromWallet: true).Order!;

        store.RejectUnit(order.Id, order.Units[0].Id, "موجود نبود", "telegram");
        var (afterDeliver, justCompleted) = store.DeliverUnit(order.Id, order.Units[1].Id, "اکانت دوم");

        Assert.True(justCompleted);
        Assert.Equal(OrderStatus.Completed, afterDeliver!.Status);
        Assert.NotNull(afterDeliver.InvoiceNumber);
        Assert.DoesNotContain("اکانت 1", afterDeliver.DeliveryContent ?? ""); // the rejected one isn't shown

        // Every account rejected → the order is cancelled, not completed.
        var other = store.PlaceOrder(store.GetUser(5)!, new[] { (1, 1, (int?)planId) }, "wallet", fromWallet: true).Order!;
        store.RejectUnit(other.Id, other.Units[0].Id, "موجود نبود", "telegram");
        Assert.Equal(OrderStatus.Cancelled, store.GetOrder(other.Id)!.Status);
    }

    [Fact]
    public void Order_bot_announcement_is_claimed_exactly_once()
    {
        var store = TestStore.Create();
        var order = store.PlaceOrder(store.GetUser(1)!, new[] { (1, 3, (int?)null) }, "کارت", fromWallet: false).Order!;

        // Several paths can approve one order (panel, receipt bot, order approve) and each account is its own
        // message — so exactly one caller may ever win the right to post them.
        Assert.True(store.TryClaimOrderBotNotification(order.Id));
        Assert.False(store.TryClaimOrderBotNotification(order.Id));
        Assert.False(store.TryClaimOrderBotNotification(order.Id));
    }

    private static long Vat(StoreData store, long goods) =>
        (long)Math.Round(goods * (double)store.GetSettings().VatPercent / 100.0, MidpointRounding.AwayFromZero);

    private static int ApprovedCard(StoreData store, int userId)
    {
        var card = store.AddCard(userId, "6037991234567893", "علی محمدی", "/uploads/card.png").Card!;
        store.SetCardStatus(card.Id, BankCardStatus.Approved, null);
        return card.Id;
    }
}

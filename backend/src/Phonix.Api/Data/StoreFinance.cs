using Phonix.Api.Models;

namespace Phonix.Api.Data;

public record WithdrawalResult(Transaction? Tx, string? Error);

public partial class StoreData
{
    private readonly List<PaymentMethod> _paymentMethods = new();
    private PaymentSettings _paymentSettings = new();
    private readonly List<Transaction> _transactions = new();

    private int _paymentSeq;
    private int _txSeq;

    // payment methods

    public IReadOnlyList<PaymentMethod> GetPaymentMethods() => GetItems(_paymentMethods);
    public PaymentMethod? GetPaymentMethod(int id) => GetItem(_paymentMethods, id);

    public PaymentMethod AddPaymentMethod(PaymentMethod m)
    {
        lock (_gate) { m.Id = ++_paymentSeq; _paymentMethods.Add(m); }
        PersistNow(); // payment destinations are settings — persist instantly so a restart keeps them.
        return m;
    }

    public bool UpdatePaymentMethod(PaymentMethod m)
    {
        bool ok;
        lock (_gate)
        {
            var e = _paymentMethods.FirstOrDefault(x => x.Id == m.Id);
            if (e is null) { ok = false; }
            else
            {
                e.Type = m.Type;
                e.Title = m.Title;
                e.Holder = m.Holder;
                e.Value = m.Value;
                e.Network = m.Network;
                e.Sheba = m.Sheba;
                e.AccountNumber = m.AccountNumber;
                e.Instructions = m.Instructions;
                e.IsActive = m.IsActive;
                e.SortOrder = m.SortOrder;
                ok = true;
            }
        }
        if (ok) PersistNow();
        return ok;
    }

    public bool DeletePaymentMethod(int id)
    {
        var ok = DeleteItem(_paymentMethods, id);
        if (ok) PersistNow();
        return ok;
    }

    // payment settings

    public PaymentSettings GetPaymentSettings()
    {
        lock (_gate) return _paymentSettings;
    }

    public void UpdatePaymentSettings(PaymentSettings s)
    {
        lock (_gate) _paymentSettings = s;
        PersistNow();
    }

    // transactions

    public IReadOnlyList<Transaction> GetTransactions(TxStatus? status = null)
    {
        lock (_gate)
        {
            IEnumerable<Transaction> q = _transactions;
            if (status is TxStatus s) q = q.Where(t => t.Status == s);
            return q.OrderByDescending(t => t.Id).ToList();
        }
    }

    public Transaction? GetTransaction(int id)
    {
        lock (_gate) return _transactions.FirstOrDefault(t => t.Id == id);
    }

    public IReadOnlyList<Transaction> GetUserTransactions(int userId)
    {
        lock (_gate) return _transactions.Where(t => t.UserId == userId).OrderByDescending(t => t.Id).ToList();
    }

    public Transaction AddTransaction(Transaction t)
    {
        lock (_gate)
        {
            t.Id = ++_txSeq;
            if (string.IsNullOrWhiteSpace(t.Code)) t.Code = $"TX-{9900 + t.Id}";
            if (string.IsNullOrWhiteSpace(t.Date)) t.Date = Today();
            _transactions.Add(t);
            return t;
        }
    }

    // Approving/rejecting a transaction moves money (credits a top-up, refunds a withdrawal, advances an
    // order's payment). Persist synchronously once the change is committed so it can survive a crash.
    public bool SetTransactionStatus(int id, TxStatus status, string via, string? note)
    {
        var changed = SetTransactionStatusCore(id, status, via, note);
        if (changed) PersistNow();
        return changed;
    }

    private bool SetTransactionStatusCore(int id, TxStatus status, string via, string? note)
    {
        lock (_gate)
        {
            var e = _transactions.FirstOrDefault(t => t.Id == id);
            if (e is null) return false;
            var becomingApproved = e.Status != TxStatus.Approved && status == TxStatus.Approved;
            var becomingRejected = e.Status != TxStatus.Rejected && status == TxStatus.Rejected;

            // A wallet top-up credits the owner's balance exactly while it is approved. Applying the
            // delta only on the approved↔not-approved transition means the first approval credits once,
            // reversing an approval (Approved→Rejected) debits it back, and re-approving never double-credits.
            if (e.Type == TxTypes.WalletTopUp && e.Amount > 0 && e.UserId > 0)
            {
                var wasApproved = e.Status == TxStatus.Approved;
                var willBeApproved = status == TxStatus.Approved;
                if (wasApproved != willBeApproved)
                {
                    var owner = UserById(e.UserId);
                    if (owner is not null)
                        owner.Wallet = Math.Max(0, owner.Wallet + (willBeApproved ? e.Amount : -e.Amount));
                }
            }

            // A withdrawal holds (debits) the balance the moment it is requested. Rejecting it returns the
            // held funds; the refund flips only on the reject↔not-reject transition, so a re-reject never
            // double-refunds and re-opening a rejected request re-holds the amount. Approving changes
            // nothing here because the money already left the balance at request time.
            if (e.Type == TxTypes.Withdraw && e.Amount < 0 && e.UserId > 0)
            {
                var wasRefunded = e.Status == TxStatus.Rejected;
                var willBeRefunded = status == TxStatus.Rejected;
                if (wasRefunded != willBeRefunded)
                {
                    var owner = UserById(e.UserId);
                    if (owner is not null)
                        owner.Wallet = Math.Max(0, owner.Wallet + (willBeRefunded ? -e.Amount : e.Amount));
                }
            }

            // Approving an order's card-to-card payment advances that order to preparing (it never touches
            // a wallet balance). Applied on the not-approved → approved transition.
            if (e.Type == TxTypes.OrderPayment && !string.IsNullOrWhiteSpace(e.OrderCode) && e.Status != TxStatus.Approved && status == TxStatus.Approved)
            {
                var ord = _orders.FirstOrDefault(o => o.Code == e.OrderCode);
                if (ord is not null && ord.Status == OrderStatus.PendingApproval)
                {
                    ord.Status = OrderStatus.Preparing;
                    AppendOrderHistory(ord, OrderStatus.PendingApproval, OrderStatus.Preparing, "سیستم (تأیید پرداخت)", "تأیید پرداخت سفارش");
                    RefreshUserOrderStats(ord.UserId);
                }
            }

            // Rejecting an order's payment cancels the still-pending order so the site status matches the
            // receipt. Routed through the real cancel path, which restores the stock and refunds ONLY what was
            // actually collected — for a pending order that is the wallet portion. The rejected receipt's money
            // never arrived, so it is never credited. No penalty: the customer didn't cancel, we rejected.
            if (e.Type == TxTypes.OrderPayment && !string.IsNullOrWhiteSpace(e.OrderCode) && e.Status != TxStatus.Rejected && status == TxStatus.Rejected)
            {
                var ord = _orders.FirstOrDefault(o => o.Code == e.OrderCode);
                if (ord is not null && ord.Status == OrderStatus.PendingApproval)
                    CancelOrderCore(ord.Id, "سیستم (رد پرداخت)", note is { Length: > 0 } ? note : "رد پرداخت سفارش", applyPenalty: false);
            }

            e.Status = status;
            e.ApprovedVia = via;
            if (note is not null) e.Note = note;

            // notify the owner when their payment is approved.
            if (becomingApproved && e.UserId > 0)
            {
                if (e.Type == TxTypes.WalletTopUp)
                    AddNotification(e.UserId, "شارژ کیف پول", $"کیف پول شما به مبلغ {e.Amount:N0} تومان شارژ شد.", "/account/wallet");
                else if (e.Type == TxTypes.OrderPayment)
                    AddNotification(e.UserId, "پرداخت تأیید شد", "پرداخت سفارش شما تأیید و سفارش در حال آماده‌سازی است.", "/account/orders");
            }

            if (becomingRejected && e.UserId > 0 && e.Type == TxTypes.OrderPayment)
                AddNotification(e.UserId, "پرداخت رد شد",
                    note is { Length: > 0 } ? $"پرداخت سفارش شما رد شد: {note}" : "پرداخت سفارش شما رد شد.", "/account/orders");
            return true;
        }
    }

    // Files a withdrawal request and holds the funds immediately: the balance is debited now so the same
    // money can't also be spent while staff review it. Approval just confirms the payout; rejection
    // refunds the held amount (see SetTransactionStatus).
    public WithdrawalResult RequestWithdrawal(int userId, long amount, string destination)
    {
        var result = RequestWithdrawalCore(userId, amount, destination);
        if (result.Error is null) PersistNow(); // the balance was just debited (held) — make it durable now.
        return result;
    }

    private WithdrawalResult RequestWithdrawalCore(int userId, long amount, string destination)
    {
        lock (_gate)
        {
            var user = UserById(userId);
            if (user is null) return new WithdrawalResult(null, "کاربر یافت نشد.");
            if (amount <= 0) return new WithdrawalResult(null, "مبلغ نامعتبر است.");
            if (user.Wallet < amount) return new WithdrawalResult(null, "موجودی کیف پول برای این برداشت کافی نیست.");

            user.Wallet -= amount;

            var name = string.IsNullOrWhiteSpace(user.Name) ? user.Username : user.Name;
            var tx = AddTransaction(new Transaction
            {
                UserId = userId,
                UserName = name,
                Type = TxTypes.Withdraw,
                Amount = -amount,
                Status = TxStatus.Pending,
                Method = destination,
                Date = Today(),
            });
            return new WithdrawalResult(tx, null);
        }
    }

    private void SeedFinance()
    {
        AddPaymentMethod(new PaymentMethod { Type = PaymentType.Card, Title = "کارت بانکی", Holder = "علی محمدی", Value = "۶۰۳۷-۹۹۷۱-۲۳۴۵-۶۷۸۹", Network = "بانک ملی", Instructions = "مبلغ را به این کارت واریز و رسید را ارسال کنید.", SortOrder = 1 });
        AddPaymentMethod(new PaymentMethod { Type = PaymentType.Crypto, Title = "تتر (USDT)", Holder = "کیف پول فونیکس", Value = "TXk9...aZ2bQ", Network = "TRC20", Instructions = "فقط در شبکه TRC20 واریز کنید.", SortOrder = 2 });
        AddPaymentMethod(new PaymentMethod { Type = PaymentType.Gateway, Title = "درگاه زرین‌پال", Holder = "Phoenix Verify", Value = "zp-merchant-0000", Network = "ZarinPal", Instructions = "پرداخت آنلاین با کارت‌های شتاب.", FeePercent = 3, SortOrder = 3 });

        _paymentSettings = new PaymentSettings
        {
            TelegramEnabled = false,
            TelegramBotToken = "",
            TelegramChatId = "",
            RequireReceipt = true,
            AutoApproveUnder = 0,
        };

        AddTransaction(new Transaction { Code = "TX-9912", UserId = 1, UserName = "علی محمدی", Type = TxTypes.WalletTopUp, Amount = 500_000, Status = TxStatus.Pending, Method = "کارت بانکی", Date = "۱۴۰۳/۰۳/۲۲" });
        AddTransaction(new Transaction { Code = "TX-9911", UserId = 2, UserName = "زهرا کریمی", Type = TxTypes.Purchase, Amount = -185_000, Status = TxStatus.Approved, Method = "کیف پول", ApprovedVia = "site", Date = "۱۴۰۳/۰۳/۲۲" });
        AddTransaction(new Transaction { Code = "TX-9910", UserId = 5, UserName = "رضا نوری", Type = TxTypes.Referral, Amount = 85_000, Status = TxStatus.Approved, Method = "سیستمی", ApprovedVia = "site", Date = "۱۴۰۳/۰۳/۲۱" });
        AddTransaction(new Transaction { Code = "TX-9909", UserId = 3, UserName = "محمد رضایی", Type = TxTypes.Withdraw, Amount = -300_000, Status = TxStatus.Pending, Method = "کارت بانکی", Date = "۱۴۰۳/۰۳/۲۰" });
        AddTransaction(new Transaction { Code = "TX-9908", UserId = 4, UserName = "سارا احمدی", Type = TxTypes.WalletTopUp, Amount = 250_000, Status = TxStatus.Rejected, Method = "تتر (USDT)", ApprovedVia = "site", Note = "رسید نامعتبر", Date = "۱۴۰۳/۰۳/۱۹" });
    }
}

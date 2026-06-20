using Phonix.Api.Models;

namespace Phonix.Api.Data;

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
        lock (_gate) { m.Id = ++_paymentSeq; _paymentMethods.Add(m); return m; }
    }

    public bool UpdatePaymentMethod(PaymentMethod m)
    {
        lock (_gate)
        {
            var e = _paymentMethods.FirstOrDefault(x => x.Id == m.Id);
            if (e is null) return false;
            e.Type = m.Type;
            e.Title = m.Title;
            e.Holder = m.Holder;
            e.Value = m.Value;
            e.Network = m.Network;
            e.Instructions = m.Instructions;
            e.IsActive = m.IsActive;
            e.SortOrder = m.SortOrder;
            return true;
        }
    }

    public bool DeletePaymentMethod(int id) => DeleteItem(_paymentMethods, id);

    // payment settings

    public PaymentSettings GetPaymentSettings()
    {
        lock (_gate) return _paymentSettings;
    }

    public void UpdatePaymentSettings(PaymentSettings s)
    {
        lock (_gate) _paymentSettings = s;
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

    public bool SetTransactionStatus(int id, TxStatus status, string via, string? note)
    {
        lock (_gate)
        {
            var e = _transactions.FirstOrDefault(t => t.Id == id);
            if (e is null) return false;
            e.Status = status;
            e.ApprovedVia = via;
            if (note is not null) e.Note = note;
            return true;
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

        AddTransaction(new Transaction { Code = "TX-9912", UserName = "علی محمدی", Type = "شارژ کیف پول", Amount = 500_000, Status = TxStatus.Pending, Method = "کارت بانکی", Date = "۱۴۰۳/۰۳/۲۲" });
        AddTransaction(new Transaction { Code = "TX-9911", UserName = "زهرا کریمی", Type = "خرید", Amount = -185_000, Status = TxStatus.Approved, Method = "کیف پول", ApprovedVia = "site", Date = "۱۴۰۳/۰۳/۲۲" });
        AddTransaction(new Transaction { Code = "TX-9910", UserName = "رضا نوری", Type = "پورسانت", Amount = 85_000, Status = TxStatus.Approved, Method = "سیستمی", ApprovedVia = "site", Date = "۱۴۰۳/۰۳/۲۱" });
        AddTransaction(new Transaction { Code = "TX-9909", UserName = "محمد رضایی", Type = "برداشت", Amount = -300_000, Status = TxStatus.Pending, Method = "کارت بانکی", Date = "۱۴۰۳/۰۳/۲۰" });
        AddTransaction(new Transaction { Code = "TX-9908", UserName = "سارا احمدی", Type = "شارژ کیف پول", Amount = 250_000, Status = TxStatus.Rejected, Method = "تتر (USDT)", ApprovedVia = "site", Note = "رسید نامعتبر", Date = "۱۴۰۳/۰۳/۱۹" });
    }
}

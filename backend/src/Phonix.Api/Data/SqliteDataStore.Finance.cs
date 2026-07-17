using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Data;

// Money: wallet credit/debit path, payment methods, transactions and their approval.
// Partial of SqliteDataStore -- split by domain the same way the JSON StoreData is (StoreOrders.cs etc.).
public sealed partial class SqliteDataStore
{
    // ── Money path (the ACID demonstration that replaces _gate) ────────────────────────────────────────

    // Files a withdrawal and HOLDS the funds immediately: balance check + debit + the pending transaction all
    // commit together, or none do. Two simultaneous withdrawals can't both pass the balance check and
    // overdraw, because IMMEDIATE serializes the writers — the second blocks at BEGIN until the first commits,
    // then re-reads the already-reduced balance. Same integrity the old `_gate` gave, without a global lock.
    public WithdrawalResult RequestWithdrawal(int userId, long amount, string destination) =>
        WriteTx<WithdrawalResult>((conn, tx) =>
        {
            var json = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Users WHERE Id = @userId", new { userId }, tx);
            if (json is null) return new WithdrawalResult(null, "کاربر یافت نشد.");
            var user = Deserialize<AppUser>(json)!;

            if (amount <= 0) return new WithdrawalResult(null, "مبلغ نامعتبر است.");
            if (user.Wallet < amount) return new WithdrawalResult(null, "موجودی کیف پول برای این برداشت کافی نیست.");

            user.Wallet -= amount;            // debit
            UpsertUser(conn, tx, user);       // persist the held balance

            var name = string.IsNullOrWhiteSpace(user.Name) ? user.Username : user.Name;
            var t = new Transaction
            {
                UserId = userId,
                UserName = name,
                Type = TxTypes.Withdraw,
                Amount = -amount,
                Status = TxStatus.Pending,
                Method = destination,
                Date = Today(),
            };
            var txId = conn.ExecuteScalar<long>(@"
INSERT INTO Transactions (UserId, Status, Date, DataJson) VALUES (@UserId, @Status, @Date, @DataJson);
SELECT last_insert_rowid();",
                new { t.UserId, Status = (int)t.Status, t.Date, DataJson = Serialize(t) }, tx);

            t.Id = (int)txId;
            conn.Execute("UPDATE Transactions SET DataJson = @DataJson WHERE Id = @Id",
                new { DataJson = Serialize(t), t.Id }, tx);

            return new WithdrawalResult(t, null); // WriteTx COMMITs here → debit + pending tx persist atomically
        });

    // Mirrors StoreData.AddTransaction: assigns id (autoincrement), a TX-code, and the date, then rewrites
    // the payload so DataJson carries them. Caller supplies the open connection + transaction.
    private static Transaction InsertTransaction(SqliteConnection conn, SqliteTransaction tx, Transaction t)
    {
        if (string.IsNullOrWhiteSpace(t.Date)) t.Date = Today();
        var id = conn.ExecuteScalar<long>(@"
INSERT INTO Transactions (UserId, Status, Date, DataJson) VALUES (@UserId, @Status, @Date, @DataJson);
SELECT last_insert_rowid();",
            new { t.UserId, Status = (int)t.Status, t.Date, DataJson = Serialize(t) }, tx);
        t.Id = (int)id;
        if (string.IsNullOrWhiteSpace(t.Code)) t.Code = $"TX-{9900 + t.Id}";
        conn.Execute("UPDATE Transactions SET DataJson = @DataJson WHERE Id = @Id",
            new { DataJson = Serialize(t), t.Id }, tx);
        return t;
    }


    // ── Finance: payment methods ────────────────────────────────────────────────────────────────────────
    public IReadOnlyList<PaymentMethod> GetPaymentMethods() => Ordered(AllJson<PaymentMethod>("PaymentMethods"));
    public PaymentMethod? GetPaymentMethod(int id) => OneJson<PaymentMethod>("PaymentMethods", id);
    public PaymentMethod AddPaymentMethod(PaymentMethod m) { InsertJson("PaymentMethods", m, (x, id) => x.Id = id); return m; }
    public bool UpdatePaymentMethod(PaymentMethod m) { if (OneJson<PaymentMethod>("PaymentMethods", m.Id) is null) return false; return UpdateJson("PaymentMethods", m.Id, m); }
    public bool DeletePaymentMethod(int id) => DeleteRow("PaymentMethods", id);

    // ── Finance: transactions ───────────────────────────────────────────────────────────────────────────
    public IReadOnlyList<Transaction> GetTransactions(TxStatus? status = null)
    {
        using var conn = OpenConnection();
        var sql = "SELECT DataJson FROM Transactions";
        if (status is not null) sql += " WHERE Status = @status";
        sql += " ORDER BY Id DESC;";
        return conn.Query<string>(sql, new { status = status is null ? 0 : (int)status.Value }).Select(j => Deserialize<Transaction>(j)!).ToList();
    }
    public Transaction? GetTransaction(int id)
    {
        using var conn = OpenConnection();
        var j = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Transactions WHERE Id=@id", new { id });
        return j is null ? null : Deserialize<Transaction>(j);
    }
    public IReadOnlyList<Transaction> GetUserTransactions(int userId)
    {
        using var conn = OpenConnection();
        return conn.Query<string>("SELECT DataJson FROM Transactions WHERE UserId=@userId ORDER BY Id DESC", new { userId })
            .Select(j => Deserialize<Transaction>(j)!).ToList();
    }
    public Transaction AddTransaction(Transaction t) => WriteTx((conn, tx) => InsertTransaction(conn, tx, t));

    public bool SetTransactionStatus(int id, TxStatus status, string via, string? note) =>
        WriteTx((conn, tx) =>
        {
            var ej = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Transactions WHERE Id=@id", new { id }, tx);
            if (ej is null) return false;
            var e = Deserialize<Transaction>(ej)!;
            var becomingApproved = e.Status != TxStatus.Approved && status == TxStatus.Approved;
            var becomingRejected = e.Status != TxStatus.Rejected && status == TxStatus.Rejected;

            if (e.Type == TxTypes.WalletTopUp && e.Amount > 0 && e.UserId > 0)
            {
                var wasApproved = e.Status == TxStatus.Approved;
                var willBeApproved = status == TxStatus.Approved;
                if (wasApproved != willBeApproved)
                {
                    var owner = LoadUser(conn, tx, e.UserId);
                    if (owner is not null) { owner.Wallet = Math.Max(0, owner.Wallet + (willBeApproved ? e.Amount : -e.Amount)); UpsertUser(conn, tx, owner); }
                }
            }

            if (e.Type == TxTypes.Withdraw && e.Amount < 0 && e.UserId > 0)
            {
                var wasRefunded = e.Status == TxStatus.Rejected;
                var willBeRefunded = status == TxStatus.Rejected;
                if (wasRefunded != willBeRefunded)
                {
                    var owner = LoadUser(conn, tx, e.UserId);
                    if (owner is not null) { owner.Wallet = Math.Max(0, owner.Wallet + (willBeRefunded ? -e.Amount : e.Amount)); UpsertUser(conn, tx, owner); }
                }
            }

            if (e.Type == TxTypes.OrderPayment && !string.IsNullOrWhiteSpace(e.OrderCode) && e.Status != TxStatus.Approved && status == TxStatus.Approved)
            {
                var oj = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Orders WHERE Code=@code", new { code = e.OrderCode }, tx);
                if (oj is not null)
                {
                    var ord = Deserialize<Order>(oj)!;
                    if (ord.Status == OrderStatus.PendingApproval)
                    {
                        ord.Status = OrderStatus.Preparing;
                        AppendOrderHistory(ord, OrderStatus.PendingApproval, OrderStatus.Preparing, "سیستم (تأیید پرداخت)", "تأیید پرداخت سفارش");
                        UpsertOrder(conn, tx, ord);
                        RefreshUserStats(conn, tx, ord.UserId);
                    }
                }
            }

            // A rejected order payment cancels the still-pending order, so the site status mirrors the receipt
            // decision. Routed through the same cancel logic the panel uses, which restores the stock and
            // refunds ONLY what was actually collected — for a pending order that is the wallet portion. The
            // rejected receipt's money never arrived, so it is never credited. No penalty: the customer didn't
            // cancel, we rejected.
            if (e.Type == TxTypes.OrderPayment && !string.IsNullOrWhiteSpace(e.OrderCode) && e.Status != TxStatus.Rejected && status == TxStatus.Rejected)
            {
                var orderId = conn.QueryFirstOrDefault<int?>("SELECT Id FROM Orders WHERE Code=@code", new { code = e.OrderCode }, tx);
                if (orderId is int oid)
                    CancelOrderInTx(conn, tx, oid, "سیستم (رد پرداخت)",
                        note is { Length: > 0 } ? note : "رد پرداخت سفارش", applyPenalty: false);
            }

            e.Status = status; e.ApprovedVia = via; if (note is not null) e.Note = note;
            conn.Execute("UPDATE Transactions SET Status=@s, DataJson=@d WHERE Id=@id", new { s = (int)e.Status, d = Serialize(e), id }, tx);

            if (becomingApproved && e.UserId > 0)
            {
                if (e.Type == TxTypes.WalletTopUp)
                    AddNotificationTx(conn, tx, e.UserId, "شارژ کیف پول", $"کیف پول شما به مبلغ {e.Amount:N0} تومان شارژ شد.", "/account/wallet");
                else if (e.Type == TxTypes.OrderPayment)
                    AddNotificationTx(conn, tx, e.UserId, "پرداخت تأیید شد", "پرداخت سفارش شما تأیید و سفارش در حال آماده‌سازی است.", "/account/orders");
            }

            if (becomingRejected && e.UserId > 0 && e.Type == TxTypes.OrderPayment)
                AddNotificationTx(conn, tx, e.UserId, "پرداخت رد شد",
                    note is { Length: > 0 } ? $"پرداخت سفارش شما رد شد: {note}" : "پرداخت سفارش شما رد شد.", "/account/orders");
            return true;
        });
}

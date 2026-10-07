using Dapper;
using Phonix.Api.Models;

namespace Phonix.Api.Data;

// Which bot message put a request up for review. A decision taken on the site uses it to rewrite that message
// straight away — buttons gone, «از طریق سایت» — so nobody in the group acts on something already settled.
public sealed partial class SqliteDataStore
{
    public void SetTransactionTelegramMessage(int id, long chatId, int messageId) =>
        WriteTx((conn, tx) =>
        {
            var json = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Transactions WHERE Id=@id", new { id }, tx);
            if (json is null) return false;
            var t = Deserialize<Transaction>(json)!;
            t.TelegramChatId = chatId;
            t.TelegramMessageId = messageId;
            var updated = Serialize(t);
            conn.Execute("UPDATE Transactions SET DataJson=@d WHERE Id=@id", new { d = updated, id }, tx);
            AppendOutbox(conn, tx, "Transactions", id, SyncOp.Upsert, updated);
            return true;
        });

    public void SetCardTelegramMessage(int id, long chatId, int messageId) =>
        WriteTx((conn, tx) =>
        {
            var json = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Cards WHERE Id=@id", new { id }, tx);
            if (json is null) return false;
            var card = Deserialize<BankCard>(json)!;
            card.TelegramChatId = chatId;
            card.TelegramMessageId = messageId;
            var updated = Serialize(card);
            conn.Execute("UPDATE Cards SET DataJson=@d WHERE Id=@id", new { d = updated, id }, tx);
            AppendOutbox(conn, tx, "Cards", id, SyncOp.Upsert, updated);
            return true;
        });

    public void SetKycTelegramMessage(int id, long chatId, int messageId) =>
        WriteTx((conn, tx) =>
        {
            var json = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Kyc WHERE Id=@id", new { id }, tx);
            if (json is null) return false;
            var req = Deserialize<KycRequest>(json)!;
            req.TelegramChatId = chatId;
            req.TelegramMessageId = messageId;
            var updated = Serialize(req);
            conn.Execute("UPDATE Kyc SET DataJson=@d WHERE Id=@id", new { d = updated, id }, tx);
            AppendOutbox(conn, tx, "Kyc", id, SyncOp.Upsert, updated);
            return true;
        });

    public void SetUnitTelegramMessage(int orderId, int unitId, long chatId, int messageId) =>
        WriteTx((conn, tx) =>
        {
            var json = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Orders WHERE Id=@id", new { id = orderId }, tx);
            if (json is null) return false;
            var o = Deserialize<Order>(json)!;
            if (o.Units.FirstOrDefault(u => u.Id == unitId) is not { } unit) return false;
            unit.TelegramChatId = chatId;
            unit.TelegramMessageId = messageId;
            UpsertOrder(conn, tx, o);
            return true;
        });
}

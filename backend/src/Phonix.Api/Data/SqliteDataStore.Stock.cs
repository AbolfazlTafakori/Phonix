using Dapper;
using Microsoft.Data.Sqlite;
using Phonix.Api.Models;
using Phonix.Api.Services;

namespace Phonix.Api.Data;

// Stock pool: the virtual inventory of ready-to-deliver items. Same hybrid shape as the rest of the store
// (indexed ProductId/Status columns + the full object in DataJson). The pull path runs inside WriteTx so two
// concurrent orders can never reserve the same item — the IMMEDIATE transaction serializes them exactly like
// wallet debits and stock decrements.
public sealed partial class SqliteDataStore
{
    private void UpsertStockItem(SqliteConnection conn, SqliteTransaction? tx, StockItem s)
    {
        var json = Serialize(s);
        conn.Execute("UPDATE StockItems SET ProductId=@ProductId, Status=@Status, DataJson=@DataJson WHERE Id=@Id",
            new { s.Id, s.ProductId, Status = (int)s.Status, DataJson = json }, tx);
        if (tx is not null) AppendOutbox(conn, tx, "StockItems", s.Id, SyncOp.Upsert, json);
    }

    public IReadOnlyList<StockItem> GetStockItems(int? productId = null)
    {
        using var conn = OpenConnection();
        var sql = "SELECT DataJson FROM StockItems" + (productId is null ? "" : " WHERE ProductId = @productId") + " ORDER BY Id";
        return conn.Query<string>(sql, new { productId }).Select(j => Deserialize<StockItem>(j)!).ToList();
    }

    public StockItem? GetStockItem(int id) => OneJson<StockItem>("StockItems", id);

    public List<StockItem> AddStockItems(int productId, IEnumerable<string> contents, string? addedBy) =>
        WriteTx((conn, tx) =>
        {
            var added = new List<StockItem>();
            foreach (var content in contents)
            {
                var item = new StockItem { ProductId = productId, Content = content, AddedBy = addedBy };
                var id = (int)conn.ExecuteScalar<long>(@"
INSERT INTO StockItems (ProductId, Status, DataJson) VALUES (@ProductId, @Status, @DataJson);
SELECT last_insert_rowid();",
                    new { item.ProductId, Status = (int)item.Status, DataJson = Serialize(item) }, tx);
                item.Id = id;
                UpsertStockItem(conn, tx, item);
                added.Add(item);
            }
            return added;
        });

    public bool SetStockItemStatus(int id, StockItemStatus status) =>
        WriteTx((conn, tx) =>
        {
            var json = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM StockItems WHERE Id = @id", new { id }, tx);
            if (json is null) return false;
            var item = Deserialize<StockItem>(json)!;
            var allowed = (item.Status, status) switch
            {
                (StockItemStatus.Available, StockItemStatus.Disabled) => true,
                (StockItemStatus.Disabled, StockItemStatus.Available) => true,
                (StockItemStatus.Reserved, StockItemStatus.Available) => true, // release an abandoned pull
                _ => false,
            };
            if (!allowed) return false;
            item.Status = status;
            if (status == StockItemStatus.Available) { item.OrderId = null; item.UnitId = null; }
            UpsertStockItem(conn, tx, item);
            return true;
        });

    public bool DeleteStockItem(int id) =>
        WriteTx((conn, tx) =>
        {
            var json = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM StockItems WHERE Id = @id", new { id }, tx);
            if (json is null) return false;
            if (Deserialize<StockItem>(json)!.Status == StockItemStatus.Delivered) return false;
            var deleted = conn.Execute("DELETE FROM StockItems WHERE Id = @id", new { id }, tx) > 0;
            if (deleted) AppendOutbox(conn, tx, "StockItems", id, SyncOp.Delete, null);
            return deleted;
        });

    public StockItem? PullStockItem(int productId, int orderId, int unitId) =>
        WriteTx((conn, tx) =>
        {
            var json = conn.QueryFirstOrDefault<string>(
                "SELECT DataJson FROM StockItems WHERE ProductId = @productId AND Status = @status ORDER BY Id LIMIT 1",
                new { productId, status = (int)StockItemStatus.Available }, tx);
            if (json is null) return null;
            var item = Deserialize<StockItem>(json)!;
            item.Status = StockItemStatus.Reserved;
            item.OrderId = orderId;
            item.UnitId = unitId;
            UpsertStockItem(conn, tx, item);
            return item;
        });

    // ── Multi-seat stock accounts (slots embedded in DataJson, like Order.Units) ───────────────────
    // Every mutation runs inside WriteTx (IMMEDIATE), so two concurrent orders can never reserve the same
    // consecutive run — the same guarantee the one-shot item pool gets.

    private void UpsertStockAccount(SqliteConnection conn, SqliteTransaction? tx, StockAccount a)
    {
        var json = Serialize(a);
        conn.Execute("UPDATE StockAccounts SET ProductId=@ProductId, DataJson=@DataJson WHERE Id=@Id",
            new { a.Id, a.ProductId, DataJson = json }, tx);
        if (tx is not null) AppendOutbox(conn, tx, "StockAccounts", a.Id, SyncOp.Upsert, json);
    }

    public IReadOnlyList<StockAccount> GetStockAccounts(int? productId = null)
    {
        using var conn = OpenConnection();
        var sql = "SELECT DataJson FROM StockAccounts" + (productId is null ? "" : " WHERE ProductId = @productId") + " ORDER BY Id";
        return conn.Query<string>(sql, new { productId }).Select(j => Deserialize<StockAccount>(j)!).ToList();
    }

    public StockAccount? GetStockAccount(int id) => OneJson<StockAccount>("StockAccounts", id);

    public StockAccount AddStockAccount(StockAccount account) =>
        WriteTx((conn, tx) =>
        {
            account.Slots = StockAccount.GenerateSlots(account.Capacity);
            var id = (int)conn.ExecuteScalar<long>(@"
INSERT INTO StockAccounts (ProductId, DataJson) VALUES (@ProductId, @DataJson);
SELECT last_insert_rowid();",
                new { account.ProductId, DataJson = Serialize(account) }, tx);
            account.Id = id;
            UpsertStockAccount(conn, tx, account);
            return account;
        });

    // One-time normalization: re-encrypts any StockAccount password still stored as plaintext (accounts saved
    // before at-rest encryption was mandatory) under the current key. Idempotent — an already-encrypted
    // password (FieldCrypto or the old optional BackupCrypto scheme) is left untouched, so restarts are no-ops.
    public int MigratePlaintextStockPasswords() =>
        WriteTx((conn, tx) =>
        {
            var rows = conn.Query<(long Id, string DataJson)>("SELECT Id, DataJson FROM StockAccounts", transaction: tx).ToList();
            var migrated = 0;
            foreach (var row in rows)
            {
                var acc = Deserialize<StockAccount>(row.DataJson)!;
                if (string.IsNullOrEmpty(acc.Password)
                    || FieldCrypto.LooksEncrypted(acc.Password) || BackupCrypto.LooksEncrypted(acc.Password))
                    continue;
                acc.Password = SensitiveField.Protect(acc.Password);
                var json = Serialize(acc);
                conn.Execute("UPDATE StockAccounts SET DataJson = @d WHERE Id = @id", new { d = json, id = row.Id }, tx);
                AppendOutbox(conn, tx, "StockAccounts", row.Id, SyncOp.Upsert, json);
                migrated++;
            }
            return migrated;
        });

    public bool DeleteStockAccount(int id) =>
        WriteTx((conn, tx) =>
        {
            var json = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM StockAccounts WHERE Id = @id", new { id }, tx);
            if (json is null) return false;
            if (Deserialize<StockAccount>(json)!.Slots.Any(s => s.Status == StockItemStatus.Delivered)) return false;
            var deleted = conn.Execute("DELETE FROM StockAccounts WHERE Id = @id", new { id }, tx) > 0;
            if (deleted) AppendOutbox(conn, tx, "StockAccounts", id, SyncOp.Delete, null);
            return deleted;
        });

    public bool SetStockAccountDisabled(int id, bool disabled) =>
        WriteTx((conn, tx) =>
        {
            var json = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM StockAccounts WHERE Id = @id", new { id }, tx);
            if (json is null) return false;
            var acc = Deserialize<StockAccount>(json)!;
            acc.Disabled = disabled;
            UpsertStockAccount(conn, tx, acc);
            return true;
        });

    public bool SetStockSlotStatus(int accountId, int slotId, StockItemStatus status) =>
        WriteTx((conn, tx) =>
        {
            var json = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM StockAccounts WHERE Id = @accountId", new { accountId }, tx);
            if (json is null) return false;
            var acc = Deserialize<StockAccount>(json)!;
            var slot = acc.Slots.FirstOrDefault(s => s.Id == slotId);
            if (slot is null || !StoreData.SlotTransitionAllowed(slot.Status, status)) return false;
            slot.Status = status;
            if (status == StockItemStatus.Available) { slot.OrderId = null; slot.UnitId = null; }
            UpsertStockAccount(conn, tx, acc);
            return true;
        });

    public (StockAccount Account, List<StockSlot> Slots)? ReserveStockSlots(int productId, int count, string planType, int orderId, int unitId)
    {
        if (count <= 0) return null;
        return WriteTx<(StockAccount, List<StockSlot>)?>((conn, tx) =>
        {
            var rows = conn.Query<string>(
                "SELECT DataJson FROM StockAccounts WHERE ProductId = @productId ORDER BY Id", new { productId }, tx);
            foreach (var acc in rows.Select(j => Deserialize<StockAccount>(j)!)
                         .Where(a => !a.Disabled && StoreData.AccountServesPlanType(a, planType)))
            {
                var run = StoreData.FindConsecutiveAvailable(acc, count);
                if (run is null) continue; // this account can't seat the whole request — try the next one
                foreach (var slot in run)
                {
                    slot.Status = StockItemStatus.Reserved;
                    slot.OrderId = orderId;
                    slot.UnitId = unitId;
                }
                UpsertStockAccount(conn, tx, acc);
                return (acc, run);
            }
            return null;
        });
    }

    public SeatReservation ReserveSeatsAcrossAccounts(int productId, int months, string planType, int count, int orderId, int unitId)
    {
        if (count <= 0) return new SeatReservation(Array.Empty<SeatGroup>(), 0, true);
        return WriteTx((conn, tx) =>
        {
            var accounts = conn.Query<string>(
                    "SELECT DataJson FROM StockAccounts WHERE ProductId = @productId ORDER BY Id", new { productId }, tx)
                .Select(j => Deserialize<StockAccount>(j)!)
                .Where(a => !a.Disabled && StoreData.AccountServesPlanType(a, planType) && StoreData.AccountServesMonths(a, months))
                .ToList();
            var reservation = StoreData.PlanSeatReservation(accounts, count, orderId, unitId, out var modified);
            foreach (var acc in accounts.Where(a => modified.Contains(a.Id)))
                UpsertStockAccount(conn, tx, acc);
            return reservation;
        });
    }

    public bool MarkStockSlotsDelivered(int orderId, int unitId) =>
        WriteTx((conn, tx) => UpdateReservedSlots(conn, tx, orderId, unitId, slot =>
        {
            slot.Status = StockItemStatus.Delivered;
            slot.DeliveredAtUtc = DateTime.UtcNow;
        }));

    public bool ReleaseStockSlots(int orderId, int unitId) =>
        WriteTx((conn, tx) => UpdateReservedSlots(conn, tx, orderId, unitId, slot =>
        {
            slot.Status = StockItemStatus.Available;
            slot.OrderId = null;
            slot.UnitId = null;
        }));

    private bool UpdateReservedSlots(SqliteConnection conn, SqliteTransaction? tx, int orderId, int unitId,
        Action<StockSlot> apply)
    {
        var changed = false;
        foreach (var json in conn.Query<string>("SELECT DataJson FROM StockAccounts", transaction: tx).ToList())
        {
            var acc = Deserialize<StockAccount>(json)!;
            var mine = acc.Slots.Where(s =>
                s.Status == StockItemStatus.Reserved && s.OrderId == orderId && s.UnitId == unitId).ToList();
            if (mine.Count == 0) continue;
            foreach (var slot in mine) apply(slot);
            UpsertStockAccount(conn, tx, acc);
            changed = true;
        }
        return changed;
    }

    public bool MarkStockItemDelivered(int orderId, int unitId) =>
        WriteTx((conn, tx) =>
        {
            var rows = conn.Query<string>("SELECT DataJson FROM StockItems WHERE Status = @status",
                new { status = (int)StockItemStatus.Reserved }, tx);
            var item = rows.Select(j => Deserialize<StockItem>(j)!)
                .FirstOrDefault(s => s.OrderId == orderId && s.UnitId == unitId);
            if (item is null) return false;
            item.Status = StockItemStatus.Delivered;
            item.DeliveredAtUtc = DateTime.UtcNow;
            UpsertStockItem(conn, tx, item);
            return true;
        });
}

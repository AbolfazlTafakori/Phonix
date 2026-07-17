using Dapper;
using Microsoft.Data.Sqlite;
using Phonix.Api.Models;

namespace Phonix.Api.Data;

// Stock pool: the virtual inventory of ready-to-deliver items. Same hybrid shape as the rest of the store
// (indexed ProductId/Status columns + the full object in DataJson). The pull path runs inside WriteTx so two
// concurrent orders can never reserve the same item — the IMMEDIATE transaction serializes them exactly like
// wallet debits and stock decrements.
public sealed partial class SqliteDataStore
{
    private static void UpsertStockItem(SqliteConnection conn, SqliteTransaction? tx, StockItem s) =>
        conn.Execute("UPDATE StockItems SET ProductId=@ProductId, Status=@Status, DataJson=@DataJson WHERE Id=@Id",
            new { s.Id, s.ProductId, Status = (int)s.Status, DataJson = Serialize(s) }, tx);

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
            return conn.Execute("DELETE FROM StockItems WHERE Id = @id", new { id }, tx) > 0;
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

using Phonix.Api.Models;

namespace Phonix.Api.Data;

// Stock pool for the legacy JSON store — same semantics as SqliteDataStore.Stock.cs, serialized by _gate.
public partial class StoreData
{
    private readonly List<StockItem> _stockItems = new();
    private int _stockSeq;

    public IReadOnlyList<StockItem> GetStockItems(int? productId = null)
    {
        lock (_gate)
        {
            var items = productId is null ? _stockItems : _stockItems.Where(s => s.ProductId == productId);
            return items.OrderBy(s => s.Id).ToList();
        }
    }

    public StockItem? GetStockItem(int id)
    {
        lock (_gate) return _stockItems.FirstOrDefault(s => s.Id == id);
    }

    public List<StockItem> AddStockItems(int productId, IEnumerable<string> contents, string? addedBy)
    {
        lock (_gate)
        {
            var added = contents.Select(content => new StockItem
            {
                Id = ++_stockSeq,
                ProductId = productId,
                Content = content,
                AddedBy = addedBy,
            }).ToList();
            _stockItems.AddRange(added);
            MarkDirty();
            return added;
        }
    }

    public bool SetStockItemStatus(int id, StockItemStatus status)
    {
        lock (_gate)
        {
            var item = _stockItems.FirstOrDefault(s => s.Id == id);
            if (item is null) return false;
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
            MarkDirty();
            return true;
        }
    }

    public bool DeleteStockItem(int id)
    {
        lock (_gate)
        {
            var item = _stockItems.FirstOrDefault(s => s.Id == id);
            if (item is null || item.Status == StockItemStatus.Delivered) return false;
            _stockItems.Remove(item);
            MarkDirty();
            return true;
        }
    }

    public StockItem? PullStockItem(int productId, int orderId, int unitId)
    {
        lock (_gate)
        {
            var item = _stockItems
                .Where(s => s.ProductId == productId && s.Status == StockItemStatus.Available)
                .OrderBy(s => s.Id)
                .FirstOrDefault();
            if (item is null) return null;
            item.Status = StockItemStatus.Reserved;
            item.OrderId = orderId;
            item.UnitId = unitId;
            MarkDirty();
            return item;
        }
    }

    public bool MarkStockItemDelivered(int orderId, int unitId)
    {
        lock (_gate)
        {
            var item = _stockItems.FirstOrDefault(s =>
                s.Status == StockItemStatus.Reserved && s.OrderId == orderId && s.UnitId == unitId);
            if (item is null) return false;
            item.Status = StockItemStatus.Delivered;
            item.DeliveredAtUtc = DateTime.UtcNow;
            MarkDirty();
            return true;
        }
    }
}

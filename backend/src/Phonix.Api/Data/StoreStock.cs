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

    // ── Multi-seat stock accounts — same semantics as SqliteDataStore.Stock.cs, serialized by _gate ──

    private readonly List<StockAccount> _stockAccounts = new();
    private int _stockAccountSeq;

    public IReadOnlyList<StockAccount> GetStockAccounts(int? productId = null)
    {
        lock (_gate)
        {
            var accounts = productId is null ? _stockAccounts : _stockAccounts.Where(a => a.ProductId == productId);
            return accounts.OrderBy(a => a.Id).ToList();
        }
    }

    public StockAccount? GetStockAccount(int id)
    {
        lock (_gate) return _stockAccounts.FirstOrDefault(a => a.Id == id);
    }

    public StockAccount AddStockAccount(StockAccount account)
    {
        lock (_gate)
        {
            account.Id = ++_stockAccountSeq;
            account.Slots = StockAccount.GenerateSlots(account.Capacity);
            _stockAccounts.Add(account);
            MarkDirty();
            return account;
        }
    }

    public bool DeleteStockAccount(int id)
    {
        lock (_gate)
        {
            var acc = _stockAccounts.FirstOrDefault(a => a.Id == id);
            if (acc is null || acc.Slots.Any(s => s.Status == StockItemStatus.Delivered)) return false;
            _stockAccounts.Remove(acc);
            MarkDirty();
            return true;
        }
    }

    public bool SetStockAccountDisabled(int id, bool disabled)
    {
        lock (_gate)
        {
            var acc = _stockAccounts.FirstOrDefault(a => a.Id == id);
            if (acc is null) return false;
            acc.Disabled = disabled;
            MarkDirty();
            return true;
        }
    }

    public bool SetStockSlotStatus(int accountId, int slotId, StockItemStatus status)
    {
        lock (_gate)
        {
            var slot = _stockAccounts.FirstOrDefault(a => a.Id == accountId)?.Slots.FirstOrDefault(s => s.Id == slotId);
            if (slot is null || !SlotTransitionAllowed(slot.Status, status)) return false;
            slot.Status = status;
            if (status == StockItemStatus.Available) { slot.OrderId = null; slot.UnitId = null; }
            MarkDirty();
            return true;
        }
    }

    // The one shared rule for slot lifecycles (mirrors SetStockItemStatus): Delivered is final.
    internal static bool SlotTransitionAllowed(StockItemStatus from, StockItemStatus to) => (from, to) switch
    {
        (StockItemStatus.Available, StockItemStatus.Disabled) => true,
        (StockItemStatus.Disabled, StockItemStatus.Available) => true,
        (StockItemStatus.Reserved, StockItemStatus.Available) => true, // release an abandoned pull
        _ => false,
    };

    public (StockAccount Account, List<StockSlot> Slots)? ReserveStockSlots(int productId, int count, int orderId, int unitId)
    {
        if (count <= 0) return null;
        lock (_gate)
        {
            foreach (var acc in _stockAccounts.Where(a => a.ProductId == productId && !a.Disabled).OrderBy(a => a.Id))
            {
                var run = FindConsecutiveAvailable(acc, count);
                if (run is null) continue; // this account can't seat the whole request — try the next one
                foreach (var slot in run)
                {
                    slot.Status = StockItemStatus.Reserved;
                    slot.OrderId = orderId;
                    slot.UnitId = unitId;
                }
                MarkDirty();
                return (acc, run);
            }
            return null;
        }
    }

    // First run of `count` Available slots at consecutive indices, scanning in slot order.
    internal static List<StockSlot>? FindConsecutiveAvailable(StockAccount acc, int count)
    {
        var run = new List<StockSlot>();
        foreach (var slot in acc.Slots.OrderBy(s => s.Index))
        {
            if (slot.Status == StockItemStatus.Available
                && (run.Count == 0 || slot.Index == run[^1].Index + 1))
                run.Add(slot);
            else
                run = slot.Status == StockItemStatus.Available ? new List<StockSlot> { slot } : new List<StockSlot>();
            if (run.Count == count) return run;
        }
        return null;
    }

    public bool MarkStockSlotsDelivered(int orderId, int unitId)
    {
        lock (_gate)
        {
            var changed = false;
            foreach (var slot in _stockAccounts.SelectMany(a => a.Slots)
                         .Where(s => s.Status == StockItemStatus.Reserved && s.OrderId == orderId && s.UnitId == unitId))
            {
                slot.Status = StockItemStatus.Delivered;
                slot.DeliveredAtUtc = DateTime.UtcNow;
                changed = true;
            }
            if (changed) MarkDirty();
            return changed;
        }
    }

    public bool ReleaseStockSlots(int orderId, int unitId)
    {
        lock (_gate)
        {
            var changed = false;
            foreach (var slot in _stockAccounts.SelectMany(a => a.Slots)
                         .Where(s => s.Status == StockItemStatus.Reserved && s.OrderId == orderId && s.UnitId == unitId))
            {
                slot.Status = StockItemStatus.Available;
                slot.OrderId = null;
                slot.UnitId = null;
                changed = true;
            }
            if (changed) MarkDirty();
            return changed;
        }
    }
}

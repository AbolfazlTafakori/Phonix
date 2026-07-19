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

    // The one shared rule for editing an account in place: identity and every slot's lifecycle are preserved,
    // only the credentials/metadata change. Capacity grows by appending fresh slots (labels continue the same
    // sequence) and shrinks only when every dropped slot is still free. Returns false when it can't shrink.
    internal static bool ApplyAccountEdit(StockAccount acc, string username, string? encryptedPassword, string plan,
        string planType, int capacity, int months)
    {
        if (capacity < acc.Capacity && acc.Slots.Any(s => s.Index >= capacity && s.Status != StockItemStatus.Available))
            return false;

        acc.Username = username;
        if (encryptedPassword is not null) acc.Password = encryptedPassword;
        acc.Plan = plan;
        acc.PlanType = planType;
        acc.Months = months;

        if (capacity > acc.Capacity)
        {
            var nextId = acc.Slots.Count == 0 ? 1 : acc.Slots.Max(s => s.Id) + 1;
            for (var i = acc.Capacity; i < capacity; i++)
                acc.Slots.Add(new StockSlot { Id = nextId++, Index = i, Label = StockAccount.SlotLabel(i) });
        }
        else if (capacity < acc.Capacity)
        {
            acc.Slots.RemoveAll(s => s.Index >= capacity);
        }
        acc.Capacity = capacity;
        return true;
    }

    public StockAccount? UpdateStockAccount(int id, string username, string? encryptedPassword, string plan,
        string planType, int capacity, int months)
    {
        lock (_gate)
        {
            var acc = _stockAccounts.FirstOrDefault(a => a.Id == id);
            if (acc is null || !ApplyAccountEdit(acc, username, encryptedPassword, plan, planType, capacity, months))
                return null;
            MarkDirty();
            return acc;
        }
    }

    public bool DeleteStockAccount(int id, bool force = false)
    {
        lock (_gate)
        {
            var acc = _stockAccounts.FirstOrDefault(a => a.Id == id);
            if (acc is null) return false;
            if (!force && acc.Slots.Any(s => s.Status == StockItemStatus.Delivered)) return false;
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

    // An account serves a purchase when it has no bound plan type (legacy «any»), or its bound type matches
    // the purchased plan's type.
    internal static bool AccountServesPlanType(StockAccount acc, string planType) =>
        string.IsNullOrWhiteSpace(acc.PlanType)
        || string.Equals(acc.PlanType.Trim(), (planType ?? "").Trim(), StringComparison.Ordinal);

    public (StockAccount Account, List<StockSlot> Slots)? ReserveStockSlots(int productId, int count, string planType, int orderId, int unitId)
    {
        if (count <= 0) return null;
        lock (_gate)
        {
            foreach (var acc in _stockAccounts
                         .Where(a => a.ProductId == productId && !a.Disabled && AccountServesPlanType(a, planType))
                         .OrderBy(a => a.Id))
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

    // ── Multi-inventory allocation (feature: an order unit may draw seats from several accounts) ──────

    // An account matches an order's subscription length when its Months equals the order's, or when the order
    // carries no machine-readable duration (months <= 0) — legacy orders then match any account of the type.
    internal static bool AccountServesMonths(StockAccount acc, int months) => months <= 0 || acc.Months == months;

    // Seats already Reserved for (orderId, unitId) on an account, in seat order. Counting these makes the
    // allocation idempotent: a retried approval tops the unit up to its target instead of double-booking.
    internal static List<StockSlot> HeldSlots(StockAccount acc, int orderId, int unitId) =>
        acc.Slots.Where(s => s.Status == StockItemStatus.Reserved && s.OrderId == orderId && s.UnitId == unitId)
            .OrderBy(s => s.Index).ToList();

    // Plans (and applies) the allocation over already-filtered, oldest-first compatible accounts: it keeps the
    // seats the unit already holds, then takes every Available seat from each account in turn — oldest account
    // first — until `count` seats are held or the pool is exhausted. Mutates the taken slots to Reserved and
    // reports which accounts changed so the caller can persist exactly those. Never releases anything.
    internal static SeatReservation PlanSeatReservation(
        IReadOnlyList<StockAccount> accounts, int count, int orderId, int unitId, out HashSet<int> modified)
    {
        modified = new HashSet<int>();
        var groups = new Dictionary<int, (StockAccount Account, List<StockSlot> Slots)>();
        var held = 0;

        // Seats already held for this unit count first (idempotent top-up), oldest account first.
        foreach (var acc in accounts)
        {
            var existing = HeldSlots(acc, orderId, unitId);
            if (existing.Count == 0) continue;
            groups[acc.Id] = (acc, existing);
            held += existing.Count;
        }

        // Then draw more Available seats, oldest account first, until the target is met or nothing is left.
        foreach (var acc in accounts)
        {
            if (held >= count) break;
            var take = acc.Slots.Where(s => s.Status == StockItemStatus.Available).OrderBy(s => s.Index)
                .Take(count - held).ToList();
            if (take.Count == 0) continue;
            foreach (var slot in take)
            {
                slot.Status = StockItemStatus.Reserved;
                slot.OrderId = orderId;
                slot.UnitId = unitId;
            }
            modified.Add(acc.Id);
            if (groups.TryGetValue(acc.Id, out var g))
                groups[acc.Id] = (acc, g.Slots.Concat(take).OrderBy(s => s.Index).ToList());
            else
                groups[acc.Id] = (acc, take);
            held += take.Count;
        }

        var ordered = accounts
            .Where(a => groups.ContainsKey(a.Id))
            .Select(a => new SeatGroup(a, groups[a.Id].Slots))
            .ToList();
        return new SeatReservation(ordered, held, held >= count);
    }

    public SeatReservation ReserveSeatsAcrossAccounts(int productId, int months, string planType, int count, int orderId, int unitId)
    {
        if (count <= 0) return new SeatReservation(Array.Empty<SeatGroup>(), 0, true);
        lock (_gate)
        {
            var accounts = _stockAccounts
                .Where(a => a.ProductId == productId && !a.Disabled
                            && AccountServesPlanType(a, planType) && AccountServesMonths(a, months))
                .OrderBy(a => a.Id).ToList();
            var reservation = PlanSeatReservation(accounts, count, orderId, unitId, out var modified);
            if (modified.Count > 0) MarkDirty();
            return reservation;
        }
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

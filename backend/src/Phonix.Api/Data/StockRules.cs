using Phonix.Api.Models;

namespace Phonix.Api.Data;

// The rules the stock pool obeys, independent of how it is stored. They live here rather than on a store so
// both persistence layers answer the same questions the same way — the copies that used to sit on each store
// were the reason a fix could land in one and be missed in the other.
public static class StockRules
{
    // The one shared rule for slot lifecycles (mirrors SetStockItemStatus): Delivered is final.
    public static bool SlotTransitionAllowed(StockItemStatus from, StockItemStatus to) => (from, to) switch
    {
        (StockItemStatus.Available, StockItemStatus.Disabled) => true,
        (StockItemStatus.Disabled, StockItemStatus.Available) => true,
        (StockItemStatus.Reserved, StockItemStatus.Available) => true, // release an abandoned pull
        _ => false,
    };

    // An account serves a purchase when it has no bound plan type (legacy «any»), or its bound type matches
    // the purchased plan's type.
    public static bool AccountServesPlanType(StockAccount acc, string planType) =>
        string.IsNullOrWhiteSpace(acc.PlanType)
        || string.Equals(acc.PlanType.Trim(), (planType ?? "").Trim(), StringComparison.Ordinal);

    // First run of `count` Available slots at consecutive indices, scanning in slot order.
    public static List<StockSlot>? FindConsecutiveAvailable(StockAccount acc, int count)
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

    // An account matches an order's subscription length when its Months equals the order's, or when the order
    // carries no machine-readable duration (months <= 0) — legacy orders then match any account of the type.
    public static bool AccountServesMonths(StockAccount acc, int months) => months <= 0 || acc.Months == months;

    // Seats already Reserved for (orderId, unitId) on an account, in seat order. Counting these makes the
    // allocation idempotent: a retried approval tops the unit up to its target instead of double-booking.
    public static List<StockSlot> HeldSlots(StockAccount acc, int orderId, int unitId) =>
        acc.Slots.Where(s => s.Status == StockItemStatus.Reserved && s.OrderId == orderId && s.UnitId == unitId)
            .OrderBy(s => s.Index).ToList();

    // Plans (and applies) the allocation over already-filtered, oldest-first compatible accounts: it keeps the
    // seats the unit already holds, then takes every Available seat from each account in turn — oldest account
    // first — until `count` seats are held or the pool is exhausted. Mutates the taken slots to Reserved and
    // reports which accounts changed so the caller can persist exactly those. Never releases anything.
    public static SeatReservation PlanSeatReservation(
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

    // The one shared rule for editing an account in place: identity and every slot's lifecycle are preserved,
    // only the credentials/metadata change. Capacity grows by appending fresh slots (labels continue the same
    // sequence) and shrinks only when every dropped slot is still free. Returns false when it can't shrink.
    public static bool ApplyAccountEdit(StockAccount acc, string username, string? encryptedPassword, string plan,
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
}

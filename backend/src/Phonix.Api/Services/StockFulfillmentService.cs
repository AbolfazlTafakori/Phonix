using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Services;

// The one place the stock pool is turned into a delivery. Every route that confirms an order's payment goes
// through AutoDeliverOrder — checkout paid entirely from the wallet, the receipts page, the transactions page
// and the receipt bot — so a pool-enabled product delivers itself no matter who approved the money. Living in
// a service rather than a controller is the whole point: the bots never reach controller code.
public interface IStockFulfillmentService
{
    // Serves every still-pending unit of pool-enabled products from the pool. Units the pool can't cover are
    // left for manual fulfillment (clean degrade). Never throws: an approval must not fail over the pool.
    void AutoDeliverOrder(Order order);

    // Same, for a just-approved order payment: resolves the transaction's order first. No-op for anything else.
    void AutoDeliverForTransaction(Transaction tx);

    // Serves ONE unit on demand, regardless of the product's auto-deliver switch — that switch only decides
    // whether payment alone delivers; a staff member explicitly approving an account may always use the pool.
    // Returns null when the pool has nothing (or the unit is already settled), which is the caller's cue to
    // ask for the account by hand.
    (Order order, bool justCompleted)? ServeUnit(Order order, OrderUnit unit, string actor);

    // Re-applies the CURRENT slot-delivery format to every already-delivered slot account, rebuilding each
    // unit's content from the account + the seats it holds. Returns how many units were rewritten.
    int ReformatDeliveredSlotOrders();
}

public sealed class StockFulfillmentService : IStockFulfillmentService
{
    public const string Actor = "انبار مجازی";

    private readonly IDataStore _store;
    private readonly ILogger<StockFulfillmentService> _logger;

    public StockFulfillmentService(IDataStore store, ILogger<StockFulfillmentService> logger)
    {
        _store = store;
        _logger = logger;
    }

    public void AutoDeliverOrder(Order order)
    {
        try
        {
            if (order.Status != OrderStatus.Preparing) return;
            foreach (var unit in order.Units.Where(u => !u.Delivered && !u.Rejected).ToList())
            {
                var product = _store.GetProduct(unit.ProductId);
                if (product is null || !product.AutoDeliverStock) continue;
                ServeUnit(order, unit, Actor);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auto delivery from the stock pool failed for order {Code}", order.Code);
        }
    }

    public void AutoDeliverForTransaction(Transaction tx)
    {
        try
        {
            if (tx.Type != TxTypes.OrderPayment || tx.Status != TxStatus.Approved) return;
            if (string.IsNullOrWhiteSpace(tx.OrderCode)) return;
            var order = _store.GetUserOrders(tx.UserId).FirstOrDefault(o => o.Code == tx.OrderCode);
            if (order is null) return;
            AutoDeliverOrder(order);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auto delivery from the stock pool failed for tx #{TxId}", tx.Id);
        }
    }

    public (Order order, bool justCompleted)? ServeUnit(Order order, OrderUnit unit, string actor)
    {
        if (unit.Delivered || unit.Rejected) return null;

        // Slot-fulfilled products are served from a multi-seat account, everything else from the item pool.
        if (_store.GetProduct(unit.ProductId) is { SlotFulfillment: true })
            return ServeFromSlotAccount(order, unit, actor);

        // An earlier pull for this same unit (a deliver modal left open, a retried approval) is reused instead
        // of burning a second account.
        var item = _store.GetStockItems(unit.ProductId).FirstOrDefault(s =>
                       s.Status == StockItemStatus.Reserved && s.OrderId == order.Id && s.UnitId == unit.Id)
                   ?? _store.PullStockItem(unit.ProductId, order.Id, unit.Id);
        if (item is null) return null; // pool empty

        var (updated, justCompleted) = _store.DeliverUnit(order.Id, unit.Id, SensitiveField.Reveal(item.Content), actor);
        if (updated is null)
        {
            _store.SetStockItemStatus(item.Id, StockItemStatus.Available); // delivery refused → put it back
            return null;
        }

        _store.MarkStockItemDelivered(order.Id, unit.Id);
        _logger.LogInformation("Stock pool delivered item {ItemId} to order {Code} unit {UnitId}",
            item.Id, order.Code, unit.Id);
        return (updated, justCompleted);
    }

    public int ReformatDeliveredSlotOrders()
    {
        var updated = 0;
        // Every delivered seat carries the order+unit it served; grouping them reconstructs each delivered
        // unit's account and its exact seats, from which the current format is rebuilt.
        var groups = _store.GetStockAccounts()
            .SelectMany(a => a.Slots
                .Where(s => s.Status == StockItemStatus.Delivered && s.OrderId is not null && s.UnitId is not null)
                .Select(s => (acc: a, slot: s)))
            .GroupBy(x => (OrderId: x.slot.OrderId!.Value, UnitId: x.slot.UnitId!.Value));

        foreach (var g in groups)
        {
            var acc = g.First().acc;
            var slots = g.Select(x => x.slot).OrderBy(s => s.Index).ToList();
            var order = _store.GetOrder(g.Key.OrderId);
            var unit = order?.Units.FirstOrDefault(u => u.Id == g.Key.UnitId);
            if (unit is null || !unit.Delivered) continue;

            var service = StockAccount.DeriveServiceName(_store.GetProduct(unit.ProductId)?.ServiceName, unit.Name);
            var content = BuildSlotDeliveryContent(service, acc, slots);
            if (_store.UpdateDeliveredUnitContent(g.Key.OrderId, g.Key.UnitId, content)) updated++;
        }
        return updated;
    }

    // How many consecutive seats one purchase claims on a shared account. A plan that sells a fixed user
    // count (e.g. «۶ کاربر») seats exactly that many; otherwise it falls back to the legacy model where the
    // cart quantity is itself the number of users on the account.
    internal static int ConnectionCount(Order order, OrderUnit unit)
    {
        var item = order.Items.FirstOrDefault(i =>
            i.ProductId == unit.ProductId && (i.Plan ?? "") == (unit.Plan ?? ""));
        if (unit.UserCount > 0) return unit.UserCount;
        if (item?.UserCount > 0) return item.UserCount;
        return Math.Max(1, item?.Quantity ?? 1);
    }

    // The plan type an order unit belongs to. Unit.Plan is «{Type} · {Months} ماهه»; the part before «·» is
    // the type used to route the purchase to the matching accounts.
    internal static string PlanType(string? plan) =>
        string.IsNullOrWhiteSpace(plan) ? "" : plan.Split('·')[0].Trim();

    private (Order order, bool justCompleted)? ServeFromSlotAccount(Order order, OrderUnit unit, string actor)
    {
        var count = ConnectionCount(order, unit);
        var planType = PlanType(unit.Plan);

        // An earlier reservation for this same unit (a retried approval, a stale tap) is reused instead of
        // claiming a second run of seats.
        var reservation = FindReservation(unit.ProductId, order.Id, unit.Id)
                          ?? _store.ReserveStockSlots(unit.ProductId, count, planType, order.Id, unit.Id);
        if (reservation is not { } r) return null; // no account of this plan type has enough consecutive seats

        var service = StockAccount.DeriveServiceName(_store.GetProduct(unit.ProductId)?.ServiceName, unit.Name);
        var content = BuildSlotDeliveryContent(service, r.Account, r.Slots);
        var (updated, justCompleted) = _store.DeliverUnit(order.Id, unit.Id, content, actor);
        if (updated is null)
        {
            _store.ReleaseStockSlots(order.Id, unit.Id); // delivery refused → seats go back
            return null;
        }

        _store.MarkStockSlotsDelivered(order.Id, unit.Id);
        _logger.LogInformation("Stock account {AccountId} seated {Count} slot(s) for order {Code} unit {UnitId}",
            r.Account.Id, r.Slots.Count, order.Code, unit.Id);
        return (updated, justCompleted);
    }

    private (StockAccount Account, List<StockSlot> Slots)? FindReservation(int productId, int orderId, int unitId)
    {
        foreach (var acc in _store.GetStockAccounts(productId))
        {
            var mine = acc.Slots
                .Where(s => s.Status == StockItemStatus.Reserved && s.OrderId == orderId && s.UnitId == unitId)
                .OrderBy(s => s.Index)
                .ToList();
            if (mine.Count > 0) return (acc, mine);
        }
        return null;
    }

    // A divider the account page renders as a rule (and bolds the first line of the block after it); in plain
    // channels (mail, Telegram) it simply reads as a clean separator between the seats.
    internal const string SeatDivider = "──────────";

    // The customer-facing message. One clean block PER seat — each is a single connection on the shared
    // account, tagged with its own «User : A - 1» label. `serviceName` is the bare product/service name.
    internal static string BuildSlotDeliveryContent(string serviceName, StockAccount acc, List<StockSlot> slots)
    {
        var pass = SensitiveField.Reveal(acc.Password);
        var blocks = slots.OrderBy(s => s.Index).Select(s => string.Join("\n", new[]
        {
            $"{serviceName} 1 Connection {acc.Months} Month",
            "",
            $"User : {acc.Username}",
            "",
            $"Pass : {pass}",
            "",
            $"Plan : {acc.Plan}",
            "",
            $"User : {StockAccount.SlotDisplayLabel(s.Index)}",
        }));
        return string.Join($"\n\n{SeatDivider}\n\n", blocks);
    }
}

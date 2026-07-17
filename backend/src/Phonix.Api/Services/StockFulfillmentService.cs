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
}

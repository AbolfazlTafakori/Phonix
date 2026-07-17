namespace Phonix.Api.Models;

public enum StockItemStatus
{
    Available,
    Reserved,   // pulled for an order unit but not delivered yet (released back if the delivery is abandoned)
    Delivered,
    Disabled,   // burned/expired credential the admin took out of rotation without losing its history
}

// One ready-to-deliver item in a product's virtual stock pool: a set of account credentials, a gift code, a
// license key. Admins load these in bulk ahead of time; fulfillment then pulls the next available item into
// the deliver flow (manually via the deliver modal, or automatically when the product opts in).
public class StockItem
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    // The deliverable payload. Stored encrypted (SensitiveField) — these are live credentials, so they get
    // the same at-rest protection as the sensitive inputs customers type at checkout.
    public string Content { get; set; } = "";
    public StockItemStatus Status { get; set; } = StockItemStatus.Available;
    // When Reserved/Delivered: the order unit that consumed it, so support can trace "which account did this
    // buyer get" straight from the pool.
    public int? OrderId { get; set; }
    public int? UnitId { get; set; }
    public string? Note { get; set; }
    public string? AddedBy { get; set; }
    public DateTime AddedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? DeliveredAtUtc { get; set; }
}

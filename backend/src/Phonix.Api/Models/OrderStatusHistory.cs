namespace Phonix.Api.Models;

// One immutable audit entry recording an order status transition: who changed it, from/to, why, and when.
// Stored as a nested append-only list on the order (Order.History), so it persists with the order and is
// returned alongside it. The id is unique within the order.
public class OrderStatusHistory
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string ChangedByUsername { get; set; } = "";
    public OrderStatus FromStatus { get; set; }
    public OrderStatus ToStatus { get; set; }
    public string? Reason { get; set; }
    public DateTime ChangedAtUtc { get; set; }
}

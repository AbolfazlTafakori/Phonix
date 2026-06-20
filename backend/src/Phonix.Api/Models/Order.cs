namespace Phonix.Api.Models;

public enum OrderStatus
{
    PendingApproval,
    Preparing,
    Completed,
    Cancelled,
}

public class OrderItem
{
    public int ProductId { get; set; }
    public string Name { get; set; } = "";
    public string Image { get; set; } = "";
    public string? Plan { get; set; }
    public long UnitPrice { get; set; }
    public int Quantity { get; set; }
    public long LineTotal => UnitPrice * Quantity;
}

public class Order
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public List<OrderItem> Items { get; set; } = new();
    public long Subtotal { get; set; }
    public string? DiscountCode { get; set; }
    public long DiscountAmount { get; set; }
    public long WalletPaid { get; set; }
    public long FeeAmount { get; set; }
    public long Total { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.PendingApproval;
    public string PaymentMethod { get; set; } = "";
    public string Date { get; set; } = "";
    public string? Note { get; set; }
    public string? DeliveryContent { get; set; }
    public string? DeliveredAt { get; set; }
}

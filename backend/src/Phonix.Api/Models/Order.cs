namespace Phonix.Api.Models;

public enum OrderStatus
{
    PendingApproval,
    Preparing,
    Completed,
    Cancelled,
}

// A single value the customer supplied for a plan's required input at checkout (see PlanInputField).
// Sensitive values (passwords) are stored encrypted in Value; the flag tells the order view to decrypt for
// display and keeps them out of plain backups.
public class OrderInputValue
{
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
    public bool Sensitive { get; set; }
}

public class OrderItem
{
    public int ProductId { get; set; }
    public string Name { get; set; } = "";
    public string Image { get; set; } = "";
    public string? Plan { get; set; }
    // Info the customer entered for this line at checkout (per the plan's PlanInputField list), plus an
    // optional free-text note. Empty when the plan collects nothing.
    public List<OrderInputValue> CustomerInputs { get; set; } = new();
    public string? CustomerNote { get; set; }
    // Machine-readable plan duration in months, captured at order time (null = a one-off item with no
    // time-based subscription). Used by the renewal-reminder worker to compute expiry; the human-readable
    // `Plan` string above is for display only.
    public int? PlanMonths { get; set; }
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
    public long VatAmount { get; set; }
    public long FeeAmount { get; set; }
    public long Total { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.PendingApproval;
    public string PaymentMethod { get; set; } = "";
    // receipt for the gateway/card remainder paid out of band at checkout (proof staff verify before approval).
    public string? ReceiptUrl { get; set; }
    public string Date { get; set; } = "";
    public string? Note { get; set; }
    public string? DeliveryContent { get; set; }
    // Human-readable Jalali delivery date (display). The real timestamp below is what drives expiry math.
    public string? DeliveredAt { get; set; }
    // Real UTC moment the order was delivered/completed; the base for subscription expiry calculations.
    public DateTime? DeliveredAtUtc { get; set; }
    // Set once when a renewal reminder has been sent, so the background worker never reminds twice.
    public DateTime? RenewalReminderSentUtc { get; set; }
    // Append-only audit trail of status changes (who/from/to/why/when).
    public List<OrderStatusHistory> History { get; set; } = new();
}

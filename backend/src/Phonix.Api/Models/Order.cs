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
    // The plan the customer actually chose, kept as an id so fulfilment can look its terms up rather than
    // parsing the display label. For a V2Ray-linked product this is the V2RayPlan id (see ApplyV2RayPlans),
    // which is what provisioning needs to know which panel, inbounds and limits to create the account with.
    public int? PlanId { get; set; }
    // How many users/seats the chosen plan covers, captured at order time (0 = the plan sells no fixed seat
    // count). For slot-fulfilled products this is how many consecutive seats one purchase claims on a shared
    // account; it's also shown as «تعداد کاربر» on the fulfillment/receipt messages.
    public int UserCount { get; set; }
    public long UnitPrice { get; set; }
    public int Quantity { get; set; }
    public long LineTotal => UnitPrice * Quantity;
}

// One deliverable unit of an order — a single account/seat. A line with quantity 2 produces two units, each
// fulfilled independently so several technical admins can work the same order in parallel without clashing.
// Each unit carries the info the customer supplied for it at checkout and the delivery content staff write
// back. Sensitive customer values are stored encrypted (see SensitiveField); DeliveryContent is plain.
public class OrderUnit
{
    public int Id { get; set; }            // unique within the order; used to address it from the panel
    public int ProductId { get; set; }
    public string Name { get; set; } = "";
    public string Image { get; set; } = "";
    public string? Plan { get; set; }
    // Mirrored from the line so fulfilment can read the chosen plan's terms straight off the unit it is
    // serving (see OrderItem.PlanId).
    public int? PlanId { get; set; }
    public int UserCount { get; set; }     // seats the plan covers, mirrored from the line (see OrderItem.UserCount)
    public int UnitIndex { get; set; }     // 1-based position within its product line ("اکانت اول/دوم")
    // What the customer entered for THIS unit at checkout, plus their optional note.
    public List<OrderInputValue> CustomerInputs { get; set; } = new();
    public string? CustomerNote { get; set; }
    // What staff prepared for the customer (saved as a draft, or the final delivered content).
    public string DeliveryContent { get; set; } = "";
    public bool Delivered { get; set; }
    public string? DeliveredAt { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
    // Staff rejected THIS account (the rest of the order can still be delivered). The buyer is refunded what
    // they actually paid for it — its price after its share of the order discount — and the amount is kept
    // here so the refund is auditable and can never be paid twice.
    public bool Rejected { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime? RejectedAtUtc { get; set; }
    public long RefundedAmount { get; set; }
    // Set when the seat pool couldn't fully cover this unit at approval time. The seats it DID get are kept
    // Reserved (never released) and the unit waits in FIFO order for new compatible inventory, which the pool
    // applies automatically. A waiting unit is never delivered partially. Cleared once fully delivered.
    public bool WaitingForInventory { get; set; }
    // Last staff member who saved a draft or delivered this unit — shown so a second admin sees who's on it.
    public string? HandledBy { get; set; }
    // Set once this account has been posted to the orders group. The order-level stamp can't serve here: a
    // self-provisioning account is announced when its service actually exists, which is after the rest of the
    // order was announced, so each account needs its own claim to be posted exactly once.
    public DateTime? BotNotifiedAtUtc { get; set; }

    // ── V2Ray provisioning ──────────────────────────────────────────────────────────────────────────────
    // Set when this unit was served by creating an account on a V2Ray panel instead of pulling one from the
    // stock pool. The panel is the source of truth for live usage; these are the handles needed to find the
    // account again and to render the customer's config page.
    public V2RayAccount? V2Ray { get; set; }

    // Set when the customer bought this unit to EXTEND a config they already hold, rather than to get a new
    // one. It is the config token of the account being renewed; fulfilment updates that account on the panel
    // in place — same UUID, same subscription link, same config page — instead of creating a second client.
    public string? V2RayRenewToken { get; set; }
}

// The account created on a V2Ray panel for one order unit. `Token` is the unguessable key to the public
// config page — the buyer may hand that link to whoever the service is for (a colleague, a family member),
// so it is deliberately shareable and carries no account or order identifiers.
public class V2RayAccount
{
    public int PanelId { get; set; }
    public int PlanId { get; set; }
    public string Email { get; set; } = "";   // the client's name on the panel; also the traffic lookup key
    public string Uuid { get; set; } = "";
    public string SubId { get; set; } = "";
    public string SubUrl { get; set; } = "";
    public string Token { get; set; } = "";
    public string Protocol { get; set; } = "";
    public string Network { get; set; } = "";
    public long VolumeGb { get; set; }
    public int DurationDays { get; set; }
    public int IpLimit { get; set; }
    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    // Provisioning is retried in the background, so a panel that is briefly unreachable never blocks an
    // approval. These record how that is going for the staff view.
    public int Attempts { get; set; }
    public string? LastError { get; set; }

    // ── Renewal ─────────────────────────────────────────────────────────────────────────────────────
    // A renewal extends THIS record in place; the customer keeps the same link. The counters are what the
    // config page and the staff view read to say "renewed 3 times, last on …".
    public int RenewCount { get; set; }
    public DateTime? LastRenewedAtUtc { get; set; }

    // ── Warnings (V2RayMonitorWorker) ───────────────────────────────────────────────────────────────
    // Stamped the moment a warning is claimed, under the store's write lock, so a customer is warned at most
    // once per service term even across restarts or a second server in the cluster. A renewal clears both,
    // which is what re-arms them for the new term.
    public DateTime? ExpiryWarnSentUtc { get; set; }
    public DateTime? VolumeWarnSentUtc { get; set; }

    // ── Panel clean-up ──────────────────────────────────────────────────────────────────────────────
    // Set once the account has been removed from the panel — either by us (the grace period after its time
    // ran out elapsed with no renewal) or by an operator deleting it by hand, which the sweep notices as the
    // client no longer being on the panel. Either way it stops the sweep touching this account again, and
    // the config page says the service has ended rather than showing stale numbers.
    public DateTime? PanelDeletedAtUtc { get; set; }
    public string? PanelDeletedReason { get; set; }
}

public class Order
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public List<OrderItem> Items { get; set; } = new();
    // Per-account deliverable units (one per quantity). Drives the fulfillment section and the customer's
    // per-account delivery view. Older orders placed before this feature have an empty list and fall back to
    // the single order-level DeliveryContent below.
    public List<OrderUnit> Units { get; set; } = new();
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
    // Unique 16-digit invoice number, minted once the order reaches Completed (i.e. it has actually been
    // delivered). Null before that: an order that was never delivered has no invoice.
    public string? InvoiceNumber { get; set; }
    public string? DeliveryContent { get; set; }
    // Human-readable Jalali delivery date (display). The real timestamp below is what drives expiry math.
    public string? DeliveredAt { get; set; }
    // Real UTC moment the order was delivered/completed; the base for subscription expiry calculations.
    public DateTime? DeliveredAtUtc { get; set; }
    // Set once when a renewal reminder has been sent, so the background worker never reminds twice.
    public DateTime? RenewalReminderSentUtc { get; set; }
    // Set once when the order bot has announced this order to the orders group. An order can reach
    // «آماده‌سازی» from several paths (panel approve, receipt approve in the panel, receipt approve in
    // Telegram) and each account is its own message, so this stamp is what stops a re-approval from
    // spamming the group with a second full set.
    public DateTime? OrderBotNotifiedAtUtc { get; set; }
    // Append-only audit trail of status changes (who/from/to/why/when).
    public List<OrderStatusHistory> History { get; set; } = new();
}

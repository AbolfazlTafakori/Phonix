namespace Phonix.Api.Models;

public enum PaymentType
{
    Card,
    Crypto,
    Gateway,
}

public class PaymentMethod : IContentItem
{
    public int Id { get; set; }
    public PaymentType Type { get; set; }
    public string Title { get; set; } = "";
    public string Holder { get; set; } = "";
    public string Value { get; set; } = "";
    public string Network { get; set; } = "";
    public string Sheba { get; set; } = "";          // destination IBAN shown for offline card-to-card deposits
    public string AccountNumber { get; set; } = "";   // destination account number shown for offline deposits
    public string Instructions { get; set; } = "";
    public decimal FeePercent { get; set; }   // gateway tax/fee added when this method is used
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public class PaymentSettings
{
    public bool TelegramEnabled { get; set; }
    public string TelegramBotToken { get; set; } = "";
    public string TelegramChatId { get; set; } = "";
    public bool RequireReceipt { get; set; } = true;
    public long AutoApproveUnder { get; set; }
}

public enum TxStatus
{
    Pending,
    Approved,
    Rejected,
}

public class Transaction
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    // links the transaction to its owner so balances are credited to the right account and a user's
    // history is filtered by id (not the fragile display name).
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public string Type { get; set; } = "";
    public long Amount { get; set; }
    public TxStatus Status { get; set; } = TxStatus.Pending;
    public string Method { get; set; } = "";
    public string? ReceiptUrl { get; set; }
    // offline card-to-card deposit details: the registered card the buyer paid from, the bank tracking
    // number, the payment date, and an optional buyer note.
    public string? SourceCard { get; set; }
    // Holder name on the source card, and the destination card (number + holder) the buyer paid TO. Captured
    // at creation so the Telegram receipt message is self-contained even if a card/method is later edited.
    public string? SourceHolder { get; set; }
    public string? DestinationCard { get; set; }
    public string? DestinationHolder { get; set; }
    public string? TrackingNumber { get; set; }
    public string? PaymentDate { get; set; }
    public string? Description { get; set; }
    // set on an "پرداخت سفارش" transaction to link it back to the order it pays; approving the
    // transaction advances that order.
    public string? OrderCode { get; set; }
    public string? ApprovedVia { get; set; }
    public string Date { get; set; } = "";
    public string? Note { get; set; }
}

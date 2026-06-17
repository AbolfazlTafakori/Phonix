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
    public string Instructions { get; set; } = "";
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
    public string UserName { get; set; } = "";
    public string Type { get; set; } = "";
    public long Amount { get; set; }
    public TxStatus Status { get; set; } = TxStatus.Pending;
    public string Method { get; set; } = "";
    public string? ReceiptUrl { get; set; }
    public string? ApprovedVia { get; set; }
    public string Date { get; set; } = "";
    public string? Note { get; set; }
}

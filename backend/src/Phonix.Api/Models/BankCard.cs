namespace Phonix.Api.Models;

public enum BankCardStatus
{
    Pending,
    Approved,
    Rejected,
}

// A bank card the customer has registered. Wallet top-ups (card-to-card) may only be paid from one of
// their own Approved cards — the holder name is copied from the user's approved KYC so a card can only
// be in the verified person's name.
public class BankCard
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public string CardNumber { get; set; } = "";   // 16 digits, normalized
    public string HolderName { get; set; } = "";    // the name on the card, entered by the user
    public string CardImage { get; set; } = "";     // photo of the card, for staff to verify against the name
    public string Bank { get; set; } = "";          // best-effort from the card BIN
    public string? Sheba { get; set; }
    public BankCardStatus Status { get; set; } = BankCardStatus.Pending;
    public string? Note { get; set; }
    // Explicit reason shown to the user when their card is rejected. Set on reject, cleared on approve.
    // Mirrors Note for backward compatibility.
    public string? RejectionReason { get; set; }
    // "site" or "telegram" — where staff decided it.
    public string? DecidedVia { get; set; }
    // Who decided it (a staff username, or the Telegram name of whoever tapped) and when. With the channel this is
    // what the other side shows once it is decided: a panel and a bot must never both act on one request.
    public string? DecidedBy { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    // The bot message that put this up for review, so a decision made on the site can rewrite it at once.
    public long? TelegramChatId { get; set; }
    public int? TelegramMessageId { get; set; }
    public string Date { get; set; } = "";
}

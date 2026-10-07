namespace Phonix.Api.Models;

public enum KycStatus
{
    Pending,
    Approved,
    Rejected,
}

public class KycRequest
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string FullName { get; set; } = "";
    public string NationalId { get; set; } = "";
    public string BirthDate { get; set; } = "";
    public string CardImage { get; set; } = "";
    public string SelfieImage { get; set; } = "";
    public KycStatus Status { get; set; } = KycStatus.Pending;
    public string? Note { get; set; }
    // Explicit reason shown to the user when their KYC is rejected (so they know what to fix). Set on
    // reject, cleared on approve/resubmit. Mirrors Note for backward compatibility.
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

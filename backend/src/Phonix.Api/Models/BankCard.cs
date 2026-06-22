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
    public string Date { get; set; } = "";
}

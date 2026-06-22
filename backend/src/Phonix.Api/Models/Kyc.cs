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
    public string Date { get; set; } = "";
}

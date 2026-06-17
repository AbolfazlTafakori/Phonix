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
    public string Date { get; set; } = "";
}

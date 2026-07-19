namespace Phonix.Api.Models;

public enum SeatSubmissionStatus
{
    Pending = 0,   // waiting for staff to look at it; the customer may still change it
    Reviewed = 1,  // staff acted on it — locked for the customer from here on
}

// Information a customer supplies AFTER delivery, for ONE seat of a shared account. Some services need
// something from the buyer before the seat can actually be set up (a device screenshot, a username, an
// address). A purchase that covers several seats gets one of these PER SEAT, so each person on the account
// files their own details independently.
//
// The image is not stored here — only the opaque id of a file in PROTECTED storage (same scheme as KYC), so a
// customer's picture is never reachable by URL and only its owner (or staff) can stream it back.
public class SeatSubmission
{
    public int Id { get; set; }
    public int UserId { get; set; }   // owner; taken from the session on write, never from the client
    public int OrderId { get; set; }
    public int UnitId { get; set; }
    // Which seat of the unit this belongs to: the 0-based position within the unit's delivered seat list, plus
    // the label the customer sees («A - 8»). The index is the identity; the label is carried for display so the
    // admin queue reads the same thing the customer does.
    public int SeatIndex { get; set; }
    public string SeatLabel { get; set; } = "";
    // Denormalized for the review queue, which lists submissions across every order without loading them all.
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string OrderCode { get; set; } = "";
    public string UserName { get; set; } = "";

    public string? ImageId { get; set; }  // protected-storage id; null when the customer only sent text
    public string Text { get; set; } = "";

    public SeatSubmissionStatus Status { get; set; } = SeatSubmissionStatus.Pending;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewNote { get; set; }  // optional message from staff, shown to the customer

    // A submission is the customer's to change right up until staff act on it — after that it's frozen, so the
    // admin never works from details that shift under them.
    public bool Editable => Status == SeatSubmissionStatus.Pending;
}

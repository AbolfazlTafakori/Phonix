namespace Phonix.Api.Models;

public enum SeatSubmissionStatus
{
    Pending = 0,   // waiting for staff to look at it; locked for the customer unless staff reopened it
    Reviewed = 1,  // staff acted on it — locked for the customer from here on
    // Staff turned it down. What the customer sent is moved into History and the entry itself is cleared, so
    // the seat is theirs to file afresh rather than edit around the refused details — while staff keep the
    // record of what was refused. It is not in the staff queue — this one is waiting on the customer.
    Rejected = 2,
}

// The outcome of turning a seat submission down: the cleared record, plus the storage id of the picture it no
// longer shows. The picture itself is kept — the refused version in History still points at it.
public sealed record SeatRejection(SeatSubmission Submission, string? RemovedImageId);

// One thing that happened to a seat submission, for the history staff read to see what the customer did and
// what was done in reply. By is the staff member's username; null means the customer.
public class SeatSubmissionEvent
{
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    // submitted | edited | reviewed | reopened | rejected
    public string Action { get; set; } = "";
    public string? By { get; set; }
    public string? Note { get; set; }
}

// One earlier state of a seat submission, kept when the customer replaces it.
public class SeatSubmissionVersion
{
    public string? ImageId { get; set; }
    public string Text { get; set; } = "";
    public string SeatLabel { get; set; } = "";
    public DateTime SubmittedAtUtc { get; set; }
    public SeatSubmissionStatus Status { get; set; }   // what staff had made of it when it was replaced
    public string? ReviewNote { get; set; }
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
    // How many post-approval changes this seat is allowed, SNAPSHOT from the plan when the submission is first
    // filed — raising or lowering the plan's limit later never changes what an existing buyer was promised.
    public int EditLimit { get; set; }
    public int EditsUsed { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewNote { get; set; }  // optional message from staff, shown to the customer

    // What this seat held before each change, newest first. A seat reopened for a correction usually comes
    // back with a different device, and staff still need the one they set up last time — so an edit moves the
    // previous details here instead of overwriting them. A rejection is the exception: it wipes on purpose.
    public List<SeatSubmissionVersion> History { get; set; } = new();

    // Every action on this seat, oldest first: filed, edited, reviewed, reopened, rejected — who and when.
    public List<SeatSubmissionEvent> Events { get; set; } = new();

    // Set when staff hand the seat back for a correction (reopen), and cleared by the customer's next save or by
    // staff approving it as it stands — so one reopen allows exactly one change.
    public bool ReopenedForEdit { get; set; }

    // Filed details are FROZEN. The customer files once; after that only staff can let them change anything —
    // by reopening the seat, or by rejecting it (which asks for new details). Details that shift under staff
    // while they work from them, or after a seat was set up on a device, are exactly what this prevents. The
    // plan's post-approval allowance (EditLimit) no longer lets a customer edit on their own.
    public bool Editable => Status == SeatSubmissionStatus.Rejected || ReopenedForEdit || ReopenedBeforeTheFlag;

    // Seats reopened before ReopenedForEdit existed carry no flag; the customer was still told to send new
    // details. For those, "the last thing that happened was a reopen" is the same fact.
    private bool ReopenedBeforeTheFlag => Events is { Count: > 0 } e && e[^1].Action == "reopened";
    // Changes still available to the customer once this seat has been approved.
    public int EditsLeft => Math.Max(0, EditLimit - EditsUsed);
}

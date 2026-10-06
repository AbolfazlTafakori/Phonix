using Phonix.Api.Models;

namespace Phonix.Api.Data;

// How a customer's per-seat submission changes once it exists — shared so both stores age it identically.
public static class SeatSubmissionRules
{
    // Enough to cover every device a seat realistically goes through, without letting one seat grow forever.
    public const int MaxHistory = 10;
    // The action log is small per entry; the cap only stops a seat edited endlessly from growing without bound.
    public const int MaxEvents = 60;

    public static void Log(SeatSubmission s, string action, string? by, string? note = null)
    {
        s.Events.Add(new SeatSubmissionEvent { Action = action, By = by, Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim() });
        if (s.Events.Count > MaxEvents) s.Events.RemoveRange(0, s.Events.Count - MaxEvents);
    }

    // What the seat currently holds, as a version for History — taken before a change or a rejection clears it.
    public static SeatSubmissionVersion Snapshot(SeatSubmission s) => new()
    {
        ImageId = s.ImageId,
        Text = s.Text,
        SeatLabel = s.SeatLabel,
        SubmittedAtUtc = s.UpdatedAtUtc,
        Status = s.Status,
        ReviewNote = s.ReviewNote,
    };

    public static void PushHistory(SeatSubmission s, SeatSubmissionVersion version)
    {
        s.History.Insert(0, version);
        if (s.History.Count > MaxHistory) s.History.RemoveRange(MaxHistory, s.History.Count - MaxHistory);
    }

    // The one shared rule for applying a customer's edit (mirrored by SqliteDataStore.SeatSubmissions.cs).
    // Changing an ALREADY-APPROVED seat spends one of its allowances and sends it back to the queue, so staff
    // re-approve what they're actually working from rather than silently inheriting a change.
    public static void ApplyEdit(SeatSubmission existing, SeatSubmission input)
    {
        // Snapshot BEFORE the status below is reset, so the version records what staff had made of it. Only
        // when the details actually change: re-saving the same thing is not a new version, and a rejected
        // seat has nothing left to keep.
        var newImage = input.ImageId ?? existing.ImageId;
        var hadContent = existing.Text.Length > 0 || existing.ImageId is not null;
        if (hadContent && (input.Text != existing.Text || newImage != existing.ImageId))
            PushHistory(existing, Snapshot(existing));
        Log(existing, existing.Status == SeatSubmissionStatus.Rejected ? "submitted" : "edited", by: null);

        if (existing.Status == SeatSubmissionStatus.Reviewed)
        {
            existing.EditsUsed++;
            existing.Status = SeatSubmissionStatus.Pending;
            existing.ReviewedAtUtc = null;
            existing.ReviewedBy = null;
        }
        else if (existing.Status == SeatSubmissionStatus.Rejected)
        {
            // Re-filing after a rejection is what staff asked for, so it spends no allowance. The reason goes
            // with it: it described details that no longer exist, and leaving it up would read as a verdict on
            // what the customer has just sent.
            existing.Status = SeatSubmissionStatus.Pending;
            existing.ReviewedAtUtc = null;
            existing.ReviewedBy = null;
            existing.ReviewNote = null;
        }
        existing.ImageId = input.ImageId ?? existing.ImageId; // keeping the old picture is a valid edit
        existing.Text = input.Text;
        existing.SeatLabel = input.SeatLabel;
        existing.UpdatedAtUtc = DateTime.UtcNow;
    }
}

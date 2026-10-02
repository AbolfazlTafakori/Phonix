using Phonix.Api.Models;

namespace Phonix.Api.Data;

// How a customer's per-seat submission changes once it exists — shared so both stores age it identically.
public static class SeatSubmissionRules
{
    // Enough to cover every device a seat realistically goes through, without letting one seat grow forever.
    public const int MaxHistory = 10;

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
        {
            existing.History.Insert(0, new SeatSubmissionVersion
            {
                ImageId = existing.ImageId,
                Text = existing.Text,
                SeatLabel = existing.SeatLabel,
                SubmittedAtUtc = existing.UpdatedAtUtc,
                Status = existing.Status,
                ReviewNote = existing.ReviewNote,
            });
            if (existing.History.Count > MaxHistory) existing.History.RemoveRange(MaxHistory, existing.History.Count - MaxHistory);
        }

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

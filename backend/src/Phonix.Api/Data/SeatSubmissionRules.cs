using Phonix.Api.Models;

namespace Phonix.Api.Data;

// How a customer's per-seat submission changes once it exists — shared so both stores age it identically.
public static class SeatSubmissionRules
{
    // The one shared rule for applying a customer's edit (mirrored by SqliteDataStore.SeatSubmissions.cs).
    // Changing an ALREADY-APPROVED seat spends one of its allowances and sends it back to the queue, so staff
    // re-approve what they're actually working from rather than silently inheriting a change.
    public static void ApplyEdit(SeatSubmission existing, SeatSubmission input)
    {
        if (existing.Status == SeatSubmissionStatus.Reviewed)
        {
            existing.EditsUsed++;
            existing.Status = SeatSubmissionStatus.Pending;
            existing.ReviewedAtUtc = null;
            existing.ReviewedBy = null;
        }
        existing.ImageId = input.ImageId ?? existing.ImageId; // keeping the old picture is a valid edit
        existing.Text = input.Text;
        existing.SeatLabel = input.SeatLabel;
        existing.UpdatedAtUtc = DateTime.UtcNow;
    }
}

using Phonix.Api.Models;

namespace Phonix.Api.Data;

// Per-seat customer submissions for the legacy JSON store — same semantics as
// SqliteDataStore.SeatSubmissions.cs, serialized by _gate.
public partial class StoreData
{
    private readonly List<SeatSubmission> _seatSubmissions = new();
    private int _seatSubmissionSeq;

    public IReadOnlyList<SeatSubmission> GetSeatSubmissions(SeatSubmissionStatus? status = null)
    {
        lock (_gate)
        {
            IEnumerable<SeatSubmission> q = _seatSubmissions;
            if (status is SeatSubmissionStatus s) q = q.Where(x => x.Status == s);
            return q.OrderByDescending(x => x.Id).ToList();
        }
    }

    public IReadOnlyList<SeatSubmission> GetSeatSubmissionsForUnit(int orderId, int unitId)
    {
        lock (_gate)
        {
            return _seatSubmissions.Where(x => x.OrderId == orderId && x.UnitId == unitId)
                .OrderBy(x => x.SeatIndex).ToList();
        }
    }

    public SeatSubmission? GetSeatSubmission(int id)
    {
        lock (_gate) return _seatSubmissions.FirstOrDefault(x => x.Id == id);
    }

    // One submission per (order, unit, seat): re-sending replaces the seat's own entry instead of piling up, so
    // the queue always shows the customer's current answer. A reviewed entry is frozen — the caller checks
    // Editable first and this refuses as a second line of defence.
    public SeatSubmission? SaveSeatSubmission(SeatSubmission input)
    {
        lock (_gate)
        {
            var existing = _seatSubmissions.FirstOrDefault(x =>
                x.OrderId == input.OrderId && x.UnitId == input.UnitId && x.SeatIndex == input.SeatIndex);
            if (existing is null)
            {
                input.Id = ++_seatSubmissionSeq;
                input.Status = SeatSubmissionStatus.Pending;
                input.CreatedAtUtc = input.UpdatedAtUtc = DateTime.UtcNow;
                _seatSubmissions.Add(input);
                MarkDirty();
                return input;
            }

            if (!existing.Editable) return null;
            existing.ImageId = input.ImageId ?? existing.ImageId; // keeping the old picture is a valid edit
            existing.Text = input.Text;
            existing.SeatLabel = input.SeatLabel;
            existing.UpdatedAtUtc = DateTime.UtcNow;
            MarkDirty();
            return existing;
        }
    }

    public SeatSubmission? ReviewSeatSubmission(int id, string? reviewedBy, string? note)
    {
        lock (_gate)
        {
            var item = _seatSubmissions.FirstOrDefault(x => x.Id == id);
            if (item is null) return null;
            item.Status = SeatSubmissionStatus.Reviewed;
            item.ReviewedBy = reviewedBy;
            item.ReviewedAtUtc = DateTime.UtcNow;
            item.ReviewNote = note;
            MarkDirty();
            return item;
        }
    }

    // Reopening hands the seat back to the customer — the way to ask for a corrected picture or text.
    public SeatSubmission? ReopenSeatSubmission(int id, string? note)
    {
        lock (_gate)
        {
            var item = _seatSubmissions.FirstOrDefault(x => x.Id == id);
            if (item is null) return null;
            item.Status = SeatSubmissionStatus.Pending;
            item.ReviewedAtUtc = null;
            item.ReviewNote = note;
            MarkDirty();
            return item;
        }
    }
}

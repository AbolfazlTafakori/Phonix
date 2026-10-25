using Dapper;
using Microsoft.Data.Sqlite;
using Phonix.Api.Models;

namespace Phonix.Api.Data;

// Per-seat customer submissions. Same hybrid shape as the rest of the store (indexed lookup columns + the full
// object in DataJson). Writes run inside WriteTx so a customer double-tapping «ذخیره» can't create two rows for
// the same seat — the uniqueness is enforced by the read-then-write inside one IMMEDIATE transaction.
public sealed partial class SqliteDataStore
{
    private void UpsertSeatSubmission(SqliteConnection conn, SqliteTransaction? tx, SeatSubmission s)
    {
        var json = Serialize(s);
        conn.Execute(@"
UPDATE SeatSubmissions SET UserId=@UserId, OrderId=@OrderId, UnitId=@UnitId, Status=@Status, DataJson=@DataJson
WHERE Id=@Id",
            new { s.Id, s.UserId, s.OrderId, s.UnitId, Status = (int)s.Status, DataJson = json }, tx);
        if (tx is not null) AppendOutbox(conn, tx, "SeatSubmissions", s.Id, SyncOp.Upsert, json);
    }

    // Restore path: writes a row with its original Id, straight from a snapshot (no outbox — a restore re-seeds
    // the sync state wholesale).
    internal void InsertSeatSubmissionRow(SqliteConnection conn, SqliteTransaction? tx, SeatSubmission s) =>
        conn.Execute(@"
INSERT INTO SeatSubmissions (Id, UserId, OrderId, UnitId, Status, DataJson)
VALUES (@Id, @UserId, @OrderId, @UnitId, @Status, @DataJson)",
            new { s.Id, s.UserId, s.OrderId, s.UnitId, Status = (int)s.Status, DataJson = Serialize(s) }, tx);

    public IReadOnlyList<SeatSubmission> GetSeatSubmissions(SeatSubmissionStatus? status = null)
    {
        using var conn = OpenConnection();
        var sql = "SELECT DataJson FROM SeatSubmissions"
                  + (status is null ? "" : " WHERE Status = @status") + " ORDER BY Id DESC";
        return conn.Query<string>(sql, new { status = (int?)status })
            .Select(j => Deserialize<SeatSubmission>(j)!).ToList();
    }

    public IReadOnlyList<SeatSubmission> GetSeatSubmissionsForUnit(int orderId, int unitId)
    {
        using var conn = OpenConnection();
        return conn.Query<string>(
                "SELECT DataJson FROM SeatSubmissions WHERE OrderId = @orderId AND UnitId = @unitId",
                new { orderId, unitId })
            .Select(j => Deserialize<SeatSubmission>(j)!)
            .OrderBy(s => s.SeatIndex).ToList();
    }

    public IReadOnlyList<SeatSubmission> GetUserSeatSubmissions(int userId)
    {
        using var conn = OpenConnection();
        return conn.Query<string>("SELECT DataJson FROM SeatSubmissions WHERE UserId = @userId", new { userId })
            .Select(j => Deserialize<SeatSubmission>(j)!).ToList();
    }

    public SeatSubmission? GetSeatSubmission(int id) => OneJson<SeatSubmission>("SeatSubmissions", id);

    public SeatSubmission? SaveSeatSubmission(SeatSubmission input) =>
        WriteTx<SeatSubmission?>((conn, tx) =>
        {
            // The unit's rows are few (one per seat) and SeatIndex lives in DataJson, so the seat is matched in
            // memory rather than with a JSON query — inside the same transaction as the write that follows.
            var existing = conn.Query<string>(
                    "SELECT DataJson FROM SeatSubmissions WHERE OrderId=@OrderId AND UnitId=@UnitId",
                    new { input.OrderId, input.UnitId }, tx)
                .Select(j => Deserialize<SeatSubmission>(j)!)
                .FirstOrDefault(s => s.SeatIndex == input.SeatIndex);

            if (existing is null)
            {
                input.Status = SeatSubmissionStatus.Pending;
                input.CreatedAtUtc = input.UpdatedAtUtc = DateTime.UtcNow;
                SeatSubmissionRules.Log(input, "submitted", by: null);
                var id = (int)conn.ExecuteScalar<long>(@"
INSERT INTO SeatSubmissions (UserId, OrderId, UnitId, Status, DataJson)
VALUES (@UserId, @OrderId, @UnitId, @Status, @DataJson);
SELECT last_insert_rowid();",
                    new { input.UserId, input.OrderId, input.UnitId, Status = (int)input.Status, DataJson = Serialize(input) }, tx);
                input.Id = id;
                UpsertSeatSubmission(conn, tx, input);
                return input;
            }

            if (!existing.Editable) return null;
            SeatSubmissionRules.ApplyEdit(existing, input);
            UpsertSeatSubmission(conn, tx, existing);
            return existing;
        });

    public SeatSubmission? ReviewSeatSubmission(int id, string? reviewedBy, string? note) =>
        MutateSeatSubmission(id, s =>
        {
            s.Status = SeatSubmissionStatus.Reviewed;
            s.ReviewedBy = reviewedBy;
            s.ReviewedAtUtc = DateTime.UtcNow;
            s.ReviewNote = note;
            // Approving takes back an unused reopen: what staff approved is what stays.
            s.ReopenedForEdit = false;
            SeatSubmissionRules.Log(s, "reviewed", reviewedBy, note);
        });

    public SeatSubmission? ReopenSeatSubmission(int id, string? note, string? reopenedBy = null) =>
        MutateSeatSubmission(id, s =>
        {
            s.Status = SeatSubmissionStatus.Pending;
            s.ReviewedAtUtc = null;
            s.ReviewNote = note;
            // The only way a customer gets to change details they already filed.
            s.ReopenedForEdit = true;
            SeatSubmissionRules.Log(s, "reopened", reopenedBy, note);
        });

    // Rejecting is not "review with a different flag": the details the customer sent are removed, because the
    // seat is going back to them to be filed again and half-kept old values are what makes staff work from the
    // wrong picture. The wipe and the notification commit together — a customer must never find their entry
    // emptied with nothing telling them why.
    public SeatRejection? RejectSeatSubmission(int id, string? rejectedBy, string? reason) =>
        WriteTx<SeatRejection?>((conn, tx) =>
        {
            var json = conn.QueryFirstOrDefault<string>(
                "SELECT DataJson FROM SeatSubmissions WHERE Id = @id", new { id }, tx);
            if (json is null) return null;
            var item = Deserialize<SeatSubmission>(json)!;

            var removedImageId = item.ImageId;
            // What was refused stays on record: staff can still see it, and the customer's next filing sits next
            // to it as the new version.
            if (item.Text.Length > 0 || item.ImageId is not null)
            {
                var refused = SeatSubmissionRules.Snapshot(item);
                refused.Status = SeatSubmissionStatus.Rejected;
                refused.ReviewNote = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
                SeatSubmissionRules.PushHistory(item, refused);
            }
            SeatSubmissionRules.Log(item, "rejected", rejectedBy, reason);
            item.ImageId = null;
            item.Text = "";
            item.Status = SeatSubmissionStatus.Rejected;
            item.ReviewedBy = rejectedBy;
            item.ReviewedAtUtc = DateTime.UtcNow;
            item.ReviewNote = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            item.UpdatedAtUtc = DateTime.UtcNow;
            UpsertSeatSubmission(conn, tx, item);

            Notify(conn, tx, item.UserId,
                OrderNotices.SeatInfoRejected(item.OrderCode, item.ProductName, SeatName(item), item.ReviewNote));
            return new SeatRejection(item, removedImageId);
        });

    // A seat the buyer has never filed anything for still needs somewhere to keep what staff did about it —
    // the warnings sent, the warranty taken away. That is this stub: same row, Status NotFiled, so it never
    // appears in the review queue and never pretends the buyer sent something.
    public SeatSubmission EnsureSeatSubmission(SeatSubmission stub) =>
        WriteTx((conn, tx) =>
        {
            var existing = conn.Query<string>(
                    "SELECT DataJson FROM SeatSubmissions WHERE OrderId=@OrderId AND UnitId=@UnitId",
                    new { stub.OrderId, stub.UnitId }, tx)
                .Select(j => Deserialize<SeatSubmission>(j)!)
                .FirstOrDefault(s => s.SeatIndex == stub.SeatIndex);
            if (existing is not null) return existing;

            stub.Status = SeatSubmissionStatus.NotFiled;
            stub.Text = "";
            stub.ImageId = null;
            stub.CreatedAtUtc = stub.UpdatedAtUtc = DateTime.UtcNow;
            var id = (int)conn.ExecuteScalar<long>(@"
INSERT INTO SeatSubmissions (UserId, OrderId, UnitId, Status, DataJson)
VALUES (@UserId, @OrderId, @UnitId, @Status, @DataJson);
SELECT last_insert_rowid();",
                new { stub.UserId, stub.OrderId, stub.UnitId, Status = (int)stub.Status, DataJson = Serialize(stub) }, tx);
            stub.Id = id;
            UpsertSeatSubmission(conn, tx, stub);
            return stub;
        })!;

    // A warning goes out once per level: resending level 1 would restart a clock the buyer has already been
    // given, so the caller is told no instead (null) and the panel offers the next one.
    public SeatSubmission? WarnSeat(int id, int level, string? by, string message) =>
        WriteTx<SeatSubmission?>((conn, tx) =>
        {
            if (level is not (1 or 2)) return null;
            if (LoadSeat(conn, tx, id) is not { } item) return null;
            if (level == 1 && item.Warning1AtUtc is not null) return null;
            if (level == 2 && item.Warning2AtUtc is not null) return null;
            var now = DateTime.UtcNow;
            if (level == 1) { item.Warning1AtUtc = now; item.Warning1By = by; }
            else { item.Warning2AtUtc = now; item.Warning2By = by; }
            item.LastWarningText = message;
            item.UpdatedAtUtc = now;
            SeatSubmissionRules.Log(item, "warned", by, $"هشدار {level}: {message}");
            UpsertSeatSubmission(conn, tx, item);
            Notify(conn, tx, item.UserId,
                OrderNotices.SeatWarning(level, item.OrderCode, item.ProductName, SeatName(item), message));
            return item;
        });

    // Out of warranty. The customer's own entry and its history are left untouched — unlike a rejection, this
    // is not about the details being wrong, and handing the seat back has to give them exactly what they had.
    public SeatSubmission? RevokeSeat(int id, string? by, string reason) =>
        WriteTx<SeatSubmission?>((conn, tx) =>
        {
            if (LoadSeat(conn, tx, id) is not { } item) return null;
            if (item.Revoked) return item;   // already out; doing it twice would notify the buyer twice
            item.RevokedAtUtc = DateTime.UtcNow;
            item.RevokedBy = by;
            item.RevokeReason = reason.Trim();
            item.UpdatedAtUtc = item.RevokedAtUtc.Value;
            SeatSubmissionRules.Log(item, "revoked", by, item.RevokeReason);
            UpsertSeatSubmission(conn, tx, item);
            Notify(conn, tx, item.UserId,
                OrderNotices.SeatRevoked(item.OrderCode, item.ProductName, SeatName(item), item.RevokeReason));
            return item;
        });

    public SeatSubmission? RestoreSeat(int id, string? by) =>
        WriteTx<SeatSubmission?>((conn, tx) =>
        {
            if (LoadSeat(conn, tx, id) is not { } item) return null;
            if (!item.Revoked) return item;
            item.RevokedAtUtc = null;
            item.RevokedBy = null;
            item.RevokeReason = null;
            item.RestoredAtUtc = DateTime.UtcNow;
            item.RestoredBy = by;
            item.UpdatedAtUtc = item.RestoredAtUtc.Value;
            SeatSubmissionRules.Log(item, "restored", by);
            UpsertSeatSubmission(conn, tx, item);
            Notify(conn, tx, item.UserId,
                OrderNotices.SeatRestored(item.OrderCode, item.ProductName, SeatName(item)));
            return item;
        });

    private SeatSubmission? LoadSeat(SqliteConnection conn, SqliteTransaction? tx, int id) =>
        conn.QueryFirstOrDefault<string>("SELECT DataJson FROM SeatSubmissions WHERE Id = @id", new { id }, tx)
            is { } json ? Deserialize<SeatSubmission>(json) : null;

    // What the customer calls this seat. The label is what they picked it by; the index is the fallback for
    // rows filed before labels were carried.
    private static string SeatName(SeatSubmission s) =>
        string.IsNullOrWhiteSpace(s.SeatLabel) ? $"پروفایل {s.SeatIndex + 1}" : s.SeatLabel;

    private SeatSubmission? MutateSeatSubmission(int id, Action<SeatSubmission> apply) =>
        WriteTx<SeatSubmission?>((conn, tx) =>
        {
            var json = conn.QueryFirstOrDefault<string>(
                "SELECT DataJson FROM SeatSubmissions WHERE Id = @id", new { id }, tx);
            if (json is null) return null;
            var item = Deserialize<SeatSubmission>(json)!;
            apply(item);
            UpsertSeatSubmission(conn, tx, item);
            return item;
        });
}

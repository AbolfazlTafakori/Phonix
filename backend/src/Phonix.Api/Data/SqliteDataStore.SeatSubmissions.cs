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
        });

    public SeatSubmission? ReopenSeatSubmission(int id, string? note) =>
        MutateSeatSubmission(id, s =>
        {
            s.Status = SeatSubmissionStatus.Pending;
            s.ReviewedAtUtc = null;
            s.ReviewNote = note;
        });

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

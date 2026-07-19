using Dapper;
using Microsoft.Data.Sqlite;
using Phonix.Api.Models;

namespace Phonix.Api.Data;

// Cluster (HA) sync plumbing: an outbox row per write, a last-writer-wins guard for the rare same-row
// conflict, and the one-time id-band bump that keeps a Standby's own inserts from ever colliding with the
// Primary's. This file is the ONLY place that knows clustering exists — every other partial just gained one
// AppendOutbox(...) call inside its existing per-table upsert/insert/delete helper, so business code (and
// every controller/service above this layer) never has to know sync is happening.
public sealed partial class SqliteDataStore
{
    internal static class SyncOp
    {
        public const string Upsert = "Upsert";
        public const string Delete = "Delete";
    }

    private const string ClusterKey = "cluster";

    // Never re-append a row that was just written BY the sync engine itself — otherwise the two nodes would
    // ping-pong the same change back and forth forever. Set by ClusterSyncService around every ApplyRemoteOp
    // call; flows correctly because WriteTx's callback runs synchronously on the calling (async) context.
    private static readonly AsyncLocal<bool> _applyingRemote = new();

    // Every table that participates in cluster sync — also drives the one-time id-band bump on a Standby and
    // the outbox re-seed after a restore. Deliberately excludes the Singletons/Counters tables (settings,
    // favorites, plan types, …): low-volume, rarely-conflicting config that isn't wired into sync yet.
    internal static readonly string[] SyncedTables =
    {
        "Users", "Orders", "Products", "StockItems", "StockAccounts", "Transactions", "Cards",
        "Notifications", "Categories", "Plans", "HeroSlides", "HomeCategories", "Showcase", "BlogPosts",
        "Comments", "Tickets", "Conversations", "Kyc", "PaymentMethods", "DiscountCodes", "ReferralEarnings",
    };

    private static void TouchRowVersion(SqliteConnection conn, SqliteTransaction tx, string table, long id, string whenUtcIso) =>
        conn.Execute(@"
INSERT INTO SyncRowVersion (EntityTable, EntityId, LastWriteUtc) VALUES (@table, @id, @when)
ON CONFLICT(EntityTable, EntityId) DO UPDATE SET LastWriteUtc = excluded.LastWriteUtc;",
            new { table, id, when = whenUtcIso }, tx);

    // Records one local write. No-ops entirely when clustering is off (the standalone default) or while the
    // sync engine is applying a row it just pulled from the peer. Called from inside the SAME transaction as
    // the real write, in every per-table upsert/insert/delete helper across the other partials — so the
    // outbox entry can never exist without the write it describes, or vice versa.
    private void AppendOutbox(SqliteConnection conn, SqliteTransaction tx, string table, long id, string op, string? dataJson)
    {
        if (!_clusterEnabled || _applyingRemote.Value) return;
        var now = DateTime.UtcNow.ToString("o");
        conn.Execute("INSERT INTO SyncOutbox (EntityTable, EntityId, Op, DataJson, CreatedAtUtc) VALUES (@table, @id, @op, @dataJson, @now)",
            new { table, id, op, dataJson, now }, tx);
        TouchRowVersion(conn, tx, table, id, now);
    }

    // ── Live cluster role (a Singletons row, not the env var — see PHONIX_CLUSTER_MODE's own comment) ─────
    public ClusterState GetClusterState() => GetSingleton<ClusterState>(ClusterKey);

    public void SetClusterState(ClusterState state)
    {
        using var conn = OpenConnection();
        WriteSingleton(conn, null, ClusterKey, state);
    }

    // ── Outbox read side (the puller's whole query: "everything after my cursor") ───────────────────────
    public IReadOnlyList<SyncOutboxEntry> GetOutboxSince(long cursor, int batchSize = 500)
    {
        using var conn = OpenConnection();
        return conn.Query<SyncOutboxEntry>(@"
SELECT Id, EntityTable, EntityId, Op, DataJson, CreatedAtUtc FROM SyncOutbox
WHERE Id > @cursor ORDER BY Id LIMIT @batchSize;",
            new { cursor, batchSize }).ToList();
    }

    public long GetOutboxHighWaterMark()
    {
        using var conn = OpenConnection();
        return conn.ExecuteScalar<long?>("SELECT MAX(Id) FROM SyncOutbox") ?? 0;
    }

    // ── Applying a row pulled from the peer ─────────────────────────────────────────────────────────────
    // Last-writer-wins by timestamp against SyncRowVersion: if this node's own copy of the row was written
    // (locally OR by an earlier remote apply) at or after the incoming entry's time, the incoming write is
    // skipped — deterministic on both sides, so two nodes that swap conflicting edits during a partition
    // converge on the same winner instead of permanently diverging. New rows never reach this branch with a
    // real conflict (see the id-band bump), so this only matters for edits to pre-existing rows.
    public bool ApplyRemoteOp(SyncOutboxEntry entry)
    {
        if (!SyncedTables.Contains(entry.EntityTable)) return false; // never trust an unknown table name into raw SQL
        return WriteTx((conn, tx) =>
        {
            var localWhen = conn.QueryFirstOrDefault<string>(
                "SELECT LastWriteUtc FROM SyncRowVersion WHERE EntityTable = @t AND EntityId = @id",
                new { t = entry.EntityTable, id = entry.EntityId }, tx);
            if (localWhen is not null && string.CompareOrdinal(localWhen, entry.CreatedAtUtc) >= 0)
                return true; // local write already at or after this point in time — nothing to apply, cursor still advances

            _applyingRemote.Value = true;
            try
            {
                if (entry.Op == SyncOp.Delete)
                {
                    conn.Execute($"DELETE FROM {entry.EntityTable} WHERE Id = @id", new { id = entry.EntityId }, tx);
                }
                else if (entry.DataJson is not null)
                {
                    ApplyUpsertByTable(conn, tx, entry.EntityTable, entry.EntityId, entry.DataJson);
                }
                TouchRowVersion(conn, tx, entry.EntityTable, entry.EntityId, entry.CreatedAtUtc);
                return true;
            }
            finally { _applyingRemote.Value = false; }
        });
    }

    // Routes a remote row to the SAME per-table upsert helper local writes already use for the tables that
    // have extra indexed columns (so those columns — Status, UserId, ProductId, … — never go stale); every
    // other synced table is plain Id+DataJson and gets the one generic upsert.
    private void ApplyUpsertByTable(SqliteConnection conn, SqliteTransaction tx, string table, long id, string json)
    {
        switch (table)
        {
            case "Users": UpsertUser(conn, tx, Deserialize<AppUser>(json)!); break;
            case "Orders": UpsertOrder(conn, tx, Deserialize<Order>(json)!); break;
            case "Products": UpsertProduct(conn, tx, Deserialize<Product>(json)!); break;
            case "StockItems": UpsertStockItemRow(conn, tx, Deserialize<StockItem>(json)!); break;
            case "StockAccounts": UpsertStockAccountRow(conn, tx, Deserialize<StockAccount>(json)!); break;
            case "Transactions": UpsertTransactionRow(conn, tx, Deserialize<Transaction>(json)!); break;
            case "Cards": UpsertCardRow(conn, tx, Deserialize<BankCard>(json)!); break;
            case "Notifications": UpsertNotificationRow(conn, tx, Deserialize<Notification>(json)!); break;
            case "DiscountCodes": UpsertDiscountCodeRow(conn, tx, Deserialize<DiscountCode>(json)!); break;
            case "ReferralEarnings": UpsertReferralEarningRow(conn, tx, id, Deserialize<ReferralEarning>(json)!); break;
            default: UpsertSimpleRow(conn, tx, table, id, json); break; // Categories/Plans/HeroSlides/… (plain Id+DataJson)
        }
    }

    private static void UpsertSimpleRow(SqliteConnection conn, SqliteTransaction tx, string table, long id, string json) =>
        conn.Execute($@"
INSERT INTO {table} (Id, DataJson) VALUES (@id, @json)
ON CONFLICT(Id) DO UPDATE SET DataJson = excluded.DataJson;", new { id, json }, tx);

    // StockItems/StockAccounts already have a per-table Upsert helper (SqliteDataStore.Stock.cs), but it's
    // UPDATE-only there — local writes always INSERT the bare row first (to get the autoincrement id) and
    // then call it to embed the full DataJson, so it never needs to handle "row doesn't exist yet". A remote
    // row DOES arrive with its id already decided, so the apply path needs a genuine insert-or-update variant.
    private static void UpsertStockItemRow(SqliteConnection conn, SqliteTransaction tx, StockItem s) =>
        conn.Execute(@"
INSERT INTO StockItems (Id, ProductId, Status, DataJson) VALUES (@Id, @ProductId, @Status, @DataJson)
ON CONFLICT(Id) DO UPDATE SET ProductId=excluded.ProductId, Status=excluded.Status, DataJson=excluded.DataJson;",
            new { s.Id, s.ProductId, Status = (int)s.Status, DataJson = Serialize(s) }, tx);

    private static void UpsertStockAccountRow(SqliteConnection conn, SqliteTransaction tx, StockAccount a) =>
        conn.Execute(@"
INSERT INTO StockAccounts (Id, ProductId, DataJson) VALUES (@Id, @ProductId, @DataJson)
ON CONFLICT(Id) DO UPDATE SET ProductId=excluded.ProductId, DataJson=excluded.DataJson;",
            new { a.Id, a.ProductId, DataJson = Serialize(a) }, tx);

    // Typed upsert-by-explicit-id helpers for the tables that carry extra indexed columns but, unlike
    // Users/Orders/Products, never needed one before now — local writes to these tables still use their own
    // existing insert/update call sites unchanged; only the remote-apply path above calls these.
    private static void UpsertTransactionRow(SqliteConnection conn, SqliteTransaction tx, Transaction t) =>
        conn.Execute(@"
INSERT INTO Transactions (Id, UserId, Status, Date, DataJson) VALUES (@Id, @UserId, @Status, @Date, @DataJson)
ON CONFLICT(Id) DO UPDATE SET UserId=excluded.UserId, Status=excluded.Status, Date=excluded.Date, DataJson=excluded.DataJson;",
            new { t.Id, t.UserId, Status = (int)t.Status, t.Date, DataJson = Serialize(t) }, tx);

    private static void UpsertCardRow(SqliteConnection conn, SqliteTransaction tx, BankCard c) =>
        conn.Execute(@"
INSERT INTO Cards (Id, UserId, Status, DataJson) VALUES (@Id, @UserId, @Status, @DataJson)
ON CONFLICT(Id) DO UPDATE SET UserId=excluded.UserId, Status=excluded.Status, DataJson=excluded.DataJson;",
            new { c.Id, c.UserId, Status = (int)c.Status, DataJson = Serialize(c) }, tx);

    private static void UpsertNotificationRow(SqliteConnection conn, SqliteTransaction tx, Notification n) =>
        conn.Execute(@"
INSERT INTO Notifications (Id, UserId, DataJson) VALUES (@Id, @UserId, @DataJson)
ON CONFLICT(Id) DO UPDATE SET UserId=excluded.UserId, DataJson=excluded.DataJson;",
            new { n.Id, n.UserId, DataJson = Serialize(n) }, tx);

    private static void UpsertDiscountCodeRow(SqliteConnection conn, SqliteTransaction tx, DiscountCode d) =>
        conn.Execute(@"
INSERT INTO DiscountCodes (Id, Code, DataJson) VALUES (@Id, @Code, @DataJson)
ON CONFLICT(Id) DO UPDATE SET Code=excluded.Code, DataJson=excluded.DataJson;",
            new { d.Id, d.Code, DataJson = Serialize(d) }, tx);

    // ReferralEarning carries no Id property of its own (it always relied on the table's bare rowid), so the
    // id travels as a separate parameter here rather than through the model.
    private static void UpsertReferralEarningRow(SqliteConnection conn, SqliteTransaction tx, long id, ReferralEarning r) =>
        conn.Execute(@"
INSERT INTO ReferralEarnings (Id, ReferrerId, DataJson) VALUES (@id, @ReferrerId, @DataJson)
ON CONFLICT(Id) DO UPDATE SET ReferrerId=excluded.ReferrerId, DataJson=excluded.DataJson;",
            new { id, r.ReferrerId, DataJson = Serialize(r) }, tx);

    // ── Sync dead-letter queue (mechanism for Fix 5: one bad event must never wedge the cluster) ─────────
    // The offset every Standby's id band starts at. Public so the sync service and tests share one constant.
    public const long StandbyIdBandOffset = 1_000_000_000;

    // Parks a remote entry that failed to apply, or bumps its retry counter/last-error if it is already
    // parked. Keyed by the origin OutboxId so the same event is never dead-lettered twice.
    public void RecordSyncFailure(SyncOutboxEntry entry, string error)
    {
        var now = DateTime.UtcNow.ToString("o");
        var trimmed = error.Length > 1000 ? error[..1000] : error;
        WriteTx<object?>((conn, tx) =>
        {
            conn.Execute(@"
INSERT INTO SyncDeadLetter (OutboxId, EntityTable, EntityId, Op, DataJson, CreatedAtUtc, RetryCount, LastError, FirstFailedUtc, LastAttemptUtc)
VALUES (@Id, @EntityTable, @EntityId, @Op, @DataJson, @CreatedAtUtc, 1, @err, @now, @now)
ON CONFLICT(OutboxId) DO UPDATE SET RetryCount = RetryCount + 1, LastError = @err, LastAttemptUtc = @now;",
                new { entry.Id, entry.EntityTable, entry.EntityId, entry.Op, entry.DataJson, entry.CreatedAtUtc, err = trimmed, now }, tx);
            return null;
        });
    }

    public void ClearSyncFailure(long outboxId) =>
        WriteTx<object?>((conn, tx) => { conn.Execute("DELETE FROM SyncDeadLetter WHERE OutboxId = @outboxId", new { outboxId }, tx); return null; });

    // The still-retryable dead-lettered entries (below the retry cap), oldest first — the sync loop reattempts
    // these each cycle. Entries at/over the cap stay parked and are surfaced in the cluster status count.
    public IReadOnlyList<SyncOutboxEntry> GetRetryableDeadLetters(int maxRetries, int limit = 100)
    {
        using var conn = OpenConnection();
        return conn.Query<SyncOutboxEntry>(@"
SELECT OutboxId AS Id, EntityTable, EntityId, Op, DataJson, CreatedAtUtc FROM SyncDeadLetter
WHERE RetryCount < @maxRetries ORDER BY OutboxId LIMIT @limit;",
            new { maxRetries, limit }).ToList();
    }

    public long GetDeadLetterCount()
    {
        using var conn = OpenConnection();
        return conn.ExecuteScalar<long>("SELECT COUNT(1) FROM SyncDeadLetter");
    }

    // ── Standby bootstrap: restore the Primary's initial snapshot, then start incremental pulls (Fix 3) ────
    // A fresh Standby attaching to an already-populated Primary can't reach a consistent state through the
    // incremental outbox alone (the outbox only holds writes since it was enabled, not the pre-existing rows).
    // So it pulls one full snapshot, restores it wholesale, wipes any local sync bookkeeping, and pins its
    // cursor to the Primary's high-water mark — every later change flows through the ordinary pull loop. Unlike
    // LoadSnapshot (which re-seeds the outbox for the peer to pull back), this is the RECEIVING side: it must
    // NOT create outbox rows, or the Standby would immediately try to push the Primary's own data back to it.
    public void RestoreFromPeerSnapshot(StoreSnapshot snapshot, long peerHighWaterMark, string? peerDataEpoch = null)
    {
        LoadSnapshotTx(snapshot);
        WriteTx<object?>((conn, tx) =>
        {
            conn.Execute("DELETE FROM SyncOutbox; DELETE FROM SyncRowVersion; DELETE FROM SyncDeadLetter;", tx);
            return null;
        });
        var state = GetClusterState();
        state.LastAppliedCursor = peerHighWaterMark;
        state.BootstrappedAtUtc = DateTime.UtcNow;
        // Records which lineage of the peer's data this node is now an exact copy of, so a later restore on
        // the peer is detected on the next pull.
        state.PeerDataEpoch = peerDataEpoch;
        SetClusterState(state);
    }

    // ── One-time id-band bump (Standby setup only) ──────────────────────────────────────────────────────
    // Reserves a disjoint autoincrement range for this node so it can NEVER mint an id the peer could also
    // mint, even during a genuine network partition where both nodes accept writes — no per-write
    // coordination needed, nothing else in the app changes. 1,000,000,000 (not a larger round number) is
    // deliberate: every insert path narrows `last_insert_rowid()` (a 64-bit value) to a 32-bit `int` with an
    // unchecked cast, so the offset must stay safely clear of int.MaxValue (2,147,483,647) while still
    // leaving this node well over a billion ids of headroom.
    public void BumpAutoincrementOffset(long offset)
    {
        WriteTx<object?>((conn, tx) =>
        {
            foreach (var table in SyncedTables)
            {
                conn.Execute("UPDATE sqlite_sequence SET seq = seq + @offset WHERE name = @table", new { offset, table }, tx);
                conn.Execute(@"
INSERT INTO sqlite_sequence (name, seq) SELECT @table, @offset
WHERE NOT EXISTS (SELECT 1 FROM sqlite_sequence WHERE name = @table);", new { table, offset }, tx);
            }
            return null;
        });
    }

    // Idempotently reserves this node's Standby id band exactly once, recording the fact in ClusterState so a
    // restart never re-applies it (which would shift ids into a second, overlapping band). Returns true if it
    // performed the bump this call. This is the piece that was missing: BumpAutoincrementOffset existed but
    // nothing ever invoked it, so a Standby's inserts could collide with the Primary's during a partition.
    public bool EnsureStandbyIdBand()
    {
        var state = GetClusterState();
        if (state.IdBandApplied) return false;
        BumpAutoincrementOffset(StandbyIdBandOffset);
        state = GetClusterState();
        state.IdBandApplied = true;
        SetClusterState(state);
        return true;
    }

    // ── Force a clean re-sync after a restore (mechanism 8) ─────────────────────────────────────────────
    // A restore replaces the database wholesale, so any cursor position the peer thinks it's at is
    // meaningless afterward. Rather than try to reconcile stale cursors against just-replaced data, the
    // outbox is cleared and re-seeded with one fresh Upsert row per row now present in every synced table —
    // an ordinary batch of ordinary outbox rows through the same incremental-pull mechanism, not a new
    // full-snapshot wire format. Called from LoadSnapshot/RestoreSection, inside their own transaction.
    public int ReseedOutboxFromCurrentState()
    {
        // Rotating the epoch is what makes a restore visible to the peer. The reseeded outbox below is
        // upserts only — it can describe every row that now exists, but nothing about the rows the restore
        // removed. A peer replaying those upserts converges on the restored rows while quietly keeping its
        // own copies of everything that was deleted, and no counter or health check ever reports it. The new
        // epoch tells the peer its cursor belongs to a lineage that no longer exists, so it re-bootstraps.
        var state = GetClusterState();
        state.DataEpoch = Guid.NewGuid().ToString("N");
        SetClusterState(state);
        return ReseedOutboxRows();
    }

    private int ReseedOutboxRows() =>
        WriteTx((conn, tx) =>
        {
            conn.Execute("DELETE FROM SyncOutbox", tx);
            conn.Execute("DELETE FROM SyncRowVersion", tx);
            var seeded = 0;
            var now = DateTime.UtcNow.ToString("o");
            foreach (var table in SyncedTables)
            {
                foreach (var row in conn.Query<(long Id, string DataJson)>($"SELECT Id, DataJson FROM {table}", transaction: tx).ToList())
                {
                    conn.Execute("INSERT INTO SyncOutbox (EntityTable, EntityId, Op, DataJson, CreatedAtUtc) VALUES (@table, @id, @op, @json, @now)",
                        new { table, id = row.Id, op = SyncOp.Upsert, json = row.DataJson, now }, tx);
                    TouchRowVersion(conn, tx, table, row.Id, now);
                    seeded++;
                }
            }
            return seeded;
        });
}

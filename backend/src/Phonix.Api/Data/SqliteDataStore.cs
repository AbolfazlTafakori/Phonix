using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Microsoft.Data.Sqlite;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Data;

// SQLite-backed implementation of the data layer (step 2 of the persistence migration — see the project
// memory). It is the live store: it implements the full IDataStore contract and is registered as the single
// IDataStore in Program.cs, so every controller/service runs against it unchanged. The legacy JSON StoreData
// survives only as the one-time bootstrap seed source (see the migration block in Program.cs after Build()).
//
// ── How _gate's guarantees are preserved without a process-wide lock ──────────────────────────────────
// The old global `_gate` serialized EVERY operation. Here, concurrency is delegated to SQLite:
//   • WAL lets unlimited readers run concurrently with a single writer (no reader/writer blocking).
//   • Every read-modify-write that must be atomic (wallet debits, order placement, stock decrement) runs
//     inside a BEGIN IMMEDIATE transaction (WriteTx). IMMEDIATE takes the write lock at BEGIN, so two
//     concurrent money operations are serialized: the second blocks until the first COMMITs, then re-reads
//     the already-updated state. That is exactly the integrity `_gate` provided — check-then-write can never
//     interleave — now scoped per-operation instead of stopping the whole app.
//   • busy_timeout makes a contended writer wait (up to 5s) instead of failing instantly.
//
// ── Hybrid schema ────────────────────────────────────────────────────────────────────────────────────
// Each table stores the heavily-queried fields as real, indexed columns (for O(log n) lookups and filters)
// PLUS the full domain object as a `DataJson` text column. Reads deserialize DataJson, so the C# models stay
// completely untouched; writes update both the columns and DataJson in one statement.
public sealed partial class SqliteDataStore : IDataStore
{
    private readonly string _connString;

    // Compact per-row JSON, enum-as-string to match the snapshot/backup format (DeserializeSnapshot uses the
    // same enum handling), so a DataJson value is byte-for-byte the shape the rest of the system expects.
    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    // Cluster (HA) support: off by default, so a standalone install pays nothing beyond this one field read
    // at startup. See SqliteDataStore.Cluster.cs for everything this gates.
    private readonly bool _clusterEnabled;

    // dbPath is optional so tests can point at a unique temp file; in production it resolves from
    // PHONIX_DB_FILE or co-locates with the other durable state. (All-optional ctor stays DI-constructable.)
    // clusterEnabledOverride lets tests exercise the sync-outbox path without mutating the process-wide
    // PHONIX_CLUSTER_MODE env var (which would be flaky under parallel test execution); production callers
    // never pass it, so behavior there is governed by the env var exactly as before.
    public SqliteDataStore(string? dbPath = null, bool? clusterEnabledOverride = null)
    {
        dbPath ??= ResolveDbPath();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);

        _connString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Pooling = true,                     // reuse handles across requests
            Cache = SqliteCacheMode.Default,    // private cache + WAL is the recommended high-concurrency setup
        }.ToString();

        var clusterMode = Environment.GetEnvironmentVariable("PHONIX_CLUSTER_MODE")?.Trim().ToLowerInvariant();
        _clusterEnabled = clusterEnabledOverride ?? clusterMode is "primary" or "standby";

        // WAL is a DURABLE, file-level setting — set once here; it persists in the database header.
        using (var conn = new SqliteConnection(_connString))
        {
            conn.Open();
            conn.Execute("PRAGMA journal_mode=WAL;");
        }

        EnsureSchema();
    }

    // Opens a pooled connection and applies the per-CONNECTION pragmas (these don't persist in the file the
    // way journal_mode does, so they're set on every open — cheap and idempotent).
    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connString);
        conn.Open();
        conn.Execute("PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;");
        return conn;
    }

    // Runs `work` inside a BEGIN IMMEDIATE transaction (deferred:false). The write lock is taken up front, so
    // concurrent writers serialize cleanly instead of racing or deadlocking on lock upgrade. On any exception
    // the `using` disposes the transaction → automatic ROLLBACK, so a half-applied money operation is
    // impossible. This is the per-operation replacement for the old global `_gate`.
    private T WriteTx<T>(Func<SqliteConnection, SqliteTransaction, T> work)
    {
        using var conn = OpenConnection();
        using var tx = conn.BeginTransaction(deferred: false);
        var result = work(conn, tx);
        tx.Commit();
        return result;
    }

    // Like WriteTx, but with foreign-key enforcement OFF for the unit of work. The bulk restore paths delete
    // parent rows (e.g. Users) while child rows that reference them (Transactions) are still present — or are
    // restored from a different section entirely — so the nominal FKs would block a perfectly valid import.
    // With the hybrid model the relationships are reconstructed from the JSON payloads, not the columns, so
    // dropping enforcement during the swap is safe. PRAGMA foreign_keys can't be toggled inside a transaction,
    // hence it's set on the bare connection before BEGIN and restored before the (pooled) handle is returned.
    private T WriteTxNoFk<T>(Func<SqliteConnection, SqliteTransaction, T> work)
    {
        using var conn = new SqliteConnection(_connString);
        conn.Open();
        conn.Execute("PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000; PRAGMA foreign_keys=OFF;");
        T result;
        using (var tx = conn.BeginTransaction(deferred: false))
        {
            result = work(conn, tx);
            tx.Commit();
        }
        conn.Execute("PRAGMA foreign_keys=ON;"); // restore the default for the pooled connection
        return result;
    }

    // Resolves the database file when the caller gives no explicit path. PHONIX_DB_FILE wins; otherwise the db
    // is co-located with the legacy JSON store (PHONIX_DATA_FILE's folder, as store.db) so a deployment that
    // only configures the old data path — and the test harness, which isolates every run via PHONIX_DATA_FILE —
    // both get a matching, isolated SQLite file. Falls back to the shared persistent location.
    private static string ResolveDbPath()
    {
        var explicitDb = Environment.GetEnvironmentVariable("PHONIX_DB_FILE");
        if (!string.IsNullOrWhiteSpace(explicitDb)) return explicitDb;
        var dataFile = Environment.GetEnvironmentVariable("PHONIX_DATA_FILE");
        if (!string.IsNullOrWhiteSpace(dataFile))
            return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dataFile))!, "store.db");
        return PersistentPaths.Combine("store.db"); // co-locate with the other durable state (survives redeploys)
    }

    // True when the store has no users — the signal the startup bootstrap uses to decide whether to import the
    // legacy JSON snapshot (existing store.json, or the built-in seed) on first run.
    public bool IsEmpty()
    {
        using var conn = OpenConnection();
        return conn.ExecuteScalar<long>("SELECT COUNT(1) FROM Users") == 0;
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    private static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Json);

    private static string Today()
    {
        var pc = new PersianCalendar();
        var now = DateTime.Now;
        var s = $"{pc.GetYear(now):0000}/{pc.GetMonth(now):00}/{pc.GetDayOfMonth(now):00}";
        return new string(s.Select(ch => char.IsDigit(ch) ? (char)('۰' + (ch - '0')) : ch).ToArray());
    }

    // ── Schema ───────────────────────────────────────────────────────────────────────────────────────
    // Only the tables exercised by this slice are shown. Every other domain table (Products, Orders,
    // Categories, Cards, Kyc, Tickets, Notifications, …) follows the IDENTICAL hybrid shape: an INTEGER PK,
    // the few columns that are filtered/joined/sorted, an index per such column, and a DataJson payload.
    private void EnsureSchema()
    {
        using var conn = OpenConnection();
        conn.Execute(@"
CREATE TABLE IF NOT EXISTS Users (
    Id                INTEGER PRIMARY KEY AUTOINCREMENT,
    Username          TEXT COLLATE NOCASE,
    Email             TEXT COLLATE NOCASE,
    Phone             TEXT,
    Role              INTEGER NOT NULL DEFAULT 0,
    Blocked           INTEGER NOT NULL DEFAULT 0,
    ReferredBy        INTEGER NULL,
    VerificationLevel INTEGER NOT NULL DEFAULT 0,
    DataJson          TEXT    NOT NULL
);
-- username is a unique login handle (case-insensitive); empty values are not constrained.
CREATE UNIQUE INDEX IF NOT EXISTS UX_Users_Username ON Users(Username)
    WHERE Username IS NOT NULL AND Username <> '';
CREATE INDEX IF NOT EXISTS IX_Users_Email      ON Users(Email);
CREATE INDEX IF NOT EXISTS IX_Users_ReferredBy ON Users(ReferredBy);

CREATE TABLE IF NOT EXISTS Transactions (
    Id       INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId   INTEGER NOT NULL,
    Status   INTEGER NOT NULL,
    Date     TEXT,
    DataJson TEXT    NOT NULL,
    FOREIGN KEY (UserId) REFERENCES Users(Id)
);
CREATE INDEX IF NOT EXISTS IX_Tx_UserId ON Transactions(UserId);
CREATE INDEX IF NOT EXISTS IX_Tx_Status ON Transactions(Status);

CREATE TABLE IF NOT EXISTS Products (
    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
    CategoryId INTEGER NOT NULL DEFAULT 0,
    IsActive   INTEGER NOT NULL DEFAULT 1,
    Stock      INTEGER NOT NULL DEFAULT 0,
    DataJson   TEXT    NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_Products_CategoryId ON Products(CategoryId);

CREATE TABLE IF NOT EXISTS StockItems (
    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
    ProductId INTEGER NOT NULL,
    Status    INTEGER NOT NULL DEFAULT 0,
    DataJson  TEXT    NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_Stock_ProductId ON StockItems(ProductId);
CREATE INDEX IF NOT EXISTS IX_Stock_Status    ON StockItems(Status);

CREATE TABLE IF NOT EXISTS StockAccounts (
    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
    ProductId INTEGER NOT NULL,
    DataJson  TEXT    NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_StockAccounts_ProductId ON StockAccounts(ProductId);

CREATE TABLE IF NOT EXISTS Orders (
    Id       INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId   INTEGER NOT NULL,
    Status   INTEGER NOT NULL,
    Code     TEXT,
    Date     TEXT,
    DataJson TEXT    NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_Orders_UserId ON Orders(UserId);
CREATE INDEX IF NOT EXISTS IX_Orders_Status ON Orders(Status);

CREATE TABLE IF NOT EXISTS DiscountCodes (
    Id       INTEGER PRIMARY KEY AUTOINCREMENT,
    Code     TEXT COLLATE NOCASE,
    DataJson TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_Discounts_Code ON DiscountCodes(Code);

CREATE TABLE IF NOT EXISTS PaymentMethods (
    Id       INTEGER PRIMARY KEY AUTOINCREMENT,
    DataJson TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Cards (
    Id       INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId   INTEGER NOT NULL,
    Status   INTEGER NOT NULL,
    DataJson TEXT    NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_Cards_UserId ON Cards(UserId);

CREATE TABLE IF NOT EXISTS ReferralEarnings (
    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
    ReferrerId INTEGER NOT NULL,
    DataJson   TEXT    NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_Referrals_ReferrerId ON ReferralEarnings(ReferrerId);

CREATE TABLE IF NOT EXISTS Notifications (
    Id       INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId   INTEGER NULL,        -- NULL = public broadcast to all users
    DataJson TEXT    NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_Notifications_UserId ON Notifications(UserId);

-- Low-traffic admin/content domains: simple id-keyed JSON rows; filtering/ordering done in-memory (small sets).
CREATE TABLE IF NOT EXISTS Categories     (Id INTEGER PRIMARY KEY AUTOINCREMENT, DataJson TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS Plans          (Id INTEGER PRIMARY KEY AUTOINCREMENT, DataJson TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS HeroSlides     (Id INTEGER PRIMARY KEY AUTOINCREMENT, DataJson TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS HomeCategories (Id INTEGER PRIMARY KEY AUTOINCREMENT, DataJson TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS Showcase       (Id INTEGER PRIMARY KEY AUTOINCREMENT, DataJson TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS BlogPosts      (Id INTEGER PRIMARY KEY AUTOINCREMENT, DataJson TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS Comments       (Id INTEGER PRIMARY KEY AUTOINCREMENT, DataJson TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS Kyc            (Id INTEGER PRIMARY KEY AUTOINCREMENT, DataJson TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS Tickets        (Id INTEGER PRIMARY KEY AUTOINCREMENT, DataJson TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS Conversations  (Id INTEGER PRIMARY KEY AUTOINCREMENT, DataJson TEXT NOT NULL);

-- Named monotonic counters that aren't a table PK (currently just the global chat-message id).
CREATE TABLE IF NOT EXISTS Counters (Name TEXT PRIMARY KEY, Value INTEGER NOT NULL);

-- App-wide singletons (PricingSettings, PaymentSettings, SiteContent, …) keyed by name.
CREATE TABLE IF NOT EXISTS Singletons (
    Key      TEXT PRIMARY KEY,
    DataJson TEXT NOT NULL
);

-- Cluster (HA) support — see SqliteDataStore.Cluster.cs. Every local write appends one row here; the peer
-- pulls whatever has Id greater than its own last-applied cursor. SyncRowVersion is the last-writer-wins
-- guard for the rare case both nodes edit the same existing row during a network partition. Neither table
-- is part of any backup/restore snapshot (see SqliteDataStore.Backup.cs) — they are sync bookkeeping, not
-- business data, and a restore always forces a clean re-sync instead of reconciling stale cursors.
CREATE TABLE IF NOT EXISTS SyncOutbox (
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    EntityTable  TEXT NOT NULL,
    EntityId     INTEGER NOT NULL,
    Op           TEXT NOT NULL,
    DataJson     TEXT NULL,
    CreatedAtUtc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_SyncOutbox_Table ON SyncOutbox(EntityTable, Id);

CREATE TABLE IF NOT EXISTS SyncRowVersion (
    EntityTable  TEXT NOT NULL,
    EntityId     INTEGER NOT NULL,
    LastWriteUtc TEXT NOT NULL,
    PRIMARY KEY (EntityTable, EntityId)
);

-- Dead-letter queue for cluster sync: a remote outbox entry that threw while being applied is parked here
-- (keyed by its origin OutboxId) INSTEAD of blocking the cursor forever. The sync loop retries these on a
-- back-off up to a cap; a permanently poisonous event is left visible with its error for an operator to see.
CREATE TABLE IF NOT EXISTS SyncDeadLetter (
    OutboxId      INTEGER PRIMARY KEY,
    EntityTable   TEXT NOT NULL,
    EntityId      INTEGER NOT NULL,
    Op            TEXT NOT NULL,
    DataJson      TEXT NULL,
    CreatedAtUtc  TEXT NOT NULL,
    RetryCount    INTEGER NOT NULL DEFAULT 0,
    LastError     TEXT NULL,
    FirstFailedUtc TEXT NOT NULL,
    LastAttemptUtc TEXT NOT NULL
);
");
    }

    // ── Generic helpers for the simple id-keyed JSON domains ────────────────────────────────────────────
    private List<T> AllJson<T>(string table)
    {
        using var conn = OpenConnection();
        return conn.Query<string>($"SELECT DataJson FROM {table}").Select(j => Deserialize<T>(j)!).ToList();
    }

    private T? OneJson<T>(string table, int id) where T : class
    {
        using var conn = OpenConnection();
        var j = conn.QueryFirstOrDefault<string>($"SELECT DataJson FROM {table} WHERE Id = @id", new { id });
        return j is null ? null : Deserialize<T>(j);
    }

    private bool DeleteRow(string table, int id)
    {
        return WriteTx((conn, tx) =>
        {
            var deleted = conn.Execute($"DELETE FROM {table} WHERE Id = @id", new { id }, tx) > 0;
            if (deleted) AppendOutbox(conn, tx, table, id, SyncOp.Delete, null);
            return deleted;
        });
    }

    // INSERT with an auto id, then rewrite DataJson so it carries the assigned id (and any derived field the
    // caller set after we hand the id back via the locator). Returns the new id.
    private int InsertJson<T>(string table, T value, Action<T, int> setId)
    {
        return WriteTx((conn, tx) =>
        {
            var id = (int)conn.ExecuteScalar<long>(
                $"INSERT INTO {table} (DataJson) VALUES (@DataJson); SELECT last_insert_rowid();",
                new { DataJson = Serialize(value) }, tx);
            setId(value, id);
            var json = Serialize(value);
            conn.Execute($"UPDATE {table} SET DataJson = @DataJson WHERE Id = @id", new { DataJson = json, id }, tx);
            AppendOutbox(conn, tx, table, id, SyncOp.Upsert, json);
            return id;
        });
    }

    private bool UpdateJson<T>(string table, int id, T value)
    {
        return WriteTx((conn, tx) =>
        {
            var json = Serialize(value);
            var updated = conn.Execute($"UPDATE {table} SET DataJson = @DataJson WHERE Id = @id",
                new { DataJson = json, id }, tx) > 0;
            if (updated) AppendOutbox(conn, tx, table, id, SyncOp.Upsert, json);
            return updated;
        });
    }

    private static int NextCounter(SqliteConnection conn, SqliteTransaction tx, string name)
    {
        conn.Execute(@"INSERT INTO Counters (Name, Value) VALUES (@name, 1)
ON CONFLICT(Name) DO UPDATE SET Value = Value + 1;", new { name }, tx);
        return conn.ExecuteScalar<int>("SELECT Value FROM Counters WHERE Name = @name", new { name }, tx);
    }

    // ── Singletons (settings blobs) ───────────────────────────────────────────────────────────────────
    private const string PricingKey = "pricing";
    private const string PaymentKey = "payment";

    private T GetSingleton<T>(string key) where T : new()
    {
        using var conn = OpenConnection();
        var json = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Singletons WHERE Key = @key", new { key });
        return json is null ? new T() : (Deserialize<T>(json) ?? new T());
    }

    private static T ReadSingleton<T>(SqliteConnection conn, SqliteTransaction tx, string key) where T : new()
    {
        var json = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Singletons WHERE Key = @key", new { key }, tx);
        return json is null ? new T() : (Deserialize<T>(json) ?? new T());
    }

    private static void WriteSingleton<T>(SqliteConnection conn, SqliteTransaction? tx, string key, T value) =>
        conn.Execute(@"
INSERT INTO Singletons (Key, DataJson) VALUES (@key, @json)
ON CONFLICT(Key) DO UPDATE SET DataJson = excluded.DataJson;",
            new { key, json = Serialize(value) }, tx);

    public PricingSettings GetSettings() => GetSingleton<PricingSettings>(PricingKey);
    public PaymentSettings GetPaymentSettings() => GetSingleton<PaymentSettings>(PaymentKey);

    public void UpdateSettings(PricingSettings settings)
    {
        using var conn = OpenConnection();
        WriteSingleton(conn, null, PricingKey, settings);
    }

    public void UpdatePaymentSettings(PaymentSettings settings)
    {
        using var conn = OpenConnection();
        WriteSingleton(conn, null, PaymentKey, settings);
    }
}

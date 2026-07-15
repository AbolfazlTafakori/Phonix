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
public sealed class SqliteDataStore : IDataStore
{
    private readonly string _connString;

    // Compact per-row JSON, enum-as-string to match the snapshot/backup format (DeserializeSnapshot uses the
    // same enum handling), so a DataJson value is byte-for-byte the shape the rest of the system expects.
    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    // dbPath is optional so tests can point at a unique temp file; in production it resolves from
    // PHONIX_DB_FILE or co-locates with the other durable state. (All-optional ctor stays DI-constructable.)
    public SqliteDataStore(string? dbPath = null)
    {
        dbPath ??= ResolveDbPath();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);

        _connString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Pooling = true,                     // reuse handles across requests
            Cache = SqliteCacheMode.Default,    // private cache + WAL is the recommended high-concurrency setup
        }.ToString();

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
        using var conn = OpenConnection();
        return conn.Execute($"DELETE FROM {table} WHERE Id = @id", new { id }) > 0;
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
            conn.Execute($"UPDATE {table} SET DataJson = @DataJson WHERE Id = @id", new { DataJson = Serialize(value), id }, tx);
            return id;
        });
    }

    private bool UpdateJson<T>(string table, int id, T value)
    {
        using var conn = OpenConnection();
        return conn.Execute($"UPDATE {table} SET DataJson = @DataJson WHERE Id = @id",
            new { DataJson = Serialize(value), id }) > 0;
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

    // ── Users (representative CRUD) ────────────────────────────────────────────────────────────────────

    // Writes the column projection + the full object payload in one statement. INSERT-or-UPDATE on the PK so
    // the same helper serves both new rows and edits.
    private static void UpsertUser(SqliteConnection conn, SqliteTransaction? tx, AppUser u) =>
        conn.Execute(@"
INSERT INTO Users (Id, Username, Email, Phone, Role, Blocked, ReferredBy, VerificationLevel, DataJson)
VALUES (@Id, @Username, @Email, @Phone, @Role, @Blocked, @ReferredBy, @VerificationLevel, @DataJson)
ON CONFLICT(Id) DO UPDATE SET
    Username=excluded.Username, Email=excluded.Email, Phone=excluded.Phone, Role=excluded.Role,
    Blocked=excluded.Blocked, ReferredBy=excluded.ReferredBy,
    VerificationLevel=excluded.VerificationLevel, DataJson=excluded.DataJson;",
            new
            {
                u.Id,
                u.Username,
                u.Email,
                u.Phone,
                Role = (int)u.Role,
                Blocked = u.Blocked ? 1 : 0,
                u.ReferredBy,
                u.VerificationLevel,
                DataJson = Serialize(u),
            }, tx);

    // Hot path (called on every authenticated request): O(log n) primary-key lookup, then deserialize.
    public AppUser? GetUser(int id)
    {
        using var conn = OpenConnection();
        var json = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Users WHERE Id = @id", new { id });
        return json is null ? null : Deserialize<AppUser>(json);
    }

    public AppUser? GetUserByUsername(string username)
    {
        using var conn = OpenConnection();
        var json = conn.QueryFirstOrDefault<string>(
            "SELECT DataJson FROM Users WHERE Username = @username COLLATE NOCASE", new { username });
        return json is null ? null : Deserialize<AppUser>(json);
    }

    public bool UsernameExists(string username)
    {
        using var conn = OpenConnection();
        return conn.ExecuteScalar<long>(
            "SELECT COUNT(1) FROM Users WHERE Username = @username COLLATE NOCASE", new { username }) > 0;
    }

    public bool EmailExists(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        using var conn = OpenConnection();
        return conn.ExecuteScalar<long>(
            "SELECT COUNT(1) FROM Users WHERE Email = @email COLLATE NOCASE", new { email }) > 0;
    }

    public AppUser? FindByLogin(string identifier)
    {
        using var conn = OpenConnection();
        // Username/Email are indexed; Phone is a cheap fallback scan (login only, and rate-limited).
        var json = conn.QueryFirstOrDefault<string>(@"
SELECT DataJson FROM Users
WHERE Username = @id COLLATE NOCASE OR Email = @id COLLATE NOCASE OR Phone = @id
LIMIT 1;", new { id = identifier });
        return json is null ? null : Deserialize<AppUser>(json);
    }

    public IReadOnlyList<AppUser> GetUsers(string? search = null, UserRole? role = null, bool? blocked = null)
    {
        using var conn = OpenConnection();
        // role/blocked are indexed-ish column filters pushed into SQL; the free-text search matches the JSON
        // store's semantics (Name/Email/Phone/Code) and is applied after materializing the filtered set.
        var sql = "SELECT DataJson FROM Users WHERE 1=1";
        if (role is not null) sql += " AND Role = @role";
        if (blocked is not null) sql += " AND Blocked = @blocked";
        sql += " ORDER BY Id DESC;";

        var rows = conn.Query<string>(sql, new { role = role is null ? 0 : (int)role.Value, blocked = blocked == true ? 1 : 0 });
        var users = rows.Select(j => Deserialize<AppUser>(j)!).ToList();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            users = users.Where(u =>
                u.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                u.Email.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                u.Phone.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                u.Code.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        return users;
    }

    public AppUser RegisterUser(AppUser user)
    {
        user.Role = UserRole.Customer;
        user.SecurityStamp = StoreData.NewStamp();
        user.EmailVerified = false; // must confirm their email before they can order
        if (string.IsNullOrWhiteSpace(user.JoinedAt)) user.JoinedAt = Today();

        return WriteTx((conn, tx) =>
        {
            // Let SQLite assign the id (AUTOINCREMENT), then stamp the derived Code and rewrite the payload so
            // DataJson carries the final id+code. Both writes are in one transaction → atomic.
            var id = conn.ExecuteScalar<long>(@"
INSERT INTO Users (Username, Email, Phone, Role, Blocked, ReferredBy, VerificationLevel, DataJson)
VALUES (@Username, @Email, @Phone, @Role, @Blocked, @ReferredBy, @VerificationLevel, @DataJson);
SELECT last_insert_rowid();",
                new
                {
                    user.Username, user.Email, user.Phone, Role = (int)user.Role,
                    Blocked = user.Blocked ? 1 : 0, user.ReferredBy, user.VerificationLevel,
                    DataJson = Serialize(user),
                }, tx);

            user.Id = (int)id;
            user.Code = $"U-{1000 + user.Id}";
            conn.Execute("UPDATE Users SET DataJson = @DataJson WHERE Id = @Id",
                new { DataJson = Serialize(user), user.Id }, tx);
            return user;
        });
    }

    // Read-modify-write under IMMEDIATE so two concurrent edits to the same user can't clobber each other.
    public bool UpdateUser(int id, Action<AppUser> mutate) =>
        WriteTx((conn, tx) =>
        {
            var json = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Users WHERE Id = @id", new { id }, tx);
            if (json is null) return false;
            var user = Deserialize<AppUser>(json)!;
            mutate(user);
            UpsertUser(conn, tx, user);
            return true;
        });

    public bool DeleteUser(int id)
    {
        using var conn = OpenConnection();
        return conn.Execute("DELETE FROM Users WHERE Id = @id", new { id }) > 0;
    }

    // ── Money path (the ACID demonstration that replaces _gate) ────────────────────────────────────────

    // Files a withdrawal and HOLDS the funds immediately: balance check + debit + the pending transaction all
    // commit together, or none do. Two simultaneous withdrawals can't both pass the balance check and
    // overdraw, because IMMEDIATE serializes the writers — the second blocks at BEGIN until the first commits,
    // then re-reads the already-reduced balance. Same integrity the old `_gate` gave, without a global lock.
    public WithdrawalResult RequestWithdrawal(int userId, long amount, string destination) =>
        WriteTx<WithdrawalResult>((conn, tx) =>
        {
            var json = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Users WHERE Id = @userId", new { userId }, tx);
            if (json is null) return new WithdrawalResult(null, "کاربر یافت نشد.");
            var user = Deserialize<AppUser>(json)!;

            if (amount <= 0) return new WithdrawalResult(null, "مبلغ نامعتبر است.");
            if (user.Wallet < amount) return new WithdrawalResult(null, "موجودی کیف پول برای این برداشت کافی نیست.");

            user.Wallet -= amount;            // debit
            UpsertUser(conn, tx, user);       // persist the held balance

            var name = string.IsNullOrWhiteSpace(user.Name) ? user.Username : user.Name;
            var t = new Transaction
            {
                UserId = userId,
                UserName = name,
                Type = TxTypes.Withdraw,
                Amount = -amount,
                Status = TxStatus.Pending,
                Method = destination,
                Date = Today(),
            };
            var txId = conn.ExecuteScalar<long>(@"
INSERT INTO Transactions (UserId, Status, Date, DataJson) VALUES (@UserId, @Status, @Date, @DataJson);
SELECT last_insert_rowid();",
                new { t.UserId, Status = (int)t.Status, t.Date, DataJson = Serialize(t) }, tx);

            t.Id = (int)txId;
            conn.Execute("UPDATE Transactions SET DataJson = @DataJson WHERE Id = @Id",
                new { DataJson = Serialize(t), t.Id }, tx);

            return new WithdrawalResult(t, null); // WriteTx COMMITs here → debit + pending tx persist atomically
        });

    // Mirrors StoreData.AddTransaction: assigns id (autoincrement), a TX-code, and the date, then rewrites
    // the payload so DataJson carries them. Caller supplies the open connection + transaction.
    private static Transaction InsertTransaction(SqliteConnection conn, SqliteTransaction tx, Transaction t)
    {
        if (string.IsNullOrWhiteSpace(t.Date)) t.Date = Today();
        var id = conn.ExecuteScalar<long>(@"
INSERT INTO Transactions (UserId, Status, Date, DataJson) VALUES (@UserId, @Status, @Date, @DataJson);
SELECT last_insert_rowid();",
            new { t.UserId, Status = (int)t.Status, t.Date, DataJson = Serialize(t) }, tx);
        t.Id = (int)id;
        if (string.IsNullOrWhiteSpace(t.Code)) t.Code = $"TX-{9900 + t.Id}";
        conn.Execute("UPDATE Transactions SET DataJson = @DataJson WHERE Id = @Id",
            new { DataJson = Serialize(t), t.Id }, tx);
        return t;
    }

    // ── Products ───────────────────────────────────────────────────────────────────────────────────────

    private static void NumberPlans(List<ProductPlan> plans)
    {
        for (var i = 0; i < plans.Count; i++) plans[i].Id = i + 1;
    }

    private static void UpsertProduct(SqliteConnection conn, SqliteTransaction? tx, Product p) =>
        conn.Execute(@"
INSERT INTO Products (Id, CategoryId, IsActive, Stock, DataJson)
VALUES (@Id, @CategoryId, @IsActive, @Stock, @DataJson)
ON CONFLICT(Id) DO UPDATE SET
    CategoryId=excluded.CategoryId, IsActive=excluded.IsActive, Stock=excluded.Stock, DataJson=excluded.DataJson;",
            new { p.Id, p.CategoryId, IsActive = p.IsActive ? 1 : 0, p.Stock, DataJson = Serialize(p) }, tx);

    public Product? GetProduct(int id)
    {
        using var conn = OpenConnection();
        var json = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Products WHERE Id = @id", new { id });
        return json is null ? null : Deserialize<Product>(json);
    }

    public IReadOnlyList<Product> GetProducts(int? categoryId = null, string? search = null)
    {
        using var conn = OpenConnection();
        var sql = "SELECT DataJson FROM Products WHERE 1=1";
        if (categoryId is not null) sql += " AND CategoryId = @categoryId";
        sql += " ORDER BY Id;";
        var products = conn.Query<string>(sql, new { categoryId }).Select(j => Deserialize<Product>(j)!).ToList();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            products = products.Where(p =>
                p.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                p.Sku.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        return products;
    }

    public Product AddProduct(Product product) =>
        WriteTx((conn, tx) =>
        {
            NumberPlans(product.Plans);
            var id = conn.ExecuteScalar<long>(@"
INSERT INTO Products (CategoryId, IsActive, Stock, DataJson) VALUES (@CategoryId, @IsActive, @Stock, @DataJson);
SELECT last_insert_rowid();",
                new { product.CategoryId, IsActive = product.IsActive ? 1 : 0, product.Stock, DataJson = Serialize(product) }, tx);
            product.Id = (int)id;
            conn.Execute("UPDATE Products SET DataJson = @DataJson WHERE Id = @Id",
                new { DataJson = Serialize(product), product.Id }, tx);
            return product;
        });

    public bool UpdateProduct(Product product) =>
        WriteTx((conn, tx) =>
        {
            var exists = conn.ExecuteScalar<long>("SELECT COUNT(1) FROM Products WHERE Id = @Id", new { product.Id }, tx) > 0;
            if (!exists) return false;
            NumberPlans(product.Plans);
            UpsertProduct(conn, tx, product);
            return true;
        });

    public bool DeleteProduct(int id)
    {
        using var conn = OpenConnection();
        return conn.Execute("DELETE FROM Products WHERE Id = @id", new { id }) > 0;
    }

    // ── Orders (reads) ──────────────────────────────────────────────────────────────────────────────────

    public Order? GetOrder(int id)
    {
        using var conn = OpenConnection();
        var json = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Orders WHERE Id = @id", new { id });
        return json is null ? null : Deserialize<Order>(json);
    }

    public IReadOnlyList<Order> GetOrders(OrderStatus? status = null)
    {
        using var conn = OpenConnection();
        var sql = "SELECT DataJson FROM Orders";
        if (status is not null) sql += " WHERE Status = @status";
        sql += " ORDER BY Id DESC;";
        return conn.Query<string>(sql, new { status = status is null ? 0 : (int)status.Value })
            .Select(j => Deserialize<Order>(j)!).ToList();
    }

    public IReadOnlyList<Order> GetUserOrders(int userId)
    {
        using var conn = OpenConnection();
        return conn.Query<string>("SELECT DataJson FROM Orders WHERE UserId = @userId ORDER BY Id DESC", new { userId })
            .Select(j => Deserialize<Order>(j)!).ToList();
    }

    // ── PlaceOrder: the fully-atomic high-traffic write ─────────────────────────────────────────────────
    // EVERYTHING — re-reading live stock + wallet, the oversell guard, the wallet debit, the stock
    // decrement, discount consumption, the order row, and the payment transactions — happens inside ONE
    // BEGIN IMMEDIATE transaction. Because the write lock is held for the whole unit of work, two concurrent
    // buyers can NEVER both take the last unit or both spend the same wallet balance: the second is serialized
    // behind the first and re-reads the post-commit state. Any failure rolls the whole thing back — no
    // half-charged wallet, no phantom stock decrement. This is the per-operation replacement for `_gate`.
    public PlaceOrderResult PlaceOrder(AppUser user, IEnumerable<(int productId, int quantity, int? planId)> items,
        string paymentMethod, bool fromWallet, string? discountCode = null, int? paymentMethodId = null,
        RemainderPayment? payment = null, bool customerCheckout = false, IReadOnlyList<OrderLineInfo>? lineInfo = null)
    {
        var itemList = items.ToList();
        return WriteTx<PlaceOrderResult>((conn, tx) =>
        {
            // Re-read the buyer INSIDE the transaction so wallet/level reflect the latest committed state,
            // never the (possibly stale) object the caller passed in.
            var liveUserJson = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Users WHERE Id = @id", new { id = user.Id }, tx);
            if (liveUserJson is null) return new PlaceOrderResult(null, "کاربر یافت نشد.");
            var buyer = Deserialize<AppUser>(liveUserJson)!;

            var settings = ReadSingleton<PricingSettings>(conn, tx, PricingKey);
            var paymentSettings = ReadSingleton<PaymentSettings>(conn, tx, PaymentKey);

            // Load every referenced product once (live row, under the write lock) for validation + mutation.
            var products = new Dictionary<int, Product>();
            foreach (var pid in itemList.Where(i => i.quantity > 0).Select(i => i.productId).Distinct())
            {
                var pj = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Products WHERE Id = @pid", new { pid }, tx);
                if (pj is not null) products[pid] = Deserialize<Product>(pj)!;
            }

            var lines = new List<OrderItem>();
            var units = new List<OrderUnit>();
            for (var idx = 0; idx < itemList.Count; idx++)
            {
                var (productId, quantity, planId) = itemList[idx];
                if (quantity <= 0) continue;
                if (!products.TryGetValue(productId, out var p)) continue;

                ProductPlan? plan = null;
                if (planId is int pid)
                {
                    plan = p.Plans.FirstOrDefault(x => x.Id == pid && x.IsActive);
                    if (plan is null) continue;
                }

                var qty = Math.Min(quantity, 100);
                var planLabel = plan is null ? null : $"{plan.Type} · {plan.Months} ماهه";
                lines.Add(new OrderItem
                {
                    ProductId = p.Id, Name = p.Name, Image = p.Image, Plan = planLabel,
                    PlanMonths = plan?.Months, UnitPrice = plan?.FinalPrice ?? p.FinalPrice, Quantity = qty,
                });

                var lineUnits = lineInfo is not null && idx < lineInfo.Count ? lineInfo[idx]?.Units : null;
                for (var u = 0; u < qty; u++)
                {
                    var ui = lineUnits is not null && u < lineUnits.Count ? lineUnits[u] : null;
                    units.Add(new OrderUnit
                    {
                        Id = units.Count + 1, ProductId = p.Id, Name = p.Name, Image = p.Image, Plan = planLabel,
                        UnitIndex = u + 1, CustomerInputs = ui?.Inputs ?? new(), CustomerNote = ui?.Note,
                    });
                }
            }

            if (lines.Count == 0) return new PlaceOrderResult(null, "محصولی برای ثبت یافت نشد.");

            // identity-level gate (products default to level 1; a level-0 user can never purchase).
            foreach (var group in lines.GroupBy(l => l.ProductId))
            {
                var p = products[group.Key];
                if (buyer.VerificationLevel < p.RequiredLevel)
                    return new PlaceOrderResult(null, $"سطح احراز هویت شما برای «{p.Name}» کافی نیست.");
            }

            // oversell guard: check-and-decrement is inside the IMMEDIATE tx, so two buyers can't both win.
            foreach (var group in lines.GroupBy(l => l.ProductId))
            {
                var p = products[group.Key];
                var needed = group.Sum(l => l.Quantity);
                if (p.Stock < needed) return new PlaceOrderResult(null, $"موجودی «{p.Name}» کافی نیست.");
            }

            var subtotal = lines.Sum(l => l.LineTotal);
            var discount = ResolveDiscountTx(conn, tx, discountCode, subtotal);
            if (discount.Error is not null) return new PlaceOrderResult(null, discount.Error);
            var goodsTotal = subtotal - discount.Amount;

            var vat = settings.VatPercent > 0
                ? (long)Math.Round(goodsTotal * (double)settings.VatPercent / 100.0, MidpointRounding.AwayFromZero)
                : 0;
            var payable = goodsTotal + vat;

            var walletUsed = fromWallet ? Math.Min(buyer.Wallet, payable) : 0;
            var remainder = payable - walletUsed;

            // customer card-to-card for the remainder: validated BEFORE any mutation (nothing is written if it fails).
            BankCard? sourceCard = null;
            if (customerCheckout && remainder > 0)
            {
                if (paymentMethodId is null)
                    return new PlaceOrderResult(null, "برای پرداخت مبلغ باقیمانده، یک روش پرداخت انتخاب کنید.");
                if (payment?.CardId is not int cardId)
                    return new PlaceOrderResult(null, "یک کارت بانکی ثبت‌شده را انتخاب کنید.");
                var cardJson = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Cards WHERE Id = @cardId", new { cardId }, tx);
                sourceCard = cardJson is null ? null : Deserialize<BankCard>(cardJson);
                if (sourceCard is null || sourceCard.UserId != buyer.Id || sourceCard.Status != BankCardStatus.Approved)
                    return new PlaceOrderResult(null, "کارت انتخاب‌شده معتبر یا تأییدشده نیست.");
                if (string.IsNullOrWhiteSpace(payment.TrackingNumber))
                    return new PlaceOrderResult(null, "شماره پیگیری واریز را وارد کنید.");
                if (string.IsNullOrWhiteSpace(payment.PaymentDate))
                    return new PlaceOrderResult(null, "تاریخ پرداخت را وارد کنید.");
                if (paymentSettings.RequireReceipt && string.IsNullOrWhiteSpace(payment.ReceiptUrl))
                    return new PlaceOrderResult(null, "رسید پرداخت مبلغ باقیمانده را بارگذاری کنید.");
            }

            // gateway fee applies only to the amount paid through the method (its own FeePercent, else global).
            // destMethod is also the destination the buyer paid TO — captured onto the receipt transaction below.
            long fee = 0;
            PaymentMethod? destMethod = null;
            if (paymentMethodId is int methodId && remainder > 0)
            {
                var pmJson = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM PaymentMethods WHERE Id = @methodId", new { methodId }, tx);
                var pm = pmJson is null ? null : Deserialize<PaymentMethod>(pmJson);
                destMethod = pm;
                if (pm is not null)
                {
                    var feePercent = pm.FeePercent > 0 ? pm.FeePercent : settings.GatewayFeePercent;
                    if (feePercent > 0)
                        fee = (long)Math.Round(remainder * (double)feePercent / 100.0, MidpointRounding.AwayFromZero);
                }
            }

            var name = string.IsNullOrWhiteSpace(buyer.Name) ? buyer.Username : buyer.Name;
            var order = new Order
            {
                UserId = buyer.Id, UserName = name, PaymentMethod = paymentMethod, Items = lines, Units = units,
                Subtotal = subtotal, DiscountCode = discount.Code?.Code, DiscountAmount = discount.Amount,
                WalletPaid = walletUsed, VatAmount = vat, FeeAmount = fee, Total = goodsTotal + vat + fee,
                ReceiptUrl = remainder > 0 && !string.IsNullOrWhiteSpace(payment?.ReceiptUrl) ? payment.ReceiptUrl.Trim() : null,
                Date = Today(),
                Status = remainder == 0 ? OrderStatus.Preparing : OrderStatus.PendingApproval,
            };

            // ── mutations (all committed together) ──
            if (discount.Code is not null) ConsumeDiscountTx(conn, tx, discount.Code);

            foreach (var line in lines)
            {
                var p = products[line.ProductId];
                p.Stock = Math.Max(0, p.Stock - line.Quantity);
            }
            foreach (var p in products.Values) UpsertProduct(conn, tx, p); // persist decremented stock

            if (walletUsed > 0)
            {
                buyer.Wallet -= walletUsed;
                InsertTransaction(conn, tx, new Transaction
                {
                    UserId = buyer.Id, UserName = name, Type = TxTypes.Purchase, Amount = -walletUsed,
                    Status = TxStatus.Approved, Method = "کیف پول", ApprovedVia = "wallet", Date = Today(),
                });
            }

            // insert the order, then stamp the derived Code and rewrite the payload (one transaction → atomic).
            var orderId = conn.ExecuteScalar<long>(@"
INSERT INTO Orders (UserId, Status, Code, Date, DataJson) VALUES (@UserId, @Status, @Code, @Date, @DataJson);
SELECT last_insert_rowid();",
                new { order.UserId, Status = (int)order.Status, order.Code, order.Date, DataJson = Serialize(order) }, tx);
            order.Id = (int)orderId;
            order.Code = $"PX-{100000 + order.Id}";
            conn.Execute("UPDATE Orders SET Code = @Code, DataJson = @DataJson WHERE Id = @Id",
                new { order.Code, DataJson = Serialize(order), order.Id }, tx);

            if (customerCheckout && remainder > 0 && sourceCard is not null)
            {
                InsertTransaction(conn, tx, new Transaction
                {
                    UserId = buyer.Id, UserName = name, Type = TxTypes.OrderPayment,
                    Amount = -(order.Total - order.WalletPaid), Status = TxStatus.Pending, Method = paymentMethod,
                    ReceiptUrl = string.IsNullOrWhiteSpace(payment!.ReceiptUrl) ? null : payment.ReceiptUrl.Trim(),
                    SourceCard = sourceCard.CardNumber, SourceHolder = sourceCard.HolderName,
                    DestinationCard = destMethod?.Value, DestinationHolder = destMethod?.Holder,
                    TrackingNumber = payment.TrackingNumber!.Trim(),
                    PaymentDate = payment.PaymentDate!.Trim(),
                    Description = string.IsNullOrWhiteSpace(payment.Description) ? null : payment.Description.Trim(),
                    OrderCode = order.Code, Date = Today(),
                });
            }

            // recompute and persist the buyer's order stats from the live Orders table (mirrors RefreshUserOrderStats).
            buyer.Orders = conn.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM Orders WHERE UserId = @id AND Status <> @cancelled",
                new { id = buyer.Id, cancelled = (int)OrderStatus.Cancelled }, tx);
            buyer.TotalSpent = conn.ExecuteScalar<long?>(
                "SELECT SUM(json_extract(DataJson,'$.Total')) FROM Orders WHERE UserId = @id AND Status = @completed",
                new { id = buyer.Id, completed = (int)OrderStatus.Completed }, tx) ?? 0;
            UpsertUser(conn, tx, buyer);

            return new PlaceOrderResult(order, null);
        });
    }

    // ── Discount helpers (transaction-scoped) ───────────────────────────────────────────────────────────

    private static DiscountResult ResolveDiscountTx(SqliteConnection conn, SqliteTransaction? tx, string? code, long subtotal)
    {
        if (string.IsNullOrWhiteSpace(code)) return new DiscountResult(null, 0, null);
        var json = conn.QueryFirstOrDefault<string>(
            "SELECT DataJson FROM DiscountCodes WHERE Code = @code COLLATE NOCASE LIMIT 1", new { code = code.Trim() }, tx);
        var dc = json is null ? null : Deserialize<DiscountCode>(json);
        if (dc is null || !dc.IsActive) return new DiscountResult(null, 0, "کد تخفیف نامعتبر است.");
        if (dc.ExpiresAt is DateTime exp && DateTime.UtcNow > exp) return new DiscountResult(null, 0, "این کد تخفیف منقضی شده است.");
        if (dc.UsageLimit > 0 && dc.UsedCount >= dc.UsageLimit) return new DiscountResult(null, 0, "ظرفیت این کد تخفیف به پایان رسیده است.");
        if (subtotal < dc.MinOrder) return new DiscountResult(null, 0, "مبلغ سفارش به حد لازم برای این کد نرسیده است.");

        long amount = dc.Type == DiscountType.Percent ? (long)Math.Round(subtotal * dc.Value / 100.0) : dc.Value;
        if (dc.Type == DiscountType.Percent && dc.MaxDiscount > 0) amount = Math.Min(amount, dc.MaxDiscount);
        amount = Math.Clamp(amount, 0, subtotal);
        return new DiscountResult(dc, amount, null);
    }

    private static void ConsumeDiscountTx(SqliteConnection conn, SqliteTransaction tx, DiscountCode dc)
    {
        dc.UsedCount++;
        conn.Execute("UPDATE DiscountCodes SET DataJson = @DataJson WHERE Id = @Id",
            new { DataJson = Serialize(dc), dc.Id }, tx);
    }

    // ── Discount codes (admin CRUD + public resolve) ────────────────────────────────────────────────────
    public IReadOnlyList<DiscountCode> GetDiscountCodes() =>
        AllJson<DiscountCode>("DiscountCodes").OrderByDescending(d => d.Id).ToList();

    public DiscountCode AddDiscountCode(DiscountCode code) =>
        WriteTx((conn, tx) =>
        {
            code.UsedCount = 0;
            var id = (int)conn.ExecuteScalar<long>(
                "INSERT INTO DiscountCodes (Code, DataJson) VALUES (@Code, @DataJson); SELECT last_insert_rowid();",
                new { code.Code, DataJson = Serialize(code) }, tx);
            code.Id = id;
            conn.Execute("UPDATE DiscountCodes SET Code = @Code, DataJson = @d WHERE Id = @id",
                new { code.Code, d = Serialize(code), id }, tx);
            return code;
        });

    // Mirrors StoreData.UpdateDiscountCode: copies the editable fields onto the stored row, preserving UsedCount.
    public bool UpdateDiscountCode(DiscountCode code) =>
        WriteTx((conn, tx) =>
        {
            var ej = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM DiscountCodes WHERE Id = @Id", new { code.Id }, tx);
            if (ej is null) return false;
            var existing = Deserialize<DiscountCode>(ej)!;
            existing.Code = code.Code;
            existing.Type = code.Type;
            existing.Value = code.Value;
            existing.MinOrder = code.MinOrder;
            existing.MaxDiscount = code.MaxDiscount;
            existing.UsageLimit = code.UsageLimit;
            existing.IsActive = code.IsActive;
            existing.ExpiresAt = code.ExpiresAt;
            conn.Execute("UPDATE DiscountCodes SET Code = @Code, DataJson = @d WHERE Id = @id",
                new { existing.Code, d = Serialize(existing), id = existing.Id }, tx);
            return true;
        });

    public bool DeleteDiscountCode(int id) => DeleteRow("DiscountCodes", id);

    // Validates a code against a subtotal WITHOUT consuming it (consumption happens atomically in PlaceOrder).
    // Reuses the transaction-scoped resolver on a plain read connection.
    public DiscountResult ResolveDiscount(string? code, long subtotal)
    {
        if (string.IsNullOrWhiteSpace(code)) return new DiscountResult(null, 0, null);
        using var conn = OpenConnection();
        return ResolveDiscountTx(conn, null, code, subtotal);
    }

    // ── Order status transitions (atomic refunds + referral earnings) ───────────────────────────────────

    private static void UpsertOrder(SqliteConnection conn, SqliteTransaction tx, Order o) =>
        conn.Execute(@"
INSERT INTO Orders (Id, UserId, Status, Code, Date, DataJson)
VALUES (@Id, @UserId, @Status, @Code, @Date, @DataJson)
ON CONFLICT(Id) DO UPDATE SET
    UserId=excluded.UserId, Status=excluded.Status, Code=excluded.Code, Date=excluded.Date, DataJson=excluded.DataJson;",
            new { o.Id, o.UserId, Status = (int)o.Status, o.Code, o.Date, DataJson = Serialize(o) }, tx);

    private static AppUser? LoadUser(SqliteConnection conn, SqliteTransaction tx, int id)
    {
        var j = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Users WHERE Id = @id", new { id }, tx);
        return j is null ? null : Deserialize<AppUser>(j);
    }

    // Recomputes the buyer's derived order stats from the live Orders table (mirrors RefreshUserOrderStats).
    private static void RefreshUserStats(SqliteConnection conn, SqliteTransaction tx, int userId)
    {
        var u = LoadUser(conn, tx, userId);
        if (u is null) return;
        u.Orders = conn.ExecuteScalar<int>(
            "SELECT COUNT(1) FROM Orders WHERE UserId = @id AND Status <> @cancelled",
            new { id = userId, cancelled = (int)OrderStatus.Cancelled }, tx);
        u.TotalSpent = conn.ExecuteScalar<long?>(
            "SELECT SUM(json_extract(DataJson,'$.Total')) FROM Orders WHERE UserId = @id AND Status = @completed",
            new { id = userId, completed = (int)OrderStatus.Completed }, tx) ?? 0;
        UpsertUser(conn, tx, u);
    }

    private static void AppendOrderHistory(Order o, OrderStatus from, OrderStatus to, string? changedBy, string? reason) =>
        o.History.Add(new OrderStatusHistory
        {
            Id = (o.History.Count == 0 ? 0 : o.History.Max(h => h.Id)) + 1,
            OrderId = o.Id,
            ChangedByUsername = string.IsNullOrWhiteSpace(changedBy) ? "سیستم" : changedBy!.Trim(),
            FromStatus = from,
            ToStatus = to,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason!.Trim(),
            ChangedAtUtc = DateTime.UtcNow,
        });

    private static void AddNotificationTx(SqliteConnection conn, SqliteTransaction tx, int? userId, string title, string body, string? link)
    {
        var n = new Notification
        {
            UserId = userId, Title = title, Body = body, Link = link, CreatedAtUtc = DateTime.UtcNow.ToString("o"),
        };
        var id = conn.ExecuteScalar<long>(
            "INSERT INTO Notifications (UserId, DataJson) VALUES (@UserId, @DataJson); SELECT last_insert_rowid();",
            new { UserId = userId, DataJson = Serialize(n) }, tx);
        n.Id = (int)id;
        conn.Execute("UPDATE Notifications SET DataJson = @DataJson WHERE Id = @Id", new { DataJson = Serialize(n), n.Id }, tx);
    }

    // Pays the referrer their commission when a referred buyer's order is completed. Runs inside the caller's
    // transaction so the wallet credit + earning record + transaction row commit atomically with the order.
    private static void CreditReferralTx(SqliteConnection conn, SqliteTransaction tx, Order order, PricingSettings settings)
    {
        var buyer = LoadUser(conn, tx, order.UserId);
        if (buyer?.ReferredBy is not int referrerId) return;
        var referrer = LoadUser(conn, tx, referrerId);
        if (referrer is null) return;

        var percent = settings.ReferralCommissionPercent;
        if (percent <= 0) return;
        var commission = (long)Math.Round(order.Total * (double)percent / 100.0, MidpointRounding.AwayFromZero);
        if (commission <= 0) return;

        referrer.Wallet += commission;
        UpsertUser(conn, tx, referrer);

        conn.Execute("INSERT INTO ReferralEarnings (ReferrerId, DataJson) VALUES (@ReferrerId, @DataJson)",
            new
            {
                ReferrerId = referrerId,
                DataJson = Serialize(new ReferralEarning
                {
                    ReferrerId = referrerId, ReferredName = order.UserName, OrderCode = order.Code,
                    OrderAmount = order.Total, Commission = commission, Date = Today(),
                }),
            }, tx);

        var referrerName = string.IsNullOrWhiteSpace(referrer.Name) ? referrer.Username : referrer.Name;
        InsertTransaction(conn, tx, new Transaction
        {
            UserId = referrerId, UserName = referrerName, Type = TxTypes.Referral, Amount = commission,
            Status = TxStatus.Approved, Method = "سیستمی", ApprovedVia = "referral", Date = Today(),
        });
    }

    public Order? SetOrderStatus(int id, OrderStatus status, string? changedBy = null, string? reason = null) =>
        WriteTx<Order?>((conn, tx) =>
        {
            var oj = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Orders WHERE Id = @id", new { id }, tx);
            if (oj is null) return null;
            var o = Deserialize<Order>(oj)!;
            var settings = ReadSingleton<PricingSettings>(conn, tx, PricingKey);

            var from = o.Status;
            var wasCompleted = o.Status == OrderStatus.Completed;
            o.Status = status;
            if (status == OrderStatus.Completed) o.DeliveredAtUtc ??= DateTime.UtcNow;

            // approving the order verifies its linked card-to-card payment too.
            if (status == OrderStatus.Preparing)
            {
                var tj = conn.QueryFirstOrDefault<string>(@"
SELECT DataJson FROM Transactions
WHERE Status = @pending
  AND json_extract(DataJson,'$.OrderCode') = @code
  AND json_extract(DataJson,'$.Type')      = @type
LIMIT 1;",
                    new { pending = (int)TxStatus.Pending, code = o.Code, type = TxTypes.OrderPayment }, tx);
                if (tj is not null)
                {
                    var t = Deserialize<Transaction>(tj)!;
                    t.Status = TxStatus.Approved;
                    conn.Execute("UPDATE Transactions SET Status = @s, DataJson = @d WHERE Id = @Id",
                        new { s = (int)t.Status, d = Serialize(t), t.Id }, tx);
                }
            }

            if (status == OrderStatus.Completed && !wasCompleted) CreditReferralTx(conn, tx, o, settings);
            if (from != status) AppendOrderHistory(o, from, status, changedBy, reason);
            UpsertOrder(conn, tx, o);
            RefreshUserStats(conn, tx, o.UserId);
            return o;
        });

    // Records the in-site delivery content for an order and marks it completed (credits referral, stamps the
    // delivery time, notifies the customer) — all atomic.
    public Order? DeliverOrder(int id, string content, string? changedBy = null) =>
        WriteTx<Order?>((conn, tx) =>
        {
            var oj = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Orders WHERE Id = @id", new { id }, tx);
            if (oj is null) return null;
            var o = Deserialize<Order>(oj)!;
            var settings = ReadSingleton<PricingSettings>(conn, tx, PricingKey);

            var from = o.Status;
            o.DeliveryContent = content;
            o.DeliveredAt = Today();
            o.DeliveredAtUtc ??= DateTime.UtcNow;
            var wasCompleted = o.Status == OrderStatus.Completed;
            o.Status = OrderStatus.Completed;
            if (!wasCompleted) CreditReferralTx(conn, tx, o, settings);
            if (from != OrderStatus.Completed) AppendOrderHistory(o, from, OrderStatus.Completed, changedBy, "تحویل سفارش");
            UpsertOrder(conn, tx, o);
            RefreshUserStats(conn, tx, o.UserId);
            AddNotificationTx(conn, tx, o.UserId, "سفارش شما آماده شد",
                $"سفارش {o.Code} آماده و قابل مشاهده در حساب شماست.", "/account/orders");
            return o;
        });

    // Cancels an order: restores stock and, if already paid, refunds the wallet minus the cancellation penalty
    // — the stock restore + refund + transaction all commit together (or roll back together).
    public OrderActionResult CancelOrder(int id, string? changedBy = null, string? reason = null) =>
        WriteTx<OrderActionResult>((conn, tx) =>
        {
            var oj = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Orders WHERE Id = @id", new { id }, tx);
            if (oj is null) return new OrderActionResult(null, "سفارش یافت نشد.");
            var o = Deserialize<Order>(oj)!;
            if (o.Status == OrderStatus.Cancelled) return new OrderActionResult(null, "این سفارش قبلاً لغو شده است.");
            if (o.Status == OrderStatus.Completed) return new OrderActionResult(null, "سفارش تکمیل‌شده قابل لغو نیست.");
            var settings = ReadSingleton<PricingSettings>(conn, tx, PricingKey);
            var from = o.Status;

            // restore stock
            foreach (var line in o.Items)
            {
                var pj = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Products WHERE Id = @pid", new { pid = line.ProductId }, tx);
                if (pj is null) continue;
                var p = Deserialize<Product>(pj)!;
                p.Stock += line.Quantity;
                UpsertProduct(conn, tx, p);
            }

            // refund what was actually collected (full total once approved, else just the wallet portion).
            var collected = o.Status == OrderStatus.Preparing ? o.Total : o.WalletPaid;
            if (collected > 0)
            {
                var buyer = LoadUser(conn, tx, o.UserId);
                if (buyer is not null)
                {
                    var penalty = settings.CancellationPenaltyPercent;
                    var penaltyAmount = (long)Math.Round(collected * (double)penalty / 100.0, MidpointRounding.AwayFromZero);
                    var refund = Math.Max(0, collected - penaltyAmount);
                    buyer.Wallet += refund;
                    UpsertUser(conn, tx, buyer);

                    var name = string.IsNullOrWhiteSpace(buyer.Name) ? buyer.Username : buyer.Name;
                    InsertTransaction(conn, tx, new Transaction
                    {
                        UserId = buyer.Id, UserName = name, Type = TxTypes.Refund, Amount = refund,
                        Status = TxStatus.Approved, Method = "کیف پول", ApprovedVia = "refund", Date = Today(),
                    });
                }
            }

            o.Status = OrderStatus.Cancelled;
            AppendOrderHistory(o, from, OrderStatus.Cancelled, changedBy, reason ?? "لغو سفارش");
            UpsertOrder(conn, tx, o);
            RefreshUserStats(conn, tx, o.UserId);
            return new OrderActionResult(o, null);
        });

    // ── Snapshot / backup bridge ────────────────────────────────────────────────────────────────────────
    // Produces / consumes the SAME StoreSnapshot shape the JSON store uses, so a backup taken from either
    // implementation restores into the other (and the Telegram-bot/admin backup flow keeps working unchanged).
    // NOTE: this currently bridges the domains already migrated to SQLite (users, products, orders,
    // transactions, cards, discounts, payment methods, referral earnings, notifications, pricing/payment
    // settings). The remaining lists fill in as their tables are added in later chunks.

    private static readonly JsonSerializerOptions SnapshotJson = new()
    {
        WriteIndented = true, // matches the JSON store's on-disk/backup format
        Converters = { new JsonStringEnumConverter() },
    };

    private static int MaxId(SqliteConnection conn, string table) =>
        conn.ExecuteScalar<int?>($"SELECT MAX(Id) FROM {table}") ?? 0; // table is a constant literal, not user input

    // ── Settings singletons (the remaining blobs) ───────────────────────────────────────────────────────
    private const string SiteContentKey = "sitecontent";
    private const string AdvancedKey = "advanced";
    private const string EmailKey = "email";
    private const string TelegramKey = "telegram";
    private const string PlanTypesKey = "plantypes";
    private const string FavoritesKey = "favorites";

    public SiteContent GetSiteContent() => GetSingleton<SiteContent>(SiteContentKey);
    public void UpdateSiteContent(SiteContent c) { using var conn = OpenConnection(); WriteSingleton(conn, null, SiteContentKey, c); }
    public AdvancedSettings GetAdvancedSettings() => GetSingleton<AdvancedSettings>(AdvancedKey);
    public void UpdateAdvancedSettings(AdvancedSettings s) { using var conn = OpenConnection(); WriteSingleton(conn, null, AdvancedKey, s); }
    public EmailSettings GetEmailSettings() => GetSingleton<EmailSettings>(EmailKey);
    public void UpdateEmailSettings(EmailSettings settings) { using var conn = OpenConnection(); WriteSingleton(conn, null, EmailKey, settings); }
    public TelegramSettings GetTelegramSettings() => GetSingleton<TelegramSettings>(TelegramKey);

    public void UpdateTelegramSettings(TelegramSettings settings)
    {
        using var conn = OpenConnection();
        var t = ReadSingletonNoTx<TelegramSettings>(conn, TelegramKey);
        t.BackupEnabled = settings.BackupEnabled;
        t.AlertsEnabled = settings.AlertsEnabled;
        t.ReceiptBotEnabled = settings.ReceiptBotEnabled;
        t.BotToken = (settings.BotToken ?? "").Trim();
        t.ChatId = (settings.ChatId ?? "").Trim();
        t.ReceiptBotToken = (settings.ReceiptBotToken ?? "").Trim();
        t.ReceiptChatId = (settings.ReceiptChatId ?? "").Trim();
        t.IntervalHours = settings.IntervalHours < 1 ? 1 : settings.IntervalHours;
        t.LastBackupError = "";
        WriteSingleton(conn, null, TelegramKey, t);
    }

    public void RecordTelegramBackup(bool success, string error)
    {
        using var conn = OpenConnection();
        var t = ReadSingletonNoTx<TelegramSettings>(conn, TelegramKey);
        if (success) t.LastBackupAtUtc = DateTime.UtcNow;
        t.LastBackupError = success ? "" : error;
        WriteSingleton(conn, null, TelegramKey, t);
    }

    private static T ReadSingletonNoTx<T>(SqliteConnection conn, string key) where T : new()
    {
        var json = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Singletons WHERE Key = @key", new { key });
        return json is null ? new T() : (Deserialize<T>(json) ?? new T());
    }

    // ── USD rate + verification levels ──────────────────────────────────────────────────────────────────
    public void SetUsdRate(long manualToman, bool auto)
    {
        using var conn = OpenConnection();
        var s = ReadSingletonNoTx<PricingSettings>(conn, PricingKey);
        s.ManualUsdRate = Math.Max(0, manualToman);
        s.UsdRateAuto = auto;
        WriteSingleton(conn, null, PricingKey, s);
    }

    public bool ApplyUsdRate(long tomanPerUsd)
    {
        if (tomanPerUsd <= 0) return false;
        return WriteTx((conn, tx) =>
        {
            var changed = false;
            foreach (var row in conn.Query("SELECT Id, DataJson FROM Products", transaction: tx).ToList())
            {
                var p = Deserialize<Product>((string)row.DataJson)!;
                var rowChanged = false;
                if (p.PriceUsd > 0)
                {
                    var toman = (long)Math.Round(p.PriceUsd * tomanPerUsd);
                    if (toman != p.Price) { p.Price = toman; rowChanged = true; }
                }
                foreach (var pl in p.Plans)
                {
                    if (pl.PriceUsd <= 0) continue;
                    var planToman = (long)Math.Round(pl.PriceUsd * tomanPerUsd);
                    if (planToman != pl.Price) { pl.Price = planToman; rowChanged = true; }
                }
                if (rowChanged) { UpsertProduct(conn, tx, p); changed = true; }
            }
            foreach (var pl in conn.Query<string>("SELECT DataJson FROM Plans", transaction: tx).ToList())
            {
                var plan = Deserialize<SubscriptionPlan>(pl)!;
                if (plan.PriceUsd <= 0) continue;
                var toman = (long)Math.Round(plan.PriceUsd * tomanPerUsd);
                if (toman != plan.Price) { plan.Price = toman; conn.Execute("UPDATE Plans SET DataJson=@d WHERE Id=@id", new { d = Serialize(plan), id = plan.Id }, tx); changed = true; }
            }
            return changed;
        });
    }

    public void HealVerificationLevels() =>
        WriteTx<object?>((conn, tx) =>
        {
            var cards = conn.Query<string>("SELECT DataJson FROM Cards", transaction: tx).Select(j => Deserialize<BankCard>(j)!).ToList();
            foreach (var uj in conn.Query<string>("SELECT DataJson FROM Users", transaction: tx).ToList())
            {
                var u = Deserialize<AppUser>(uj)!;
                var derived = u.Verified ? 2 : (cards.Any(c => c.UserId == u.Id && c.Status == BankCardStatus.Approved) ? 1 : 0);
                var changed = false;
                if (u.VerificationLevel < derived) { u.VerificationLevel = derived; changed = true; }
                if (u.VerificationLevel >= 2 && !u.Verified) { u.Verified = true; changed = true; }
                if (changed) UpsertUser(conn, tx, u);
            }
            return null;
        });

    public AppUser? SetVerificationLevel(int userId, int level) =>
        WriteTx<AppUser?>((conn, tx) =>
        {
            var user = LoadUser(conn, tx, userId);
            if (user is null) return null;
            level = Math.Clamp(level, 0, 2);

            if (level < 2)
                foreach (var row in conn.Query("SELECT Id, DataJson FROM Kyc", transaction: tx).ToList())
                {
                    var k = Deserialize<KycRequest>((string)row.DataJson)!;
                    if (k.UserId == userId && k.Status == KycStatus.Approved)
                    {
                        k.Status = KycStatus.Rejected; k.Note = "احراز هویت توسط مدیر لغو شد";
                        conn.Execute("UPDATE Kyc SET DataJson=@d WHERE Id=@id", new { d = Serialize(k), id = k.Id }, tx);
                    }
                }
            if (level < 1)
                foreach (var c in conn.Query<string>("SELECT DataJson FROM Cards", transaction: tx).ToList())
                {
                    var card = Deserialize<BankCard>(c)!;
                    if (card.UserId == userId && card.Status == BankCardStatus.Approved)
                    {
                        card.Status = BankCardStatus.Rejected; card.Note = "توسط مدیر لغو شد";
                        conn.Execute("UPDATE Cards SET Status=@s, DataJson=@d WHERE Id=@id", new { s = (int)card.Status, d = Serialize(card), id = card.Id }, tx);
                    }
                }

            user.VerificationLevel = level;
            user.Verified = level >= 2;
            UpsertUser(conn, tx, user);
            return user;
        });

    // ── Categories ──────────────────────────────────────────────────────────────────────────────────────
    public IReadOnlyList<Category> GetCategories() => AllJson<Category>("Categories").OrderBy(c => c.SortOrder).ToList();
    public Category? GetCategory(int id) => OneJson<Category>("Categories", id);
    public int CountProducts(int categoryId)
    {
        using var conn = OpenConnection();
        return conn.ExecuteScalar<int>("SELECT COUNT(1) FROM Products WHERE CategoryId = @categoryId AND IsActive = 1", new { categoryId });
    }
    public Category AddCategory(Category category) { InsertJson("Categories", category, (c, id) => c.Id = id); return category; }
    public bool UpdateCategory(Category category)
    {
        var existing = GetCategory(category.Id);
        if (existing is null) return false;
        existing.Name = category.Name; existing.Slug = category.Slug; existing.Icon = category.Icon;
        existing.Description = category.Description;
        existing.IsActive = category.IsActive; existing.SortOrder = category.SortOrder;
        return UpdateJson("Categories", existing.Id, existing);
    }
    public bool DeleteCategory(int id) => DeleteRow("Categories", id);

    // ── Subscription plans ──────────────────────────────────────────────────────────────────────────────
    public IReadOnlyList<SubscriptionPlan> GetPlans() => AllJson<SubscriptionPlan>("Plans").OrderBy(p => p.Months).ToList();
    public SubscriptionPlan AddPlan(SubscriptionPlan plan) { InsertJson("Plans", plan, (p, id) => p.Id = id); return plan; }
    public bool UpdatePlan(SubscriptionPlan plan)
    {
        var existing = OneJson<SubscriptionPlan>("Plans", plan.Id);
        if (existing is null) return false;
        existing.Label = plan.Label; existing.Months = plan.Months; existing.Price = plan.Price;
        existing.PriceUsd = plan.PriceUsd; existing.DiscountPercent = plan.DiscountPercent;
        return UpdateJson("Plans", existing.Id, existing);
    }
    public bool DeletePlan(int id) => DeleteRow("Plans", id);

    // ── Product delivery templates ──────────────────────────────────────────────────────────────────────
    public IReadOnlyList<ProductDeliveryTemplate> GetDeliveryTemplates(int productId) =>
        GetProduct(productId)?.DeliveryTemplates.ToList() ?? (IReadOnlyList<ProductDeliveryTemplate>)Array.Empty<ProductDeliveryTemplate>();

    public ProductDeliveryTemplate? AddDeliveryTemplate(int productId, string title, string content) =>
        WriteTx<ProductDeliveryTemplate?>((conn, tx) =>
        {
            var pj = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Products WHERE Id = @productId", new { productId }, tx);
            if (pj is null) return null;
            var p = Deserialize<Product>(pj)!;
            var tpl = new ProductDeliveryTemplate
            {
                Id = (p.DeliveryTemplates.Count == 0 ? 0 : p.DeliveryTemplates.Max(x => x.Id)) + 1,
                ProductId = productId, Title = title.Trim(), TemplateContent = content,
            };
            p.DeliveryTemplates.Add(tpl);
            UpsertProduct(conn, tx, p);
            return tpl;
        });

    public bool DeleteDeliveryTemplate(int productId, int templateId) =>
        WriteTx((conn, tx) =>
        {
            var pj = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Products WHERE Id = @productId", new { productId }, tx);
            if (pj is null) return false;
            var p = Deserialize<Product>(pj)!;
            var removed = p.DeliveryTemplates.RemoveAll(x => x.Id == templateId) > 0;
            if (removed) UpsertProduct(conn, tx, p);
            return removed;
        });

    // ── Users: rename/email/owner ───────────────────────────────────────────────────────────────────────
    public string? SetUsername(int userId, string username) =>
        WriteTx<string?>((conn, tx) =>
        {
            var user = LoadUser(conn, tx, userId);
            if (user is null) return "کاربر یافت نشد.";
            var u = (username ?? "").Trim();
            if (string.Equals(u, user.Username, StringComparison.Ordinal)) return null;
            if (u.Length is < 3 or > 20) return "نام کاربری باید بین ۳ تا ۲۰ کاراکتر باشد.";
            if (!u.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')))
                return "نام کاربری فقط می‌تواند شامل حروف و اعداد انگلیسی باشد (بدون فاصله و خط تیره).";
            var taken = conn.ExecuteScalar<long>("SELECT COUNT(1) FROM Users WHERE Id <> @userId AND Username = @u COLLATE NOCASE", new { userId, u }, tx) > 0;
            if (taken) return "این نام کاربری قبلاً گرفته شده است.";
            user.Username = u;
            UpsertUser(conn, tx, user);
            return null;
        });

    public string? SetEmail(int userId, string email) =>
        WriteTx<string?>((conn, tx) =>
        {
            var user = LoadUser(conn, tx, userId);
            if (user is null) return "کاربر یافت نشد.";
            var mail = (email ?? "").Trim();
            if (string.Equals(mail, user.Email, StringComparison.OrdinalIgnoreCase)) return null;
            if (!string.IsNullOrWhiteSpace(mail) &&
                conn.ExecuteScalar<long>("SELECT COUNT(1) FROM Users WHERE Id <> @userId AND Email = @mail COLLATE NOCASE", new { userId, mail }, tx) > 0)
                return "این ایمیل قبلاً برای حساب دیگری ثبت شده است.";
            user.Email = mail;
            UpsertUser(conn, tx, user);
            return null;
        });

    public void EnsureOwnerFromEnvironment()
    {
        var username = Environment.GetEnvironmentVariable("PHONIX_OWNER_USERNAME")?.Trim();
        var password = Environment.GetEnvironmentVariable("PHONIX_OWNER_PASSWORD");
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) return;

        WriteTx<object?>((conn, tx) =>
        {
            var oj = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Users WHERE Username = @username COLLATE NOCASE", new { username }, tx);
            if (oj is null)
            {
                var owner = new AppUser
                {
                    Name = username, Username = username, Password = PasswordHasher.Hash(password), Role = UserRole.Admin,
                    SecurityStamp = StoreData.NewStamp(), EmailVerified = true, Verified = true, VerificationLevel = 2, JoinedAt = Today(),
                };
                var id = (int)conn.ExecuteScalar<long>(@"
INSERT INTO Users (Username, Email, Phone, Role, Blocked, ReferredBy, VerificationLevel, DataJson)
VALUES (@Username,@Email,@Phone,@Role,@Blocked,@ReferredBy,@VerificationLevel,@DataJson); SELECT last_insert_rowid();",
                    new { owner.Username, owner.Email, owner.Phone, Role = (int)owner.Role, Blocked = 0, owner.ReferredBy, owner.VerificationLevel, DataJson = Serialize(owner) }, tx);
                owner.Id = id; owner.Code = $"U-{1000 + id}";
                conn.Execute("UPDATE Users SET DataJson=@d WHERE Id=@id", new { d = Serialize(owner), id }, tx);
            }
            else
            {
                var owner = Deserialize<AppUser>(oj)!;
                var changed = false;
                if (owner.Role != UserRole.Admin) { owner.Role = UserRole.Admin; changed = true; }
                if (owner.Blocked) { owner.Blocked = false; changed = true; }
                if (!PasswordHasher.Verify(password, owner.Password)) { owner.Password = PasswordHasher.Hash(password); owner.SecurityStamp = StoreData.NewStamp(); changed = true; }
                if (changed) UpsertUser(conn, tx, owner);
            }
            return null;
        });
    }

    // ── Staff / auth / 2FA / tokens ─────────────────────────────────────────────────────────────────────
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (int UserId, string Purpose, DateTime ExpiresAt)> _tokens = new();

    public StaffResult PromoteToStaff(string username, UserRole role, IEnumerable<string> permissions) =>
        WriteTx<StaffResult>((conn, tx) =>
        {
            var u = (username ?? "").Trim();
            if (string.IsNullOrWhiteSpace(u)) return new StaffResult(null, "نام کاربری را وارد کنید.");
            var uj = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Users WHERE Username = @u COLLATE NOCASE", new { u }, tx);
            var user = uj is null ? null : Deserialize<AppUser>(uj);
            if (user is null) return new StaffResult(null, "کاربری با این نام کاربری یافت نشد.");
            if (user.Role != UserRole.Customer) return new StaffResult(null, "این حساب از قبل دسترسی کارمندی دارد.");
            user.Role = role;
            user.Permissions = role == UserRole.Support ? permissions.Distinct().ToList() : new();
            user.SecurityStamp = StoreData.NewStamp();
            UpsertUser(conn, tx, user);
            return new StaffResult(user, null);
        });

    public bool SetUserPermissions(int userId, IEnumerable<string> permissions) =>
        UpdateUser(userId, u => u.Permissions = permissions.Distinct().ToList());

    public string RotateSecurityStamp(int userId)
    {
        var stamp = StoreData.NewStamp();
        var ok = UpdateUser(userId, u => u.SecurityStamp = stamp);
        return ok ? stamp : "";
    }

    public bool SetTwoFactorSecret(int userId, string secret) =>
        UpdateUser(userId, u => { u.TwoFactorSecret = secret; u.TwoFactorEnabled = false; });

    public bool SetTwoFactorEnabled(int userId, bool enabled) =>
        UpdateUser(userId, u => { u.TwoFactorEnabled = enabled; if (!enabled) u.TwoFactorSecret = ""; });

    public string CreateToken(int userId, string purpose, TimeSpan lifetime)
    {
        var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        _tokens[token] = (userId, purpose, DateTime.UtcNow + lifetime);
        return token;
    }

    public int? ConsumeToken(string? token, string purpose)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        if (!_tokens.TryRemove(token, out var entry)) return null;
        if (entry.Purpose != purpose || entry.ExpiresAt <= DateTime.UtcNow) return null;
        return entry.UserId;
    }

    // ── Admin badge counts ──────────────────────────────────────────────────────────────────────────────
    public AdminBadgeCounts GetAdminBadgeCounts()
    {
        using var conn = OpenConnection();
        int Count(string sql, object p) => conn.ExecuteScalar<int>(sql, p);
        var tickets = AllJson<Ticket>("Tickets");
        var kyc = AllJson<KycRequest>("Kyc");
        var comments = AllJson<Comment>("Comments");
        var convos = AllJson<ChatConversation>("Conversations");
        return new AdminBadgeCounts(
            PendingOrders: Count("SELECT COUNT(1) FROM Orders WHERE Status=@s", new { s = (int)OrderStatus.PendingApproval }),
            PreparingOrders: Count("SELECT COUNT(1) FROM Orders WHERE Status=@s", new { s = (int)OrderStatus.Preparing }),
            PendingTransactions: Count("SELECT COUNT(1) FROM Transactions WHERE Status=@s", new { s = (int)TxStatus.Pending }),
            OpenTickets: tickets.Count(t => t.Status == TicketStatus.Open),
            PendingKyc: kyc.Count(k => k.Status == KycStatus.Pending),
            PendingCards: Count("SELECT COUNT(1) FROM Cards WHERE Status=@s", new { s = (int)BankCardStatus.Pending }),
            PendingComments: comments.Count(c => c.Status == CommentStatus.Pending),
            UnreadChats: convos.Count(c => c.Messages.Any(m => !m.FromAdmin && m.Id > c.AdminReadUpTo)));
    }

    // ── Bank cards ──────────────────────────────────────────────────────────────────────────────────────
    public IReadOnlyList<BankCard> GetAllCards(BankCardStatus? status = null)
    {
        var all = AllJson<BankCard>("Cards");
        if (status is BankCardStatus s) all = all.Where(c => c.Status == s).ToList();
        return all.OrderByDescending(c => c.Id).ToList();
    }
    public IReadOnlyList<BankCard> GetUserCards(int userId) =>
        AllJson<BankCard>("Cards").Where(c => c.UserId == userId).OrderByDescending(c => c.Id).ToList();
    public BankCard? GetCard(int id) => OneJson<BankCard>("Cards", id);

    public AddCardResult AddCard(int userId, string cardNumber, string holderName, string cardImage) =>
        WriteTx<AddCardResult>((conn, tx) =>
        {
            var user = LoadUser(conn, tx, userId);
            if (user is null) return new AddCardResult(null, "کاربر یافت نشد.");
            var digits = InputValidation.DigitsOnly(cardNumber);
            if (digits.Length != 16) return new AddCardResult(null, "شماره کارت باید ۱۶ رقم باشد.");
            if (!InputValidation.PassesLuhn(digits)) return new AddCardResult(null, "شماره کارت نامعتبر است.");
            var name = (holderName ?? "").Trim();
            if (name.Length == 0) return new AddCardResult(null, "نام صاحب کارت را وارد کنید.");
            if (string.IsNullOrWhiteSpace(cardImage)) return new AddCardResult(null, "تصویر کارت بانکی را بارگذاری کنید.");
            var dup = conn.Query<string>("SELECT DataJson FROM Cards WHERE UserId=@userId", new { userId }, tx)
                .Select(j => Deserialize<BankCard>(j)!).Any(c => c.CardNumber == digits);
            if (dup) return new AddCardResult(null, "این کارت قبلاً ثبت شده است.");

            var card = new BankCard
            {
                UserId = userId, UserName = string.IsNullOrWhiteSpace(user.Name) ? user.Username : user.Name,
                CardNumber = digits, HolderName = name, CardImage = cardImage.Trim(), Bank = BankFromCard(digits),
                Status = BankCardStatus.Pending, Date = Today(),
            };
            var id = (int)conn.ExecuteScalar<long>(
                "INSERT INTO Cards (UserId, Status, DataJson) VALUES (@UserId,@Status,@DataJson); SELECT last_insert_rowid();",
                new { card.UserId, Status = (int)card.Status, DataJson = Serialize(card) }, tx);
            card.Id = id;
            conn.Execute("UPDATE Cards SET DataJson=@d WHERE Id=@id", new { d = Serialize(card), id }, tx);
            return new AddCardResult(card, null);
        });

    public BankCard? SetCardStatus(int id, BankCardStatus status, string? note) =>
        WriteTx<BankCard?>((conn, tx) =>
        {
            var cj = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Cards WHERE Id=@id", new { id }, tx);
            if (cj is null) return null;
            var card = Deserialize<BankCard>(cj)!;
            card.Status = status; card.Note = note;
            card.RejectionReason = status == BankCardStatus.Rejected ? note : null;
            if (status == BankCardStatus.Approved)
            {
                var owner = LoadUser(conn, tx, card.UserId);
                if (owner is not null && owner.VerificationLevel < 1)
                {
                    owner.VerificationLevel = 1;
                    UpsertUser(conn, tx, owner);
                    AddNotificationTx(conn, tx, owner.Id, "احراز هویت سطح ۱ تأیید شد",
                        "تبریک! احراز هویت سطح یک شما با موفقیت انجام شد و کارت بانکی شما تأیید گردید. اکنون می‌توانید خرید کنید.", "/account/kyc");
                }
            }
            conn.Execute("UPDATE Cards SET Status=@s, DataJson=@d WHERE Id=@id", new { s = (int)card.Status, d = Serialize(card), id }, tx);
            return card;
        });

    public bool DeleteCard(int id) => DeleteRow("Cards", id);

    private static string BankFromCard(string digits)
    {
        if (digits.Length < 6) return "";
        return digits[..6] switch
        {
            "603799" => "ملی ایران", "589210" => "سپه", "627648" or "207177" => "توسعه صادرات",
            "627961" => "صنعت و معدن", "603770" => "کشاورزی", "628023" => "مسکن", "627760" => "پست بانک",
            "502908" => "توسعه تعاون", "627412" => "اقتصاد نوین", "622106" or "627884" or "639194" => "پارسیان",
            "502229" or "639347" => "پاسارگاد", "627488" or "502910" => "کارآفرین", "621986" => "سامان",
            "639346" => "سینا", "502938" => "دی", "603769" => "صادرات", "610433" or "991975" => "ملت",
            "627353" or "585983" => "تجارت", "589463" => "رفاه", "502806" or "504172" => "شهر",
            "636214" => "آینده", "505785" => "ایران زمین", "636949" => "حکمت ایرانیان", "505416" => "گردشگری",
            "606373" => "قرض‌الحسنه مهر ایران", "628157" => "موسسه اعتباری توسعه", "606256" => "موسسه اعتباری ملل",
            _ => "",
        };
    }

    // ── Comments ────────────────────────────────────────────────────────────────────────────────────────
    public IReadOnlyList<Comment> GetComments(int? productId = null, CommentStatus? status = null)
    {
        var all = AllJson<Comment>("Comments").AsEnumerable();
        if (productId is int pid) all = all.Where(c => c.ProductId == pid);
        if (status is CommentStatus s) all = all.Where(c => c.Status == s);
        return all.OrderByDescending(c => c.Id).ToList();
    }
    public IReadOnlyList<Comment> GetApprovedForProduct(int productId) =>
        AllJson<Comment>("Comments").Where(c => c.ProductId == productId && c.Status == CommentStatus.Approved).OrderBy(c => c.Id).ToList();

    public Comment AddComment(Comment c)
    {
        if (string.IsNullOrWhiteSpace(c.Date)) c.Date = Today();
        InsertJson("Comments", c, (x, id) => x.Id = id);
        return c;
    }
    public bool SetCommentStatus(int id, CommentStatus status)
    {
        var c = OneJson<Comment>("Comments", id);
        if (c is null) return false;
        c.Status = status;
        return UpdateJson("Comments", id, c);
    }
    public bool SetCommentFeaturedOnHome(int id, bool on)
    {
        var c = OneJson<Comment>("Comments", id);
        if (c is null) return false;
        c.FeaturedOnHome = on;
        return UpdateJson("Comments", id, c);
    }
    public IReadOnlyList<Comment> GetHomeTestimonials() =>
        AllJson<Comment>("Comments")
            .Where(c => c.FeaturedOnHome && c.Status == CommentStatus.Approved && c.ParentId == null)
            .OrderByDescending(c => c.Id)
            .ToList();
    public Comment? AddReply(int parentId, string body, string author)
    {
        var parent = OneJson<Comment>("Comments", parentId);
        if (parent is null) return null;
        var reply = new Comment
        {
            ProductId = parent.ProductId, UserName = author, Body = body, Rating = 0,
            Status = CommentStatus.Approved, ParentId = parentId, IsAdminReply = true, Date = Today(),
        };
        InsertJson("Comments", reply, (x, id) => x.Id = id);
        return reply;
    }
    public bool DeleteComment(int id) =>
        WriteTx((conn, tx) =>
        {
            var ids = conn.Query("SELECT Id, DataJson FROM Comments", transaction: tx)
                .Where(r => (long)r.Id == id || (Deserialize<Comment>((string)r.DataJson)!.ParentId == id))
                .Select(r => (long)r.Id).ToList();
            if (ids.Count == 0) return false;
            conn.Execute("DELETE FROM Comments WHERE Id = @id", ids.Select(x => new { id = x }), tx);
            return true;
        });

    // ── KYC ─────────────────────────────────────────────────────────────────────────────────────────────
    public IReadOnlyList<KycRequest> GetAllKyc(KycStatus? status = null)
    {
        var all = AllJson<KycRequest>("Kyc").AsEnumerable();
        if (status is KycStatus s) all = all.Where(k => k.Status == s);
        return all.OrderByDescending(k => k.Id).ToList();
    }
    public KycRequest? GetKycForUser(int userId) =>
        AllJson<KycRequest>("Kyc").Where(k => k.UserId == userId).OrderByDescending(k => k.Id).FirstOrDefault();

    public KycRequest SubmitKyc(KycRequest input) =>
        WriteTx((conn, tx) =>
        {
            var existingRow = conn.Query("SELECT Id, DataJson FROM Kyc", transaction: tx)
                .FirstOrDefault(r => Deserialize<KycRequest>((string)r.DataJson)!.UserId == input.UserId);
            if (existingRow is null)
            {
                input.Status = KycStatus.Pending; input.Note = null;
                if (string.IsNullOrWhiteSpace(input.Date)) input.Date = Today();
                var id = (int)conn.ExecuteScalar<long>("INSERT INTO Kyc (DataJson) VALUES (@d); SELECT last_insert_rowid();", new { d = Serialize(input) }, tx);
                input.Id = id;
                conn.Execute("UPDATE Kyc SET DataJson=@d WHERE Id=@id", new { d = Serialize(input), id }, tx);
                return input;
            }
            var existing = Deserialize<KycRequest>((string)existingRow.DataJson)!;
            existing.FullName = input.FullName; existing.NationalId = input.NationalId; existing.BirthDate = input.BirthDate;
            existing.CardImage = input.CardImage; existing.SelfieImage = input.SelfieImage;
            existing.Status = KycStatus.Pending; existing.Note = null; existing.Date = Today();
            conn.Execute("UPDATE Kyc SET DataJson=@d WHERE Id=@id", new { d = Serialize(existing), id = existing.Id }, tx);
            return existing;
        });

    public KycRequest? SetKycStatus(int id, KycStatus status, string? note) =>
        WriteTx<KycRequest?>((conn, tx) =>
        {
            var kj = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Kyc WHERE Id=@id", new { id }, tx);
            if (kj is null) return null;
            var req = Deserialize<KycRequest>(kj)!;
            req.Status = status; req.Note = note;
            req.RejectionReason = status == KycStatus.Rejected ? note : null;
            if (status == KycStatus.Approved)
            {
                var user = LoadUser(conn, tx, req.UserId);
                if (user is not null) { user.VerificationLevel = 2; user.Verified = true; UpsertUser(conn, tx, user); }
            }
            conn.Execute("UPDATE Kyc SET DataJson=@d WHERE Id=@id", new { d = Serialize(req), id }, tx);
            return req;
        });

    // ── Content: hero / home categories / showcase / blog ───────────────────────────────────────────────
    private static List<T> Ordered<T>(IEnumerable<T> items) where T : IContentItem =>
        items.OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToList();

    public IReadOnlyList<HeroSlide> GetHeroSlides() => Ordered(AllJson<HeroSlide>("HeroSlides"));
    public HeroSlide? GetHeroSlide(int id) => OneJson<HeroSlide>("HeroSlides", id);
    public HeroSlide AddHeroSlide(HeroSlide s) { InsertJson("HeroSlides", s, (x, id) => x.Id = id); return s; }
    public bool UpdateHeroSlide(HeroSlide s) { if (OneJson<HeroSlide>("HeroSlides", s.Id) is null) return false; return UpdateJson("HeroSlides", s.Id, s); }
    public bool DeleteHeroSlide(int id) => DeleteRow("HeroSlides", id);

    public IReadOnlyList<HomeCategory> GetHomeCategories() => Ordered(AllJson<HomeCategory>("HomeCategories"));
    public HomeCategory? GetHomeCategory(int id) => OneJson<HomeCategory>("HomeCategories", id);
    public HomeCategory AddHomeCategory(HomeCategory c) { InsertJson("HomeCategories", c, (x, id) => x.Id = id); return c; }
    public bool UpdateHomeCategory(HomeCategory c) { if (OneJson<HomeCategory>("HomeCategories", c.Id) is null) return false; return UpdateJson("HomeCategories", c.Id, c); }
    public bool DeleteHomeCategory(int id) => DeleteRow("HomeCategories", id);

    public IReadOnlyList<Showcase> GetShowcase() => Ordered(AllJson<Showcase>("Showcase"));
    public Showcase? GetShowcaseItem(int id) => OneJson<Showcase>("Showcase", id);
    public Showcase AddShowcase(Showcase s) { InsertJson("Showcase", s, (x, id) => x.Id = id); return s; }
    public bool UpdateShowcase(Showcase s) { if (OneJson<Showcase>("Showcase", s.Id) is null) return false; return UpdateJson("Showcase", s.Id, s); }
    public bool DeleteShowcase(int id) => DeleteRow("Showcase", id);

    public IReadOnlyList<BlogPost> GetBlogPosts() => Ordered(AllJson<BlogPost>("BlogPosts"));
    public BlogPost? GetBlogPost(int id) => OneJson<BlogPost>("BlogPosts", id);
    public BlogPost AddBlogPost(BlogPost p) { InsertJson("BlogPosts", p, (x, id) => x.Id = id); return p; }
    public bool UpdateBlogPost(BlogPost p) { if (OneJson<BlogPost>("BlogPosts", p.Id) is null) return false; return UpdateJson("BlogPosts", p.Id, p); }
    public bool DeleteBlogPost(int id) => DeleteRow("BlogPosts", id);

    // ── Finance: payment methods ────────────────────────────────────────────────────────────────────────
    public IReadOnlyList<PaymentMethod> GetPaymentMethods() => Ordered(AllJson<PaymentMethod>("PaymentMethods"));
    public PaymentMethod? GetPaymentMethod(int id) => OneJson<PaymentMethod>("PaymentMethods", id);
    public PaymentMethod AddPaymentMethod(PaymentMethod m) { InsertJson("PaymentMethods", m, (x, id) => x.Id = id); return m; }
    public bool UpdatePaymentMethod(PaymentMethod m) { if (OneJson<PaymentMethod>("PaymentMethods", m.Id) is null) return false; return UpdateJson("PaymentMethods", m.Id, m); }
    public bool DeletePaymentMethod(int id) => DeleteRow("PaymentMethods", id);

    // ── Finance: transactions ───────────────────────────────────────────────────────────────────────────
    public IReadOnlyList<Transaction> GetTransactions(TxStatus? status = null)
    {
        using var conn = OpenConnection();
        var sql = "SELECT DataJson FROM Transactions";
        if (status is not null) sql += " WHERE Status = @status";
        sql += " ORDER BY Id DESC;";
        return conn.Query<string>(sql, new { status = status is null ? 0 : (int)status.Value }).Select(j => Deserialize<Transaction>(j)!).ToList();
    }
    public Transaction? GetTransaction(int id)
    {
        using var conn = OpenConnection();
        var j = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Transactions WHERE Id=@id", new { id });
        return j is null ? null : Deserialize<Transaction>(j);
    }
    public IReadOnlyList<Transaction> GetUserTransactions(int userId)
    {
        using var conn = OpenConnection();
        return conn.Query<string>("SELECT DataJson FROM Transactions WHERE UserId=@userId ORDER BY Id DESC", new { userId })
            .Select(j => Deserialize<Transaction>(j)!).ToList();
    }
    public Transaction AddTransaction(Transaction t) => WriteTx((conn, tx) => InsertTransaction(conn, tx, t));

    public bool SetTransactionStatus(int id, TxStatus status, string via, string? note) =>
        WriteTx((conn, tx) =>
        {
            var ej = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Transactions WHERE Id=@id", new { id }, tx);
            if (ej is null) return false;
            var e = Deserialize<Transaction>(ej)!;
            var becomingApproved = e.Status != TxStatus.Approved && status == TxStatus.Approved;

            if (e.Type == TxTypes.WalletTopUp && e.Amount > 0 && e.UserId > 0)
            {
                var wasApproved = e.Status == TxStatus.Approved;
                var willBeApproved = status == TxStatus.Approved;
                if (wasApproved != willBeApproved)
                {
                    var owner = LoadUser(conn, tx, e.UserId);
                    if (owner is not null) { owner.Wallet = Math.Max(0, owner.Wallet + (willBeApproved ? e.Amount : -e.Amount)); UpsertUser(conn, tx, owner); }
                }
            }

            if (e.Type == TxTypes.Withdraw && e.Amount < 0 && e.UserId > 0)
            {
                var wasRefunded = e.Status == TxStatus.Rejected;
                var willBeRefunded = status == TxStatus.Rejected;
                if (wasRefunded != willBeRefunded)
                {
                    var owner = LoadUser(conn, tx, e.UserId);
                    if (owner is not null) { owner.Wallet = Math.Max(0, owner.Wallet + (willBeRefunded ? -e.Amount : e.Amount)); UpsertUser(conn, tx, owner); }
                }
            }

            if (e.Type == TxTypes.OrderPayment && !string.IsNullOrWhiteSpace(e.OrderCode) && e.Status != TxStatus.Approved && status == TxStatus.Approved)
            {
                var oj = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Orders WHERE Code=@code", new { code = e.OrderCode }, tx);
                if (oj is not null)
                {
                    var ord = Deserialize<Order>(oj)!;
                    if (ord.Status == OrderStatus.PendingApproval)
                    {
                        ord.Status = OrderStatus.Preparing;
                        AppendOrderHistory(ord, OrderStatus.PendingApproval, OrderStatus.Preparing, "سیستم (تأیید پرداخت)", "تأیید پرداخت سفارش");
                        UpsertOrder(conn, tx, ord);
                        RefreshUserStats(conn, tx, ord.UserId);
                    }
                }
            }

            e.Status = status; e.ApprovedVia = via; if (note is not null) e.Note = note;
            conn.Execute("UPDATE Transactions SET Status=@s, DataJson=@d WHERE Id=@id", new { s = (int)e.Status, d = Serialize(e), id }, tx);

            if (becomingApproved && e.UserId > 0)
            {
                if (e.Type == TxTypes.WalletTopUp)
                    AddNotificationTx(conn, tx, e.UserId, "شارژ کیف پول", $"کیف پول شما به مبلغ {e.Amount:N0} تومان شارژ شد.", "/account/wallet");
                else if (e.Type == TxTypes.OrderPayment)
                    AddNotificationTx(conn, tx, e.UserId, "پرداخت تأیید شد", "پرداخت سفارش شما تأیید و سفارش در حال آماده‌سازی است.", "/account/orders");
            }
            return true;
        });

    // ── Notifications ───────────────────────────────────────────────────────────────────────────────────
    public Notification AddNotification(int? userId, string title, string body, string? link = null) =>
        WriteTx((conn, tx) =>
        {
            var n = new Notification { UserId = userId, Title = title, Body = body, Link = link, CreatedAtUtc = DateTime.UtcNow.ToString("o") };
            // A broadcast is frozen to the users who exist right now, so newcomers never see older broadcasts.
            if (userId is null) n.AudienceMaxUserId = conn.ExecuteScalar<int?>("SELECT MAX(Id) FROM Users", transaction: tx) ?? 0;
            var nid = (int)conn.ExecuteScalar<long>("INSERT INTO Notifications (UserId, DataJson) VALUES (@UserId,@DataJson); SELECT last_insert_rowid();",
                new { UserId = userId, DataJson = Serialize(n) }, tx);
            n.Id = nid;
            conn.Execute("UPDATE Notifications SET DataJson=@d WHERE Id=@id", new { d = Serialize(n), id = nid }, tx);
            return n;
        });

    // A broadcast (UserId null) reaches a user only if it was sent while they already had an account; a private
    // notification always reaches its owner. AudienceMaxUserId == 0 = legacy/unbounded (shown to everyone).
    private static bool IsVisibleTo(Notification n, int userId) =>
        n.UserId == userId || (n.UserId is null && (n.AudienceMaxUserId == 0 || userId <= n.AudienceMaxUserId));

    public IReadOnlyList<Notification> GetUserNotifications(int userId)
    {
        using var conn = OpenConnection();
        return conn.Query<string>("SELECT DataJson FROM Notifications WHERE UserId=@u OR UserId IS NULL", new { u = userId })
            .Select(j => Deserialize<Notification>(j)!).Where(n => IsVisibleTo(n, userId)).OrderByDescending(n => n.CreatedAtUtc).ToList();
    }
    public IReadOnlyList<Notification> GetAllNotifications() =>
        AllJson<Notification>("Notifications").OrderByDescending(n => n.CreatedAtUtc).ToList();
    public int CountUnread(int userId)
    {
        using var conn = OpenConnection();
        return conn.Query<string>("SELECT DataJson FROM Notifications WHERE UserId=@u OR UserId IS NULL", new { u = userId })
            .Select(j => Deserialize<Notification>(j)!).Count(n => IsVisibleTo(n, userId) && !n.ReadBy.Contains(userId));
    }
    public void MarkNotificationsRead(int userId) =>
        WriteTx<object?>((conn, tx) =>
        {
            foreach (var row in conn.Query("SELECT Id, DataJson FROM Notifications WHERE UserId=@u OR UserId IS NULL", new { u = userId }, tx).ToList())
            {
                var n = Deserialize<Notification>((string)row.DataJson)!;
                if (IsVisibleTo(n, userId) && !n.ReadBy.Contains(userId)) { n.ReadBy.Add(userId); conn.Execute("UPDATE Notifications SET DataJson=@d WHERE Id=@id", new { d = Serialize(n), id = (long)row.Id }, tx); }
            }
            return null;
        });
    public bool DeleteNotification(int id) => DeleteRow("Notifications", id);

    // ── Favorites (singleton dict) ──────────────────────────────────────────────────────────────────────
    public IReadOnlyList<int> GetFavorites(int userId)
    {
        var fav = GetSingleton<Dictionary<int, List<int>>>(FavoritesKey);
        return fav.TryGetValue(userId, out var list) ? list.ToList() : new List<int>();
    }
    public bool ToggleFavorite(int userId, int productId) =>
        WriteTx((conn, tx) =>
        {
            var fav = ReadSingleton<Dictionary<int, List<int>>>(conn, tx, FavoritesKey);
            if (!fav.TryGetValue(userId, out var list)) { list = new List<int>(); fav[userId] = list; }
            bool added;
            if (list.Remove(productId)) added = false;
            else { list.Add(productId); added = true; }
            WriteSingleton(conn, tx, FavoritesKey, fav);
            return added;
        });

    // ── Plan types (singleton list) ─────────────────────────────────────────────────────────────────────
    public IReadOnlyList<string> GetPlanTypes() => GetSingleton<List<string>>(PlanTypesKey);
    public bool AddPlanType(string name)
    {
        name = (name ?? "").Trim();
        if (string.IsNullOrEmpty(name)) return false;
        return WriteTx((conn, tx) =>
        {
            var types = ReadSingleton<List<string>>(conn, tx, PlanTypesKey);
            if (types.Any(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase))) return false;
            types.Add(name);
            WriteSingleton(conn, tx, PlanTypesKey, types);
            return true;
        });
    }
    public bool RenamePlanType(string oldName, string newName)
    {
        oldName = (oldName ?? "").Trim(); newName = (newName ?? "").Trim();
        if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName)) return false;
        return WriteTx((conn, tx) =>
        {
            var types = ReadSingleton<List<string>>(conn, tx, PlanTypesKey);
            var index = types.FindIndex(t => string.Equals(t, oldName, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return false;
            if (types.Any(t => string.Equals(t, newName, StringComparison.OrdinalIgnoreCase) && !string.Equals(t, oldName, StringComparison.OrdinalIgnoreCase))) return false;
            types[index] = newName;
            WriteSingleton(conn, tx, PlanTypesKey, types);
            // cascade: update every product plan that referenced the old type name.
            foreach (var row in conn.Query("SELECT Id, DataJson FROM Products", transaction: tx).ToList())
            {
                var p = Deserialize<Product>((string)row.DataJson)!;
                var touched = false;
                foreach (var plan in p.Plans)
                    if (string.Equals(plan.Type, oldName, StringComparison.OrdinalIgnoreCase)) { plan.Type = newName; touched = true; }
                if (touched) UpsertProduct(conn, tx, p);
            }
            return true;
        });
    }
    public bool RemovePlanType(string name)
    {
        name = (name ?? "").Trim();
        return WriteTx((conn, tx) =>
        {
            var types = ReadSingleton<List<string>>(conn, tx, PlanTypesKey);
            var existing = types.FirstOrDefault(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase));
            if (existing is null) return false;
            types.Remove(existing);
            WriteSingleton(conn, tx, PlanTypesKey, types);
            return true;
        });
    }

    // ── Tickets ─────────────────────────────────────────────────────────────────────────────────────────
    public IReadOnlyList<Ticket> GetTickets(TicketStatus? status = null)
    {
        var all = AllJson<Ticket>("Tickets").AsEnumerable();
        if (status is TicketStatus s) all = all.Where(t => t.Status == s);
        return all.OrderByDescending(t => t.Id).ToList();
    }
    public IReadOnlyList<Ticket> GetUserTickets(int userId) =>
        AllJson<Ticket>("Tickets").Where(t => t.UserId == userId).OrderByDescending(t => t.Id).ToList();
    public Ticket? GetTicket(int id) => OneJson<Ticket>("Tickets", id);

    public Ticket CreateTicket(int userId, string userName, string subject, string department, string body,
        TicketPriority priority = TicketPriority.Medium, string attachment = "")
    {
        var t = new Ticket
        {
            UserId = userId, UserName = userName, Subject = subject, Department = department, Priority = priority,
            Attachment = attachment ?? "", Status = TicketStatus.Open, Date = Today(),
        };
        InsertJson("Tickets", t, (x, id) => { x.Id = id; x.Code = $"T-{5800 + id}"; });
        // append the opening message and re-save (Code/Id were just assigned).
        t.Messages.Add(new TicketMessage { Author = userName, Body = body, IsAdmin = false, Date = Today() });
        UpdateJson("Tickets", t.Id, t);
        return t;
    }

    public Ticket CreateTicketForUser(int userId, string userName, string subject, string department, string body,
        string authorName, TicketPriority priority = TicketPriority.Medium, string attachment = "")
    {
        var t = new Ticket
        {
            UserId = userId, UserName = userName, Subject = subject, Department = department, Priority = priority,
            Status = TicketStatus.Answered, Date = Today(),
        };
        InsertJson("Tickets", t, (x, id) => { x.Id = id; x.Code = $"T-{5800 + id}"; });
        t.Messages.Add(new TicketMessage { Author = authorName, Body = body, IsAdmin = true, Date = Today(), Attachment = attachment ?? "" });
        UpdateJson("Tickets", t.Id, t);
        AddNotification(userId, "تیکت جدید از پشتیبانی", $"پشتیبانی فونیکس برای شما تیکت «{subject}» باز کرد.", "/account/tickets");
        return t;
    }

    public Ticket? ReplyTicket(int id, string author, string body, bool isAdmin, string? attachment = null)
    {
        var t = OneJson<Ticket>("Tickets", id);
        if (t is null) return null;
        t.Messages.Add(new TicketMessage { Author = author, Body = body, IsAdmin = isAdmin, Date = Today(), Attachment = attachment ?? "" });
        t.Status = isAdmin ? TicketStatus.Answered : TicketStatus.Open;
        UpdateJson("Tickets", id, t);
        if (isAdmin) AddNotification(t.UserId, "پاسخ تیکت پشتیبانی", $"به تیکت «{t.Subject}» پاسخ داده شد.", "/account/tickets");
        return t;
    }

    public bool SetTicketStatus(int id, TicketStatus status)
    {
        var t = OneJson<Ticket>("Tickets", id);
        if (t is null) return false;
        t.Status = status;
        return UpdateJson("Tickets", id, t);
    }

    // ── Live chat ───────────────────────────────────────────────────────────────────────────────────────
    private static string NowIso() => DateTime.UtcNow.ToString("o");

    public ChatConversation? GetUserConversation(int userId) =>
        AllJson<ChatConversation>("Conversations")
            .Where(c => c.UserId == userId && c.Status == ConversationStatus.Open)
            .OrderByDescending(c => c.LastMessageAtUtc).FirstOrDefault();

    public void CloseUserConversation(int userId) =>
        WriteTx<object?>((conn, tx) =>
        {
            foreach (var row in conn.Query("SELECT Id, DataJson FROM Conversations", transaction: tx).ToList())
            {
                var c = Deserialize<ChatConversation>((string)row.DataJson)!;
                if (c.UserId == userId && c.Status == ConversationStatus.Open)
                {
                    c.Status = ConversationStatus.Closed;
                    conn.Execute("UPDATE Conversations SET DataJson=@d WHERE Id=@id", new { d = Serialize(c), id = (long)row.Id }, tx);
                }
            }
            return null;
        });

    public ChatConversation? GetConversation(int id) => OneJson<ChatConversation>("Conversations", id);
    public IReadOnlyList<ChatConversation> GetConversations() =>
        AllJson<ChatConversation>("Conversations").OrderByDescending(c => c.LastMessageAtUtc).ToList();

    private static void AppendChatMessage(SqliteConnection conn, SqliteTransaction tx, ChatConversation conv, bool fromAdmin, string authorName, string body)
    {
        var msg = new ChatMessage { Id = NextCounter(conn, tx, "chatMessage"), FromAdmin = fromAdmin, AuthorName = authorName, Body = body, CreatedAtUtc = NowIso() };
        conv.Messages.Add(msg);
        conv.LastMessageAtUtc = msg.CreatedAtUtc;
        conv.Status = ConversationStatus.Open;
        if (fromAdmin) conv.AdminReadUpTo = msg.Id; else conv.UserReadUpTo = msg.Id;
    }

    public ChatConversation SendUserMessage(int userId, string userName, string body) =>
        WriteTx((conn, tx) =>
        {
            var rows = conn.Query("SELECT Id, DataJson FROM Conversations", transaction: tx).ToList();
            var mine = rows.Select(r => (Id: (long)r.Id, Conv: Deserialize<ChatConversation>((string)r.DataJson)!))
                .Where(x => x.Conv.UserId == userId).OrderByDescending(x => x.Conv.LastMessageAtUtc).ToList();
            var open = mine.FirstOrDefault(x => x.Conv.Status == ConversationStatus.Open);
            ChatConversation conv;
            long rowId;
            if (open.Conv is not null) { conv = open.Conv; rowId = open.Id; }
            else if (mine.Count > 0) { conv = mine[0].Conv; rowId = mine[0].Id; }
            else
            {
                conv = new ChatConversation { UserId = userId, UserName = userName, CreatedAtUtc = NowIso(), LastMessageAtUtc = NowIso() };
                rowId = conn.ExecuteScalar<long>("INSERT INTO Conversations (DataJson) VALUES (@d); SELECT last_insert_rowid();", new { d = Serialize(conv) }, tx);
                conv.Id = (int)rowId;
            }
            AppendChatMessage(conn, tx, conv, fromAdmin: false, userName, body);
            conn.Execute("UPDATE Conversations SET DataJson=@d WHERE Id=@id", new { d = Serialize(conv), id = rowId }, tx);
            return conv;
        });

    public ChatConversation? AddAdminMessage(int conversationId, string authorName, string body)
    {
        var result = WriteTx<(ChatConversation? Conv, int UserId)>((conn, tx) =>
        {
            var cj = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Conversations WHERE Id=@id", new { id = conversationId }, tx);
            if (cj is null) return (null, 0);
            var conv = Deserialize<ChatConversation>(cj)!;
            AppendChatMessage(conn, tx, conv, fromAdmin: true, authorName, body);
            conn.Execute("UPDATE Conversations SET DataJson=@d WHERE Id=@id", new { d = Serialize(conv), id = conversationId }, tx);
            return (conv, conv.UserId);
        });
        if (result.Conv is null) return null;
        AddNotification(result.UserId, "پاسخ پشتیبانی", "پشتیبانی به گفتگوی زنده‌ی شما پاسخ داد.", null);
        return result.Conv;
    }

    public void MarkConversationRead(int conversationId, bool byAdmin) =>
        WriteTx<object?>((conn, tx) =>
        {
            var cj = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Conversations WHERE Id=@id", new { id = conversationId }, tx);
            if (cj is null) return null;
            var conv = Deserialize<ChatConversation>(cj)!;
            var lastId = conv.Messages.Count == 0 ? 0 : conv.Messages.Max(m => m.Id);
            if (byAdmin) conv.AdminReadUpTo = lastId; else conv.UserReadUpTo = lastId;
            conn.Execute("UPDATE Conversations SET DataJson=@d WHERE Id=@id", new { d = Serialize(conv), id = conversationId }, tx);
            return null;
        });

    public bool CloseConversation(int id)
    {
        var conv = OneJson<ChatConversation>("Conversations", id);
        if (conv is null) return false;
        conv.Status = ConversationStatus.Closed;
        return UpdateJson("Conversations", id, conv);
    }

    public int CountUnreadForUser(int userId)
    {
        var conv = GetUserConversation(userId);
        return conv is null ? 0 : conv.Messages.Count(m => m.FromAdmin && m.Id > conv.UserReadUpTo);
    }
    public int UnreadChatsForAdmin() =>
        AllJson<ChatConversation>("Conversations").Count(c => c.Messages.Any(m => !m.FromAdmin && m.Id > c.AdminReadUpTo));
    public int UnreadMessagesForAdmin(ChatConversation conv) =>
        conv.Messages.Count(m => !m.FromAdmin && m.Id > conv.AdminReadUpTo);

    // ── Orders: remaining ───────────────────────────────────────────────────────────────────────────────
    public void RefreshAllUserOrderStats() =>
        WriteTx<object?>((conn, tx) =>
        {
            foreach (var uid in conn.Query<int>("SELECT Id FROM Users", transaction: tx).ToList())
                RefreshUserStats(conn, tx, uid);
            return null;
        });

    public Order? SaveUnitDraft(int orderId, int unitId, string content, string? changedBy = null) =>
        WriteTx<Order?>((conn, tx) =>
        {
            var oj = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Orders WHERE Id=@orderId", new { orderId }, tx);
            if (oj is null) return null;
            var o = Deserialize<Order>(oj)!;
            var unit = o.Units.FirstOrDefault(u => u.Id == unitId);
            if (unit is null || unit.Delivered) return null;
            unit.DeliveryContent = content; unit.HandledBy = changedBy;
            UpsertOrder(conn, tx, o);
            return o;
        });

    public (Order? order, bool justCompleted) DeliverUnit(int orderId, int unitId, string content, string? changedBy = null) =>
        WriteTx<(Order?, bool)>((conn, tx) =>
        {
            var oj = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Orders WHERE Id=@orderId", new { orderId }, tx);
            if (oj is null) return (null, false);
            var o = Deserialize<Order>(oj)!;
            var unit = o.Units.FirstOrDefault(u => u.Id == unitId);
            if (unit is null) return (null, false);

            unit.DeliveryContent = content; unit.HandledBy = changedBy;
            if (!unit.Delivered) { unit.Delivered = true; unit.DeliveredAt = Today(); unit.DeliveredAtUtc = DateTime.UtcNow; }

            var justCompleted = false;
            if (o.Units.Count > 0 && o.Units.All(u => u.Delivered) && o.Status != OrderStatus.Completed)
            {
                var from = o.Status;
                o.DeliveryContent = string.Join("\n\n", o.Units.OrderBy(u => u.UnitIndex)
                    .Select(u => o.Units.Count > 1 ? $"اکانت {u.UnitIndex}:\n{u.DeliveryContent}" : u.DeliveryContent));
                o.DeliveredAt = Today(); o.DeliveredAtUtc ??= DateTime.UtcNow; o.Status = OrderStatus.Completed;
                CreditReferralTx(conn, tx, o, ReadSingleton<PricingSettings>(conn, tx, PricingKey));
                AppendOrderHistory(o, from, OrderStatus.Completed, changedBy, "تحویل همه‌ی اکانت‌ها");
                UpsertOrder(conn, tx, o);
                RefreshUserStats(conn, tx, o.UserId);
                AddNotificationTx(conn, tx, o.UserId, "سفارش شما آماده شد", $"سفارش {o.Code} آماده و قابل مشاهده در حساب شماست.", "/account/orders");
                justCompleted = true;
            }
            else UpsertOrder(conn, tx, o);
            return (o, justCompleted);
        });

    public IReadOnlyList<RenewalReminder> CollectDueRenewalReminders(int hoursBefore)
    {
        var due = new List<RenewalReminder>();
        if (hoursBefore <= 0) return due;
        WriteTx<object?>((conn, tx) =>
        {
            var now = DateTime.UtcNow;
            var window = TimeSpan.FromHours(hoursBefore);
            foreach (var row in conn.Query("SELECT Id, DataJson FROM Orders WHERE Status=@s", new { s = (int)OrderStatus.Completed }, tx).ToList())
            {
                var o = Deserialize<Order>((string)row.DataJson)!;
                if (o.DeliveredAtUtc is not DateTime delivered) continue;
                if (o.RenewalReminderSentUtc is not null) continue;
                var months = o.Items.Where(i => i.PlanMonths is int m && m > 0).Select(i => i.PlanMonths!.Value).DefaultIfEmpty(0).Max();
                if (months <= 0) continue;
                var expires = delivered.AddMonths(months);
                var remaining = expires - now;
                if (remaining <= TimeSpan.Zero || remaining > window) continue;

                var user = LoadUser(conn, tx, o.UserId);
                if (user is null) continue;

                o.RenewalReminderSentUtc = now;
                var expiresFa = JalaliDate(expires);
                conn.Execute("UPDATE Orders SET DataJson=@d WHERE Id=@id", new { d = Serialize(o), id = (long)row.Id }, tx);
                AddNotificationTx(conn, tx, user.Id, "یادآوری تمدید اشتراک",
                    $"اشتراک سفارش {o.Code} شما در تاریخ {expiresFa} منقضی می‌شود. برای جلوگیری از قطع سرویس، آن را تمدید کنید.", "/account/orders");
                due.Add(new RenewalReminder(user.Id, user.Email, o.Code, expiresFa));
            }
            return null;
        });
        return due;
    }

    public IReadOnlyList<ReferralEarning> GetReferralEarnings(int referrerId)
    {
        using var conn = OpenConnection();
        return conn.Query<string>("SELECT DataJson FROM ReferralEarnings WHERE ReferrerId=@referrerId", new { referrerId })
            .Select(j => Deserialize<ReferralEarning>(j)!).OrderByDescending(e => e.Date).ToList();
    }

    public int CountReferredUsers(int referrerId)
    {
        using var conn = OpenConnection();
        return conn.ExecuteScalar<int>("SELECT COUNT(1) FROM Users WHERE ReferredBy=@referrerId", new { referrerId });
    }

    private static string JalaliDate(DateTime dt)
    {
        var pc = new System.Globalization.PersianCalendar();
        var s = $"{pc.GetYear(dt):0000}/{pc.GetMonth(dt):00}/{pc.GetDayOfMonth(dt):00}";
        return new string(s.Select(ch => char.IsDigit(ch) ? (char)('۰' + (ch - '0')) : ch).ToArray());
    }

    // ── Backup log (in-memory ring, like the JSON store) ────────────────────────────────────────────────
    private readonly List<BackupLogEntry> _backupLog = new();
    public void RecordBackup(string section, string target, bool ok, string error)
    {
        lock (_backupLog)
        {
            _backupLog.Insert(0, new BackupLogEntry(section, target, ok, error, DateTime.UtcNow));
            if (_backupLog.Count > 100) _backupLog.RemoveRange(100, _backupLog.Count - 100);
        }
    }
    public IReadOnlyList<BackupLogEntry> GetBackupLog() { lock (_backupLog) return _backupLog.ToList(); }

    // Writes are durable on COMMIT (WAL); a passive checkpoint keeps the WAL from growing unbounded.
    public void Save() { using var conn = OpenConnection(); conn.Execute("PRAGMA wal_checkpoint(PASSIVE);"); }

    public StoreSnapshot CaptureSnapshot()
    {
        using var conn = OpenConnection();
        List<T> All<T>(string table) =>
            conn.Query<string>($"SELECT DataJson FROM {table} ORDER BY Id").Select(j => Deserialize<T>(j)!).ToList();

        var snap = new StoreSnapshot
        {
            Categories = All<Category>("Categories"),
            Products = All<Product>("Products"),
            Users = All<AppUser>("Users"),
            Plans = All<SubscriptionPlan>("Plans"),
            HeroSlides = All<HeroSlide>("HeroSlides"),
            HomeCategories = All<HomeCategory>("HomeCategories"),
            Showcase = All<Showcase>("Showcase"),
            BlogPosts = All<BlogPost>("BlogPosts"),
            Comments = All<Comment>("Comments"),
            PaymentMethods = All<PaymentMethod>("PaymentMethods"),
            Transactions = All<Transaction>("Transactions"),
            Cards = All<BankCard>("Cards"),
            Kyc = All<KycRequest>("Kyc"),
            Orders = All<Order>("Orders"),
            Tickets = All<Ticket>("Tickets"),
            Notifications = All<Notification>("Notifications"),
            Conversations = All<ChatConversation>("Conversations"),
            // ReferralEarnings has no Id column — order by rowid instead.
            ReferralEarnings = conn.Query<string>("SELECT DataJson FROM ReferralEarnings ORDER BY rowid")
                .Select(j => Deserialize<ReferralEarning>(j)!).ToList(),
            DiscountCodes = All<DiscountCode>("DiscountCodes"),
            PlanTypes = GetSingleton<List<string>>(PlanTypesKey),
            Favorites = GetSingleton<Dictionary<int, List<int>>>(FavoritesKey),
            Settings = GetSettings(),
            SiteContent = GetSiteContent(),
            AdvancedSettings = GetAdvancedSettings(),
            PaymentSettings = GetPaymentSettings(),
            EmailSettings = GetEmailSettings(),
            TelegramSettings = GetTelegramSettings(),
            Seq = new StoreSnapshot.SeqState
            {
                Category = MaxId(conn, "Categories"),
                Product = MaxId(conn, "Products"),
                User = MaxId(conn, "Users"),
                Plan = MaxId(conn, "Plans"),
                Hero = MaxId(conn, "HeroSlides"),
                HomeCat = MaxId(conn, "HomeCategories"),
                Showcase = MaxId(conn, "Showcase"),
                Blog = MaxId(conn, "BlogPosts"),
                Comment = MaxId(conn, "Comments"),
                Payment = MaxId(conn, "PaymentMethods"),
                Tx = MaxId(conn, "Transactions"),
                Card = MaxId(conn, "Cards"),
                Kyc = MaxId(conn, "Kyc"),
                Order = MaxId(conn, "Orders"),
                Ticket = MaxId(conn, "Tickets"),
                Notification = MaxId(conn, "Notifications"),
                Discount = MaxId(conn, "DiscountCodes"),
                Conversation = MaxId(conn, "Conversations"),
                ChatMessage = conn.ExecuteScalar<int?>("SELECT Value FROM Counters WHERE Name='chatMessage'") ?? 0,
            },
        };
        return snap;
    }

    public string SerializeSnapshot() => JsonSerializer.Serialize(CaptureSnapshot(), SnapshotJson);

    public StoreSnapshot? DeserializeSnapshot(string json) => JsonSerializer.Deserialize<StoreSnapshot>(json, SnapshotJson);

    // Replaces the durable contents with a snapshot — atomically (one IMMEDIATE transaction): the whole import
    // commits or nothing does, so a failed/partial restore can never leave a half-loaded database.
    public void LoadSnapshot(StoreSnapshot s) =>
        WriteTxNoFk<object?>((conn, tx) =>
        {
            conn.Execute(@"
DELETE FROM Users; DELETE FROM Products; DELETE FROM Orders; DELETE FROM Transactions;
DELETE FROM Cards; DELETE FROM DiscountCodes; DELETE FROM PaymentMethods;
DELETE FROM ReferralEarnings; DELETE FROM Notifications; DELETE FROM Categories;
DELETE FROM Plans; DELETE FROM HeroSlides; DELETE FROM HomeCategories; DELETE FROM Showcase;
DELETE FROM BlogPosts; DELETE FROM Comments; DELETE FROM Kyc; DELETE FROM Tickets;
DELETE FROM Conversations; DELETE FROM Counters;", transaction: tx);

            // hybrid-column tables use their typed upserts/inserts (Id preserved)
            foreach (var u in s.Users) UpsertUser(conn, tx, u);
            foreach (var p in s.Products) UpsertProduct(conn, tx, p);
            foreach (var o in s.Orders) UpsertOrder(conn, tx, o);
            foreach (var t in s.Transactions)
                conn.Execute("INSERT INTO Transactions (Id, UserId, Status, Date, DataJson) VALUES (@Id,@UserId,@Status,@Date,@DataJson)",
                    new { t.Id, t.UserId, Status = (int)t.Status, t.Date, DataJson = Serialize(t) }, tx);
            foreach (var c in s.Cards)
                conn.Execute("INSERT INTO Cards (Id, UserId, Status, DataJson) VALUES (@Id,@UserId,@Status,@DataJson)",
                    new { c.Id, c.UserId, Status = (int)c.Status, DataJson = Serialize(c) }, tx);
            foreach (var d in s.DiscountCodes)
                conn.Execute("INSERT INTO DiscountCodes (Id, Code, DataJson) VALUES (@Id,@Code,@DataJson)",
                    new { d.Id, d.Code, DataJson = Serialize(d) }, tx);
            foreach (var m in s.PaymentMethods)
                conn.Execute("INSERT INTO PaymentMethods (Id, DataJson) VALUES (@Id,@DataJson)", new { m.Id, DataJson = Serialize(m) }, tx);
            foreach (var r in s.ReferralEarnings)
                conn.Execute("INSERT INTO ReferralEarnings (ReferrerId, DataJson) VALUES (@ReferrerId,@DataJson)", new { r.ReferrerId, DataJson = Serialize(r) }, tx);
            foreach (var n in s.Notifications)
                conn.Execute("INSERT INTO Notifications (Id, UserId, DataJson) VALUES (@Id,@UserId,@DataJson)", new { n.Id, n.UserId, DataJson = Serialize(n) }, tx);

            // simple id-keyed JSON tables
            void Ins<T>(string table, int id, T obj) => conn.Execute($"INSERT INTO {table} (Id, DataJson) VALUES (@id,@d)", new { id, d = Serialize(obj) }, tx);
            foreach (var c in s.Categories) Ins("Categories", c.Id, c);
            foreach (var p in s.Plans) Ins("Plans", p.Id, p);
            foreach (var h in s.HeroSlides) Ins("HeroSlides", h.Id, h);
            foreach (var h in s.HomeCategories) Ins("HomeCategories", h.Id, h);
            foreach (var sh in s.Showcase) Ins("Showcase", sh.Id, sh);
            foreach (var b in s.BlogPosts) Ins("BlogPosts", b.Id, b);
            foreach (var cm in s.Comments) Ins("Comments", cm.Id, cm);
            foreach (var k in s.Kyc) Ins("Kyc", k.Id, k);
            foreach (var tk in s.Tickets) Ins("Tickets", tk.Id, tk);
            foreach (var cv in s.Conversations) Ins("Conversations", cv.Id, cv);

            // restore the global chat-message counter so new messages keep unique ids
            conn.Execute("INSERT INTO Counters (Name, Value) VALUES ('chatMessage', @v) ON CONFLICT(Name) DO UPDATE SET Value=@v",
                new { v = s.Seq.ChatMessage }, tx);

            // singletons / settings
            WriteSingleton(conn, tx, PricingKey, s.Settings);
            WriteSingleton(conn, tx, PaymentKey, s.PaymentSettings);
            WriteSingleton(conn, tx, SiteContentKey, s.SiteContent);
            WriteSingleton(conn, tx, AdvancedKey, s.AdvancedSettings);
            WriteSingleton(conn, tx, EmailKey, s.EmailSettings);
            WriteSingleton(conn, tx, TelegramKey, s.TelegramSettings);
            WriteSingleton(conn, tx, PlanTypesKey, s.PlanTypes);
            WriteSingleton(conn, tx, FavoritesKey, s.Favorites);
            return null;
        });

    private static void InsRow<T>(SqliteConnection conn, SqliteTransaction tx, string table, int id, T obj) =>
        conn.Execute($"INSERT INTO {table} (Id, DataJson) VALUES (@id, @d)", new { id, d = Serialize(obj) }, tx);

    // ── Per-section backup (each domain exported / restored on its own — small Telegram-friendly files) ──
    // Mirrors StoreData's section→collections mapping EXACTLY, so a partial file taken from either backend
    // restores into the other. A section snapshot carries only its own collections plus the Section marker the
    // backup controller verifies before restoring.
    public string SerializeSection(BackupSection section)
    {
        var s = new StoreSnapshot { Section = section.ToString() };
        switch (section)
        {
            case BackupSection.Catalog:
                s.Categories = GetCategories().ToList();
                s.Products = GetProducts().ToList();
                s.Plans = GetPlans().ToList();
                s.PlanTypes = GetPlanTypes().ToList();
                s.DiscountCodes = GetDiscountCodes().ToList();
                break;
            case BackupSection.Content:
                s.HeroSlides = GetHeroSlides().ToList();
                s.HomeCategories = GetHomeCategories().ToList();
                s.Showcase = GetShowcase().ToList();
                s.BlogPosts = GetBlogPosts().ToList();
                s.SiteContent = GetSiteContent();
                s.Settings = GetSettings();
                break;
            case BackupSection.Users:
                s.Users = GetUsers().ToList();
                s.Cards = GetAllCards().ToList();
                s.Kyc = GetAllKyc().ToList();
                s.ReferralEarnings = AllJson<ReferralEarning>("ReferralEarnings");
                s.Favorites = GetSingleton<Dictionary<int, List<int>>>(FavoritesKey);
                break;
            case BackupSection.Commerce:
                s.Orders = GetOrders().ToList();
                s.Transactions = GetTransactions().ToList();
                s.PaymentMethods = GetPaymentMethods().ToList();
                s.PaymentSettings = GetPaymentSettings();
                break;
            case BackupSection.Support:
                s.Tickets = GetTickets().ToList();
                s.Comments = GetComments().ToList();
                s.Notifications = GetAllNotifications().ToList();
                s.Conversations = GetConversations().ToList();
                break;
            case BackupSection.System:
                s.EmailSettings = GetEmailSettings();
                s.TelegramSettings = GetTelegramSettings();
                s.AdvancedSettings = GetAdvancedSettings();
                break;
        }
        return JsonSerializer.Serialize(s, SnapshotJson);
    }

    // Replaces ONLY the given section's tables/singletons from a partial snapshot; every other domain is left
    // untouched. Runs in one FK-disabled IMMEDIATE transaction (a Users restore drops user rows that live
    // Transactions still reference), so the swap is atomic and never trips the nominal foreign keys.
    public void RestoreSection(BackupSection section, StoreSnapshot s) =>
        WriteTxNoFk<object?>((conn, tx) =>
        {
            switch (section)
            {
                case BackupSection.Catalog:
                    conn.Execute("DELETE FROM Categories; DELETE FROM Products; DELETE FROM Plans; DELETE FROM DiscountCodes;", transaction: tx);
                    foreach (var c in s.Categories) InsRow(conn, tx, "Categories", c.Id, c);
                    foreach (var p in s.Products) UpsertProduct(conn, tx, p);
                    foreach (var p in s.Plans) InsRow(conn, tx, "Plans", p.Id, p);
                    foreach (var d in s.DiscountCodes)
                        conn.Execute("INSERT INTO DiscountCodes (Id, Code, DataJson) VALUES (@Id,@Code,@DataJson)",
                            new { d.Id, d.Code, DataJson = Serialize(d) }, tx);
                    WriteSingleton(conn, tx, PlanTypesKey, s.PlanTypes);
                    break;
                case BackupSection.Content:
                    conn.Execute("DELETE FROM HeroSlides; DELETE FROM HomeCategories; DELETE FROM Showcase; DELETE FROM BlogPosts;", transaction: tx);
                    foreach (var h in s.HeroSlides) InsRow(conn, tx, "HeroSlides", h.Id, h);
                    foreach (var h in s.HomeCategories) InsRow(conn, tx, "HomeCategories", h.Id, h);
                    foreach (var sh in s.Showcase) InsRow(conn, tx, "Showcase", sh.Id, sh);
                    foreach (var b in s.BlogPosts) InsRow(conn, tx, "BlogPosts", b.Id, b);
                    WriteSingleton(conn, tx, SiteContentKey, s.SiteContent);
                    WriteSingleton(conn, tx, PricingKey, s.Settings);
                    break;
                case BackupSection.Users:
                    conn.Execute("DELETE FROM Users; DELETE FROM Cards; DELETE FROM Kyc; DELETE FROM ReferralEarnings;", transaction: tx);
                    foreach (var u in s.Users) UpsertUser(conn, tx, u);
                    foreach (var c in s.Cards)
                        conn.Execute("INSERT INTO Cards (Id, UserId, Status, DataJson) VALUES (@Id,@UserId,@Status,@DataJson)",
                            new { c.Id, c.UserId, Status = (int)c.Status, DataJson = Serialize(c) }, tx);
                    foreach (var k in s.Kyc) InsRow(conn, tx, "Kyc", k.Id, k);
                    foreach (var r in s.ReferralEarnings)
                        conn.Execute("INSERT INTO ReferralEarnings (ReferrerId, DataJson) VALUES (@ReferrerId,@DataJson)",
                            new { r.ReferrerId, DataJson = Serialize(r) }, tx);
                    WriteSingleton(conn, tx, FavoritesKey, s.Favorites);
                    break;
                case BackupSection.Commerce:
                    conn.Execute("DELETE FROM Orders; DELETE FROM Transactions; DELETE FROM PaymentMethods;", transaction: tx);
                    foreach (var o in s.Orders) UpsertOrder(conn, tx, o);
                    foreach (var t in s.Transactions)
                        conn.Execute("INSERT INTO Transactions (Id, UserId, Status, Date, DataJson) VALUES (@Id,@UserId,@Status,@Date,@DataJson)",
                            new { t.Id, t.UserId, Status = (int)t.Status, t.Date, DataJson = Serialize(t) }, tx);
                    foreach (var m in s.PaymentMethods)
                        conn.Execute("INSERT INTO PaymentMethods (Id, DataJson) VALUES (@Id,@DataJson)", new { m.Id, DataJson = Serialize(m) }, tx);
                    WriteSingleton(conn, tx, PaymentKey, s.PaymentSettings);
                    break;
                case BackupSection.Support:
                    conn.Execute("DELETE FROM Tickets; DELETE FROM Comments; DELETE FROM Notifications; DELETE FROM Conversations;", transaction: tx);
                    foreach (var tk in s.Tickets) InsRow(conn, tx, "Tickets", tk.Id, tk);
                    foreach (var cm in s.Comments) InsRow(conn, tx, "Comments", cm.Id, cm);
                    foreach (var n in s.Notifications)
                        conn.Execute("INSERT INTO Notifications (Id, UserId, DataJson) VALUES (@Id,@UserId,@DataJson)",
                            new { n.Id, n.UserId, DataJson = Serialize(n) }, tx);
                    foreach (var cv in s.Conversations) InsRow(conn, tx, "Conversations", cv.Id, cv);
                    // keep the global chat-message counter at least at the highest restored message id, so new
                    // messages never reuse an id (mirrors StoreData.RecomputeSeqFromData for the chat counter).
                    var maxMsg = s.Conversations.SelectMany(c => c.Messages).Select(m => m.Id).DefaultIfEmpty(0).Max();
                    conn.Execute("INSERT INTO Counters (Name, Value) VALUES ('chatMessage', @v) ON CONFLICT(Name) DO UPDATE SET Value = MAX(Value, @v)",
                        new { v = maxMsg }, tx);
                    break;
                case BackupSection.System:
                    WriteSingleton(conn, tx, EmailKey, s.EmailSettings);
                    WriteSingleton(conn, tx, TelegramKey, s.TelegramSettings);
                    WriteSingleton(conn, tx, AdvancedKey, s.AdvancedSettings);
                    break;
            }
            return null;
        });

    // ── Live, consistent single-file backup (for the Telegram bot) ──────────────────────────────────────
    // `VACUUM INTO` writes a transactionally-consistent, defragmented COPY of the whole database to one file,
    // taken as of a read snapshot. Under WAL it does NOT block writers (they keep appending to the WAL) and
    // needs no app downtime — so the backup worker can grab a clean `.db` while the server is live. The
    // produced file is a normal SQLite database the bot can ship as-is and that restores by simply opening it.
    public string BackupToFile(string destPath)
    {
        var full = Path.GetFullPath(destPath);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        if (File.Exists(full)) File.Delete(full); // VACUUM INTO requires the target not to exist

        using var conn = OpenConnection();
        conn.Execute($"VACUUM INTO '{full.Replace("'", "''")}'");
        return full;
    }
}

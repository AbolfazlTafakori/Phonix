using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Data;

// Users: CRUD, register/login lookups, rename/email, staff, auth, 2FA, one-time tokens.
// Partial of SqliteDataStore -- split by domain the same way the JSON StoreData is (StoreOrders.cs etc.).
public sealed partial class SqliteDataStore
{
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
}

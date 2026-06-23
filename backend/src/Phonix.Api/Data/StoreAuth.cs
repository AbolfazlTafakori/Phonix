using System.Security.Cryptography;
using Phonix.Api.Models;

namespace Phonix.Api.Data;

public record StaffResult(AppUser? User, string? Error);

public partial class StoreData
{
    // Grants staff access to an EXISTING account, looked up by its username. The admin only supplies the
    // username (plus role + granted sections) — never a new email or password, because the person already
    // has their own account. A Support member carries the granted section permissions; an Admin ignores
    // them (full access). Rotating the security stamp forces the account to re-authenticate so the new role
    // and its elevated cookie take effect on the next sign-in.
    public StaffResult PromoteToStaff(string username, UserRole role, IEnumerable<string> permissions)
    {
        StaffResult result;
        lock (_gate)
        {
            var u = (username ?? "").Trim();
            if (string.IsNullOrWhiteSpace(u))
                return new StaffResult(null, "نام کاربری را وارد کنید.");
            var user = _users.FirstOrDefault(x => string.Equals(x.Username, u, StringComparison.OrdinalIgnoreCase));
            if (user is null)
                return new StaffResult(null, "کاربری با این نام کاربری یافت نشد.");
            if (user.Role != UserRole.Customer)
                return new StaffResult(null, "این حساب از قبل دسترسی کارمندی دارد.");

            user.Role = role;
            user.Permissions = role == UserRole.Support ? permissions.Distinct().ToList() : new();
            user.SecurityStamp = NewStamp();
            result = new StaffResult(user, null);
        }
        PersistNow();
        return result;
    }

    // Replaces a user's granted section permissions (only meaningful for a Support account).
    public bool SetUserPermissions(int userId, IEnumerable<string> permissions)
    {
        bool ok;
        lock (_gate)
        {
            var user = _users.FirstOrDefault(u => u.Id == userId);
            if (user is null) { ok = false; }
            else { user.Permissions = permissions.Distinct().ToList(); ok = true; }
        }
        if (ok) PersistNow();
        return ok;
    }

    // A fresh per-user session nonce. Sessions are now stateless (encrypted cookies validated via Data
    // Protection — see ISessionProtector), so there is no server-side session table to clear; rotating a
    // user's stamp is what invalidates all of their outstanding cookies at once.
    public static string NewStamp() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    // Returns the new stamp so the caller can re-issue a fresh cookie for the current device if it wants
    // to keep that one session alive (e.g. on a self-service password change).
    public string RotateSecurityStamp(int userId)
    {
        string stamp = NewStamp();
        lock (_gate)
        {
            var user = _users.FirstOrDefault(u => u.Id == userId);
            if (user is null) return "";
            user.SecurityStamp = stamp;
        }
        PersistNow(); // durable so the rotation can't be undone by a restart reloading the old stamp.
        return stamp;
    }

    // Provisions (or replaces) a pending TOTP secret without activating it; 2FA stays off until the owner
    // confirms a code via SetTwoFactorEnabled. Durable so a restart can't lose a half-finished setup.
    public bool SetTwoFactorSecret(int userId, string secret)
    {
        bool ok;
        lock (_gate)
        {
            var user = _users.FirstOrDefault(u => u.Id == userId);
            if (user is null) { ok = false; }
            else { user.TwoFactorSecret = secret; user.TwoFactorEnabled = false; ok = true; }
        }
        if (ok) PersistNow();
        return ok;
    }

    // Turns 2FA on or off. Disabling clears the secret so a fresh setup is required to re-enable.
    public bool SetTwoFactorEnabled(int userId, bool enabled)
    {
        bool ok;
        lock (_gate)
        {
            var user = _users.FirstOrDefault(u => u.Id == userId);
            if (user is null) { ok = false; }
            else
            {
                user.TwoFactorEnabled = enabled;
                if (!enabled) user.TwoFactorSecret = "";
                ok = true;
            }
        }
        if (ok) PersistNow();
        return ok;
    }

    private sealed class OneTimeToken
    {
        public int UserId { get; init; }
        public string Purpose { get; init; } = "";
        public DateTime ExpiresAt { get; init; }
    }

    // single-use tokens for email verification and password reset (not persisted; resend if lost).
    private readonly Dictionary<string, OneTimeToken> _tokens = new();

    public string CreateToken(int userId, string purpose, TimeSpan lifetime)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        lock (_gate) _tokens[token] = new OneTimeToken { UserId = userId, Purpose = purpose, ExpiresAt = DateTime.UtcNow + lifetime };
        return token;
    }

    // validates and consumes a token, returning its user id (or null if invalid/expired).
    public int? ConsumeToken(string? token, string purpose)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        lock (_gate)
        {
            if (!_tokens.TryGetValue(token, out var entry)) return null;
            if (entry.Purpose != purpose || entry.ExpiresAt <= DateTime.UtcNow)
            {
                _tokens.Remove(token);
                return null;
            }
            _tokens.Remove(token);
            return entry.UserId;
        }
    }
}

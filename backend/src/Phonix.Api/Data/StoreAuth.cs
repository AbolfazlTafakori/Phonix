using System.Security.Cryptography;

namespace Phonix.Api.Data;

public partial class StoreData
{
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

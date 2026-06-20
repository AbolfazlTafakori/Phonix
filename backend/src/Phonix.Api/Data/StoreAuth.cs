using System.Security.Cryptography;
using Phonix.Api.Models;

namespace Phonix.Api.Data;

public partial class StoreData
{
    private sealed class Session
    {
        public int UserId { get; init; }
        public DateTime ExpiresAt { get; set; }
    }

    // sessions expire after this much inactivity; each successful use slides the window.
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(3);

    // opaque bearer token -> session. tokens are random and only resolvable server-side.
    private readonly Dictionary<string, Session> _sessions = new();

    public string CreateSession(AppUser user)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        lock (_gate) _sessions[token] = new Session { UserId = user.Id, ExpiresAt = DateTime.UtcNow + SessionLifetime };
        return token;
    }

    public AppUser? ResolveSession(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(token, out var session)) return null;

            var user = _users.FirstOrDefault(u => u.Id == session.UserId);
            // a blocked, deleted, or expired token is no longer valid.
            if (user is null || user.Blocked || session.ExpiresAt <= DateTime.UtcNow)
            {
                _sessions.Remove(token);
                return null;
            }

            session.ExpiresAt = DateTime.UtcNow + SessionLifetime;
            return user;
        }
    }

    public void RemoveSession(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        lock (_gate) _sessions.Remove(token);
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

using System.Security.Cryptography;
using System.Text;

namespace Phonix.Api.Services;

// Authenticates every inter-node HTTP call between a Primary and Standby with a shared secret
// (PHONIX_CLUSTER_SECRET) — HMAC-SHA256 over the request, not a session cookie, since the caller is a peer
// server, not a logged-in staff member. Same shape as BackupCrypto: a static class reading its key straight
// from the environment, so an install with clustering off never even has the secret configured.
public static class ClusterAuth
{
    public const string TimestampHeader = "X-Cluster-Timestamp";
    public const string SignatureHeader = "X-Cluster-Signature";

    // Generous enough for real network latency + modest clock drift between two independent servers, tight
    // enough that a captured request can't be replayed hours later.
    private static readonly TimeSpan ReplayWindow = TimeSpan.FromMinutes(5);

    private static string? Secret => Environment.GetEnvironmentVariable("PHONIX_CLUSTER_SECRET") is { } s && !string.IsNullOrWhiteSpace(s) ? s : null;

    public static bool IsConfigured => Secret is not null;

    private static string Sign(string secret, string method, string path, string timestamp, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var bodyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
        var message = $"{method.ToUpperInvariant()}\n{path}\n{timestamp}\n{bodyHash}";
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(message)));
    }

    // Called by the node MAKING a request (ClusterSyncService) to fill in both headers.
    public static (string Timestamp, string Signature)? SignRequest(string method, string path, string body)
    {
        if (Secret is not { } secret) return null;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        return (timestamp, Sign(secret, method, path, timestamp, body));
    }

    // Called by the node RECEIVING a request (ClusterPeerAuthAttribute) to verify both headers.
    public static bool Verify(string method, string path, string body, string? timestampHeader, string? signatureHeader)
    {
        if (Secret is not { } secret) return false;
        if (string.IsNullOrEmpty(timestampHeader) || string.IsNullOrEmpty(signatureHeader)) return false;
        if (!long.TryParse(timestampHeader, out var unixSeconds)) return false;

        var when = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        if ((DateTimeOffset.UtcNow - when).Duration() > ReplayWindow) return false;

        var expected = Sign(secret, method, path, timestampHeader, body);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(signatureHeader));
    }
}

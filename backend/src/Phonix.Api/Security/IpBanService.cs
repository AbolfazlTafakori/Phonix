using Microsoft.Extensions.Caching.Memory;

namespace Phonix.Api.Security;

// Short-lived IP bans (e.g. honeypot hits). Backed by IMemoryCache so entries expire on their own and
// never grow store.json; bans are intentionally ephemeral and reset on restart, like sessions.
public sealed class IpBanService
{
    private readonly IMemoryCache _cache;

    public IpBanService(IMemoryCache cache) => _cache = cache;

    private static string Key(string ip) => $"ipban:{ip}";

    public void Ban(string ip, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(ip)) return;
        _cache.Set(Key(ip), DateTimeOffset.UtcNow.Add(duration), duration);
    }

    public bool IsBanned(string ip) =>
        !string.IsNullOrWhiteSpace(ip) && _cache.TryGetValue(Key(ip), out _);
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;
using Phonix.Api.Services;

namespace Phonix.Api.Controllers;

// What the config page shows. Everything here is about ONE account: no order code, no customer, no panel
// address or credential — the buyer may pass this link to whoever the service is actually for, so it must
// carry nothing about the purchase or the infrastructure behind it.
public sealed record V2RayConfigLineDto(string Uri, string Remark, string Protocol, string Network);

public sealed record V2RayConfigDto(
    string Name, string Uuid, string Server, string Flag, string Protocol, string Network,
    string SubUrl, string SubId,
    long UsedBytes, long UpBytes, long DownBytes, long TotalBytes,
    int IpLimit, bool Online,
    int? RemainingDays, DateTime? ExpiresAtUtc, DateTime? CreatedAtUtc,
    bool Active, bool StatsLive,
    IReadOnlyList<V2RayConfigLineDto> Configs);

// The customer-facing view of one provisioned V2Ray account, addressed only by its unguessable token.
// Anonymous on purpose: the buyer often provisions for someone else (a colleague, a family member) and needs
// a link they can simply hand over.
[ApiController]
[Route("api/v2ray/config")]
[AllowAnonymous]
public class V2RayConfigController : ControllerBase
{
    // The page polls, and the link is meant to be shared — several people refreshing one service must not
    // turn into a request per refresh against the subscription server. A few seconds is short enough that
    // usage still reads as live and long enough to absorb a burst.
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(10);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime At, V2RaySubscription Sub)> Cache = new();

    private readonly IDataStore _store;
    private readonly IV2RayPanelConnector _connector;

    public V2RayConfigController(IDataStore store, IV2RayPanelConnector connector)
    {
        _store = store;
        _connector = connector;
    }

    // One entry per service would otherwise accumulate for the life of the process, so expired entries are
    // swept once the map grows past a shop's worth of active services.
    private const int CacheLimit = 500;

    private async Task<V2RaySubscription> ReadSubscriptionAsync(string subUrl, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        if (Cache.TryGetValue(subUrl, out var hit) && now - hit.At < CacheFor) return hit.Sub;

        var sub = await _connector.GetSubscriptionAsync(subUrl, ct);
        // A failed read isn't cached: the next visitor should get a fresh attempt rather than a stuck error.
        if (!sub.Ok) return sub;

        if (Cache.Count >= CacheLimit)
            foreach (var stale in Cache.Where(e => now - e.Value.At >= CacheFor).Select(e => e.Key).ToList())
                Cache.TryRemove(stale, out _);

        Cache[subUrl] = (now, sub);
        return sub;
    }

    [HttpGet("{token}")]
    public async Task<IActionResult> Get(string token, CancellationToken ct)
    {
        var found = _store.FindUnitByV2RayToken(token);
        if (found is not var (_, unit) || unit.V2Ray is null) return NotFound();
        var account = unit.V2Ray;

        var panel = _store.GetV2RayPanel(account.PanelId);
        var server = panel is null || string.IsNullOrWhiteSpace(panel.Name) ? "سرور" : panel.Name.Trim();

        // The subscription link is the source of truth the customer's own app polls — usage to the byte AND
        // the config list — with no panel login. A link that is briefly down must not take the whole page
        // with it: the plan's own terms are still worth showing, flagged as not live.
        var sub = string.IsNullOrWhiteSpace(account.SubUrl)
            ? V2RaySubscription.Fail("لینک اشتراک تنظیم نشده است.")
            : await ReadSubscriptionAsync(account.SubUrl, ct);

        var totalBytes = sub.Ok && sub.Total > 0 ? sub.Total : account.VolumeGb * 1024L * 1024L * 1024L;
        var expiresAt = sub.Ok && sub.ExpireUnix > 0
            ? DateTimeOffset.FromUnixTimeSeconds(sub.ExpireUnix).UtcDateTime
            : account.ExpiresAtUtc;
        var remainingDays = expiresAt is DateTime e
            ? Math.Max(0, (int)Math.Ceiling((e - DateTime.UtcNow).TotalDays))
            : (int?)null;

        var configs = (sub.Configs ?? Array.Empty<V2RayConfigLine>())
            .Select(c => new V2RayConfigLineDto(c.Uri, c.Remark, c.Protocol, c.Network))
            .ToList();

        return Ok(new V2RayConfigDto(
            Name: account.Email,
            Uuid: account.Uuid,
            Server: server,
            Flag: panel?.Flag ?? "",
            Protocol: account.Protocol,
            Network: account.Network,
            SubUrl: account.SubUrl,
            SubId: account.SubId,
            UsedBytes: sub.Up + sub.Down,
            UpBytes: sub.Up,
            DownBytes: sub.Down,
            TotalBytes: totalBytes,
            IpLimit: account.IpLimit,
            Online: false,
            RemainingDays: remainingDays,
            ExpiresAtUtc: expiresAt,
            CreatedAtUtc: account.CreatedAtUtc,
            Active: true,
            StatsLive: sub.Ok,
            Configs: configs));
    }
}

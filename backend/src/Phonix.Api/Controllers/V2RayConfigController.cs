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

// `Status` is the single word the page renders its state from, so the rules that decide it live here rather
// than being re-derived in the browser from a handful of numbers:
//
//   active    — running normally
//   expired   — its time ran out
//   depleted  — its traffic ran out (the time may still be fine)
//   disabled  — the panel switched it off for some other reason
//   removed   — the account no longer exists on the panel
//
// A finished service keeps reporting the usage and the expiry it finished on. Zeroing those out is what made
// an expired config look like a brand-new empty one instead of one that had simply ended.
public sealed record V2RayConfigDto(
    string Name, string Uuid, string Server, string Flag, string Protocol, string Network,
    string SubUrl, string SubId,
    long UsedBytes, long UpBytes, long DownBytes, long TotalBytes,
    int IpLimit, bool Online, DateTime? LastOnlineUtc,
    int? RemainingDays, DateTime? ExpiresAtUtc, DateTime? CreatedAtUtc,
    string Status, bool Active, bool StatsLive,
    int RenewCount, DateTime? LastRenewedAtUtc,
    IReadOnlyList<V2RayConfigLineDto> Configs);

// A plan this exact service can be renewed onto: the same catalogue category AND the same server, because a
// renewal rewrites the term of the client where it already lives.
public sealed record V2RayRenewalPlanDto(
    int Id, string Title, string Description, long VolumeGb, int DurationDays, int IpLimit,
    long Price, int DiscountPercent, long FinalPrice);

public sealed record V2RayRenewalsDto(
    bool Renewable, string Reason, int ProductId, string ProductName,
    IReadOnlyList<V2RayRenewalPlanDto> Plans);

// The customer-facing view of one provisioned V2Ray account, addressed only by its unguessable token.
// Anonymous on purpose: the buyer often provisions for someone else (a colleague, a family member) and needs
// a link they can simply hand over.
[ApiController]
[Route("api/v2ray/config")]
[AllowAnonymous]
public class V2RayConfigController : ControllerBase
{
    // The page polls, and the link is meant to be shared — several people refreshing one service must not
    // turn into a request per refresh against the panel. A few seconds is short enough that usage still reads
    // as live and long enough to absorb a burst.
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(10);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime At, V2RaySubscription Sub)> SubCache = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime At, V2RayTraffic Traffic)> LiveCache = new();

    private readonly IDataStore _store;
    private readonly IV2RayPanelConnector _connector;

    public V2RayConfigController(IDataStore store, IV2RayPanelConnector connector)
    {
        _store = store;
        _connector = connector;
    }

    // One entry per service would otherwise accumulate for the life of the process, so expired entries are
    // swept once a map grows past a shop's worth of active services.
    private const int CacheLimit = 500;

    private static void Sweep<T>(System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime At, T Value)> cache, DateTime now)
    {
        if (cache.Count < CacheLimit) return;
        foreach (var stale in cache.Where(e => now - e.Value.At >= CacheFor).Select(e => e.Key).ToList())
            cache.TryRemove(stale, out _);
    }

    private async Task<V2RaySubscription> ReadSubscriptionAsync(string subUrl, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        if (SubCache.TryGetValue(subUrl, out var hit) && now - hit.At < CacheFor) return hit.Sub;

        var sub = await _connector.GetSubscriptionAsync(subUrl, ct);
        // A failed read isn't cached: the next visitor should get a fresh attempt rather than a stuck error.
        if (!sub.Ok) return sub;

        Sweep(SubCache, now);
        SubCache[subUrl] = (now, sub);
        return sub;
    }

    // The panel's own numbers for this client. This — not the subscription link — is what keeps working once
    // a service ends: the panel stops serving configs to a finished client, but it still knows exactly how
    // much that client used and when it expired.
    private async Task<V2RayTraffic> ReadLiveAsync(V2RayPanel panel, string email, CancellationToken ct)
    {
        var key = $"{panel.Id}|{email}";
        var now = DateTime.UtcNow;
        if (LiveCache.TryGetValue(key, out var hit) && now - hit.At < CacheFor) return hit.Traffic;

        var traffic = await _connector.GetTrafficAsync(
            panel.Provider,
            new V2RayCredentials(panel.Url, panel.Username, SensitiveField.Reveal(panel.Password), SensitiveField.Reveal(panel.ApiToken)),
            email, ct);
        // A deleted account is a settled answer and worth caching; an unreachable panel is not.
        if (!traffic.Ok && !traffic.Missing) return traffic;

        Sweep(LiveCache, now);
        LiveCache[key] = (now, traffic);
        return traffic;
    }

    [HttpGet("{token}")]
    public async Task<IActionResult> Get(string token, CancellationToken ct)
    {
        var found = _store.FindUnitByV2RayToken(token);
        if (found is not var (_, unit) || unit.V2Ray is null) return NotFound();
        var account = unit.V2Ray;

        var panel = _store.GetV2RayPanel(account.PanelId);
        var server = panel is null || string.IsNullOrWhiteSpace(panel.Name) ? "سرور" : panel.Name.Trim();

        // An account we already know is gone is never asked about again — there is nothing on the panel to
        // ask, and the terms it ended on are all the page has left to show.
        var live = account.PanelDeletedAtUtc is not null || panel is null
            ? V2RayTraffic.Gone("این سرویس دیگر روی سرور موجود نیست.")
            : await ReadLiveAsync(panel, account.Email, ct);

        // The subscription link is what the customer's app polls, and the only place the config URIs come
        // from. A link that is down must not take the whole page with it.
        var sub = string.IsNullOrWhiteSpace(account.SubUrl) || live.Missing
            ? V2RaySubscription.Fail("لینک اشتراک در دسترس نیست.")
            : await ReadSubscriptionAsync(account.SubUrl, ct);

        // Preference order for the numbers: the panel (authoritative, survives the service ending), then the
        // subscription header, then the plan's own terms as a last resort.
        var statsLive = live.Ok || sub.Ok;
        var up = live.Ok ? live.Up : sub.Up;
        var down = live.Ok ? live.Down : sub.Down;
        var totalBytes = live.Ok ? live.Total : (sub.Ok && sub.Total > 0 ? sub.Total : account.VolumeGb * 1024L * 1024L * 1024L);

        var expiresAt = live.Ok && live.ExpiryTimeMs > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(live.ExpiryTimeMs).UtcDateTime
            : live.Ok
                ? null                       // the panel says this client never expires
                : sub.Ok && sub.ExpireUnix > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(sub.ExpireUnix).UtcDateTime
                    : account.ExpiresAtUtc;

        var now = DateTime.UtcNow;
        var expired = expiresAt is DateTime e && e <= now;
        var depleted = totalBytes > 0 && up + down >= totalBytes;
        var remainingDays = expiresAt is DateTime exp
            ? Math.Max(0, (int)Math.Ceiling((exp - now).TotalDays))
            : (int?)null;

        var status = live.Missing || account.PanelDeletedAtUtc is not null ? "removed"
            : expired ? "expired"
            : depleted ? "depleted"
            : live.Ok && !live.Enable ? "disabled"
            : "active";

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
            UsedBytes: up + down,
            UpBytes: up,
            DownBytes: down,
            TotalBytes: totalBytes,
            IpLimit: account.IpLimit,
            Online: live.Online,
            LastOnlineUtc: live.LastOnlineMs > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(live.LastOnlineMs).UtcDateTime
                : null,
            RemainingDays: remainingDays,
            ExpiresAtUtc: expiresAt,
            CreatedAtUtc: account.CreatedAtUtc,
            Status: status,
            Active: status == "active",
            StatsLive: statsLive,
            RenewCount: account.RenewCount,
            LastRenewedAtUtc: account.LastRenewedAtUtc,
            Configs: configs));
    }

    // What this service can be renewed onto. Read-only and anonymous like the page it feeds — these are the
    // same plans the storefront publishes — but renewing itself still requires the buyer to be signed in, and
    // the checkout re-checks every rule below against the account that is paying.
    [HttpGet("{token}/renewals")]
    public IActionResult Renewals(string token)
    {
        var found = _store.FindUnitByV2RayToken(token);
        if (found is not var (_, unit) || unit.V2Ray is not { Uuid.Length: > 0 } account) return NotFound();

        var empty = Array.Empty<V2RayRenewalPlanDto>();
        var product = _store.GetProduct(unit.ProductId);

        if (account.PanelDeletedAtUtc is not null)
            return Ok(new V2RayRenewalsDto(false, "این سرویس حذف شده و قابل تمدید نیست.", 0, "", empty));
        if (product is null || !product.IsActive || product.V2RayCategoryId <= 0)
            return Ok(new V2RayRenewalsDto(false, "تمدید این سرویس در حال حاضر ممکن نیست.", 0, "", empty));

        // Filtered by PANEL, not by category: every active category is a location the product sells, so the
        // service being renewed may well live in a different one than the product is linked to. What the
        // renewal genuinely requires is the same server, because it rewrites the term of the client already
        // there — a plan from another location would be written onto a panel that doesn't hold this client.
        var activeCategories = _store.GetV2RayCategories().Where(c => c.Active).Select(c => c.Id).ToHashSet();
        var plans = _store.GetV2RayPlans()
            .Where(p => activeCategories.Contains(p.CategoryId) && p.PanelId == account.PanelId && p.Active && !p.SoldOut)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.FinalPrice)
            .Select(p => new V2RayRenewalPlanDto(
                p.Id, p.Title, p.Description, p.VolumeGb, p.DurationDays, p.IpLimit,
                p.Price, p.DiscountPercent, p.FinalPrice))
            .ToList();

        return plans.Count == 0
            ? Ok(new V2RayRenewalsDto(false, "در حال حاضر پلنی برای تمدید این سرویس موجود نیست.", product.Id, product.Name, empty))
            : Ok(new V2RayRenewalsDto(true, "", product.Id, product.Name, plans));
    }
}

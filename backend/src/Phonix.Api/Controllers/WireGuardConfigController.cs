using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;
using Phonix.Api.Services;

namespace Phonix.Api.Controllers;

// One device's configuration as the customer's page shows it: the file to import or scan, and for OpenVPN
// the username/password that goes with it.
public sealed record WireGuardProfileDto(
    int DeviceId, string DeviceName, string InterfaceName, string Protocol, string Filename, string Body,
    string Username, string Password);

// What the WireGuard config page shows. Same rules as V2RayConfigDto: about ONE service, with nothing about
// the purchase or the infrastructure behind it. `Status` is one of active / expired / depleted / disabled /
// removed, decided here rather than in the browser.
public sealed record WireGuardConfigDto(
    string Name, string Server, string Flag, string Protocol,
    string SubUrl, string SubId,
    long UsedBytes, long UpBytes, long DownBytes, long TotalBytes,
    int DeviceLimit, int OnlineNow,
    int? RemainingDays, DateTime? ExpiresAtUtc, DateTime? CreatedAtUtc,
    string Status, bool Active, bool StatsLive,
    int RenewCount, DateTime? LastRenewedAtUtc,
    IReadOnlyList<WireGuardProfileDto> Profiles);

public sealed record WireGuardRenewalPlanDto(
    int Id, string Title, string Description, long VolumeGb, int DurationDays, int DeviceLimit,
    long Price, int DiscountPercent, long FinalPrice);

public sealed record WireGuardRenewalsDto(
    bool Renewable, string Reason, int ProductId, string ProductName,
    IReadOnlyList<WireGuardRenewalPlanDto> Plans);

// The customer-facing view of one provisioned W-UI customer, addressed only by its unguessable token.
[ApiController]
[Route("api/wireguard/config")]
[AllowAnonymous]
public class WireGuardConfigController : ControllerBase
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(10);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime At, WireGuardClientState State)> LiveCache = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime At, WireGuardProfilesResult Profiles)> ProfileCache = new();
    private const int CacheLimit = 500;

    private readonly IDataStore _store;
    private readonly IWireGuardPanelConnector _connector;

    public WireGuardConfigController(IDataStore store, IWireGuardPanelConnector connector)
    {
        _store = store;
        _connector = connector;
    }

    private static void Sweep<T>(System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime At, T Value)> cache, DateTime now)
    {
        if (cache.Count < CacheLimit) return;
        foreach (var stale in cache.Where(e => now - e.Value.At >= CacheFor).Select(e => e.Key).ToList())
            cache.TryRemove(stale, out _);
    }

    private static WireGuardCredentials Creds(WireGuardPanel panel) =>
        new(panel.Url, panel.Username, SensitiveField.Reveal(panel.Password), SensitiveField.Reveal(panel.ApiToken));

    private async Task<WireGuardClientState> ReadLiveAsync(WireGuardPanel panel, int clientId, CancellationToken ct)
    {
        var key = $"{panel.Id}|{clientId}";
        var now = DateTime.UtcNow;
        if (LiveCache.TryGetValue(key, out var hit) && now - hit.At < CacheFor) return hit.State;

        var state = await _connector.GetClientAsync(panel.Provider, Creds(panel), clientId, ct);
        if (!state.Ok && !state.Missing) return state;

        Sweep(LiveCache, now);
        LiveCache[key] = (now, state);
        return state;
    }

    // Profiles change only when a device is added or a tunnel moves, but they are several panel reads per
    // page load, so they are cached for the same short window as the counters.
    private async Task<WireGuardProfilesResult> ReadProfilesAsync(WireGuardPanel panel, int clientId, CancellationToken ct)
    {
        var key = $"{panel.Id}|{clientId}";
        var now = DateTime.UtcNow;
        if (ProfileCache.TryGetValue(key, out var hit) && now - hit.At < CacheFor) return hit.Profiles;

        var profiles = await _connector.GetProfilesAsync(panel.Provider, Creds(panel), clientId, ct);
        if (!profiles.Ok) return profiles;

        Sweep(ProfileCache, now);
        ProfileCache[key] = (now, profiles);
        return profiles;
    }

    [HttpGet("{token}")]
    public async Task<IActionResult> Get(string token, CancellationToken ct)
    {
        var found = _store.FindUnitByWireGuardToken(token);
        if (found is not var (_, unit) || unit.WireGuard is null) return NotFound();
        var account = unit.WireGuard;

        var panel = _store.GetWireGuardPanel(account.PanelId);
        var server = panel is null || string.IsNullOrWhiteSpace(panel.Name) ? "سرور" : panel.Name.Trim();

        var live = account.PanelDeletedAtUtc is not null || panel is null || account.ClientId <= 0
            ? WireGuardClientState.Gone("این سرویس دیگر روی سرور موجود نیست.")
            : await ReadLiveAsync(panel, account.ClientId, ct);

        // A finished service keeps its files: the page must still show what the customer had, and a client
        // the panel switched off can be renewed without re-importing anything.
        var profiles = live.Missing || panel is null
            ? WireGuardProfilesResult.Fail("کانفیگ در دسترس نیست.")
            : await ReadProfilesAsync(panel, account.ClientId, ct);

        var used = live.Ok ? live.UsedBytes : 0;
        var totalBytes = live.Ok ? live.QuotaBytes : account.VolumeGb * 1024L * 1024L * 1024L;
        var expiresAt = live.Ok ? live.ExpiresAt?.UtcDateTime : account.ExpiresAtUtc;

        var now = DateTime.UtcNow;
        var expired = expiresAt is DateTime e && e <= now;
        var depleted = totalBytes > 0 && used >= totalBytes;
        var remainingDays = expiresAt is DateTime exp
            ? Math.Max(0, (int)Math.Ceiling((exp - now).TotalDays))
            : (int?)null;

        var status = live.Missing || account.PanelDeletedAtUtc is not null ? "removed"
            : live.Ok && live.Status == "expired" || expired ? "expired"
            : live.Ok && live.Status == "exhausted" || depleted ? "depleted"
            : live.Ok && live.Status == "disabled" ? "disabled"
            : "active";

        var files = (profiles.Profiles ?? Array.Empty<WireGuardProfile>())
            .Select(p => new WireGuardProfileDto(p.DeviceId, p.DeviceName, p.InterfaceName, p.Protocol, p.Filename, p.Body, p.Username, p.Password))
            .ToList();

        return Ok(new WireGuardConfigDto(
            Name: account.Name,
            Server: server,
            Flag: panel?.Flag ?? "",
            Protocol: account.Protocol,
            SubUrl: account.SubUrl,
            SubId: account.SubId,
            UsedBytes: used,
            UpBytes: live.Ok ? live.UpBytes : 0,
            DownBytes: live.Ok ? live.DownBytes : 0,
            TotalBytes: totalBytes,
            DeviceLimit: live.Ok && live.DeviceLimit > 0 ? live.DeviceLimit : account.DeviceLimit,
            OnlineNow: live.Ok ? live.OnlineNow : 0,
            RemainingDays: remainingDays,
            ExpiresAtUtc: expiresAt,
            CreatedAtUtc: account.CreatedAtUtc,
            Status: status,
            Active: status == "active",
            StatsLive: live.Ok,
            RenewCount: account.RenewCount,
            LastRenewedAtUtc: account.LastRenewedAtUtc,
            Profiles: files));
    }

    [HttpGet("{token}/renewals")]
    public IActionResult Renewals(string token)
    {
        var found = _store.FindUnitByWireGuardToken(token);
        if (found is not var (_, unit) || unit.WireGuard is not { ClientId: > 0 } account) return NotFound();

        var empty = Array.Empty<WireGuardRenewalPlanDto>();
        var product = _store.GetProduct(unit.ProductId);

        if (account.PanelDeletedAtUtc is not null)
            return Ok(new WireGuardRenewalsDto(false, "این سرویس حذف شده و قابل تمدید نیست.", 0, "", empty));
        if (product is null || !product.IsActive || product.V2RayCategoryId > 0 || product.WireGuardCategoryId <= 0)
            return Ok(new WireGuardRenewalsDto(false, "تمدید این سرویس در حال حاضر ممکن نیست.", 0, "", empty));

        var activeCategories = _store.GetWireGuardCategories().Where(c => c.Active).Select(c => c.Id).ToHashSet();
        var plans = _store.GetWireGuardPlans()
            .Where(p => activeCategories.Contains(p.CategoryId) && p.PanelId == account.PanelId && p.Active && !p.SoldOut)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.FinalPrice)
            .Select(p => new WireGuardRenewalPlanDto(
                p.Id, p.Title, p.Description, p.VolumeGb, p.DurationDays, p.DeviceLimit,
                p.Price, p.DiscountPercent, p.FinalPrice))
            .ToList();

        return plans.Count == 0
            ? Ok(new WireGuardRenewalsDto(false, "در حال حاضر پلنی برای تمدید این سرویس موجود نیست.", product.Id, product.Name, empty))
            : Ok(new WireGuardRenewalsDto(true, "", product.Id, product.Name, plans));
    }
}

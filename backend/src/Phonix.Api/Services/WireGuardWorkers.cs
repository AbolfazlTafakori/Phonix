using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Services;

// The WireGuard twins of V2RayProvisionWorker and V2RayMonitorWorker. Same cadence, same rules; the only
// differences are the connector they talk to and the account record they read. See the V2Ray originals for
// the reasoning behind each rule — it is deliberately not repeated here so the two cannot drift apart in
// their comments while staying identical in behaviour.

public class WireGuardProvisionWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(45);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<WireGuardProvisionWorker> _logger;

    public WireGuardProvisionWorker(IServiceScopeFactory scopes, ILogger<WireGuardProvisionWorker> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IDataStore>();
                var fulfil = scope.ServiceProvider.GetRequiredService<IWireGuardFulfillmentService>();
                // The same pool the V2Ray worker sweeps: every approved order with an undelivered unit. The
                // service picks out the units that are its own.
                foreach (var order in store.GetOrdersAwaitingV2Ray())
                {
                    if (stoppingToken.IsCancellationRequested) return;
                    await fulfil.ProvisionOrderAsync(order, stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "WireGuard provisioning sweep failed; will retry on the next cycle.");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}

public class WireGuardMonitorWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);
    private const long BytesPerGb = 1024L * 1024L * 1024L;

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<WireGuardMonitorWorker> _logger;

    public WireGuardMonitorWorker(IServiceScopeFactory scopes, ILogger<WireGuardMonitorWorker> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    private static string FrontendUrl => Environment.GetEnvironmentVariable("PHONIX_FRONTEND_URL") ?? "http://localhost:3000";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "WireGuard monitoring sweep failed; will retry on the next cycle.");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IDataStore>();
        var connector = scope.ServiceProvider.GetRequiredService<IWireGuardPanelConnector>();
        var email = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        // The thresholds are shared with V2Ray: one "warn me N hours before / delete N hours after" setting
        // for every panel-provisioned service the shop sells.
        var alerts = store.GetV2RayAlertSettings();
        if (!alerts.Enabled) return;

        var services = store.GetWireGuardServices();
        if (services.Count == 0) return;

        var now = DateTime.UtcNow;
        foreach (var group in services.GroupBy(s => s.PanelId))
        {
            if (ct.IsCancellationRequested) return;

            var panel = store.GetWireGuardPanel(group.Key);
            if (panel is null || !panel.Enabled) continue;

            var creds = new WireGuardCredentials(panel.Url, panel.Username,
                SensitiveField.Reveal(panel.Password), SensitiveField.Reveal(panel.ApiToken));

            var snapshot = await connector.GetSnapshotAsync(panel.Provider, creds, ct);
            if (!snapshot.Ok || snapshot.Clients is null)
            {
                _logger.LogWarning("Skipped WireGuard panel {Panel} this cycle: {Error}", panel.Id, snapshot.Error);
                continue;
            }

            foreach (var service in group)
            {
                if (ct.IsCancellationRequested) return;
                await ReviewAsync(store, connector, email, alerts, panel, creds, snapshot, service, now, ct);
            }
        }
    }

    private async Task ReviewAsync(
        IDataStore store, IWireGuardPanelConnector connector, IEmailSender email,
        V2RayAlertSettings alerts, WireGuardPanel panel, WireGuardCredentials creds,
        WireGuardPanelSnapshot snapshot, WireGuardServiceRef service, DateTime now, CancellationToken ct)
    {
        if (!snapshot.Clients!.TryGetValue(service.ClientId, out var state))
        {
            store.MarkWireGuardPanelDeleted(service.OrderId, service.UnitId, "روی پنل پیدا نشد", notify: false);
            _logger.LogInformation("WireGuard customer {Id} is no longer on panel {Panel}; marked as removed.",
                service.ClientId, panel.Id);
            return;
        }

        var verdict = Decide(alerts, state, service, now);

        if (verdict.Delete)
        {
            var result = await connector.DeleteClientAsync(panel.Provider, creds, service.ClientId, ct);
            if (!result.Ok)
            {
                _logger.LogWarning("Could not remove the expired WireGuard customer {Id} from panel {Panel}: {Error}",
                    service.ClientId, panel.Id, result.Error);
                return;
            }

            var target = store.MarkWireGuardPanelDeleted(
                service.OrderId, service.UnitId, "پس از پایان مهلت تمدید حذف شد", notify: true);
            _logger.LogInformation("Removed the expired WireGuard customer {Id} from panel {Panel} for order {Code}.",
                service.ClientId, panel.Id, service.OrderCode);

            if (target is { Email.Length: > 0 })
            {
                var (text, html) = EmailTemplates.V2RayRemoved(target.OrderCode, FrontendUrl);
                await SendAsync(email, target.Email, $"پایان سرویس — سفارش {target.OrderCode}", text, html, service.OrderCode);
            }
            return;
        }

        if (!verdict.Warns) return;

        var remainingFa = FormatGb(Math.Max(0, verdict.RemainingBytes));
        var expiresFa = verdict.ExpiresAt is DateTime d ? JalaliDate.Format(d) : "";

        var claimed = store.ClaimWireGuardWarning(
            service.OrderId, service.UnitId, verdict.WarnExpiry, verdict.WarnVolume, expiresFa, remainingFa);
        if (claimed is null) return;

        _logger.LogInformation("Warned {Email} about WireGuard service {Service} (time: {Time}, volume: {Volume}).",
            claimed.Email, service.ClientId, verdict.WarnExpiry, verdict.WarnVolume);

        if (string.IsNullOrWhiteSpace(claimed.Email)) return;
        var (body, markup) = EmailTemplates.V2RayRunningOut(
            claimed.OrderCode,
            verdict.WarnExpiry ? expiresFa : null,
            verdict.WarnVolume ? remainingFa : null,
            $"{FrontendUrl}/wg/{claimed.Token}");
        await SendAsync(email, claimed.Email, $"سرویس شما رو به پایان است — سفارش {claimed.OrderCode}", body, markup, claimed.OrderCode);
    }

    // The same rules as V2RayMonitorWorker.Decide, on W-UI's counters (bytes used against a byte quota, a
    // nullable expiry).
    public static V2RayVerdict Decide(V2RayAlertSettings alerts, WireGuardClientState state, WireGuardServiceRef service, DateTime now)
    {
        var expiresAt = state.ExpiresAt?.UtcDateTime;
        var remaining = state.QuotaBytes > 0 ? state.QuotaBytes - state.UsedBytes : 0;

        var delete = alerts.DeleteAfterExpiryHours > 0
            && expiresAt is DateTime ended
            && ended.AddHours(alerts.DeleteAfterExpiryHours) <= now;
        if (delete) return new V2RayVerdict(false, false, true, expiresAt, remaining);

        var warnExpiry = !service.ExpiryWarned
            && alerts.ExpiryWarnHours > 0
            && expiresAt is DateTime due
            && due > now
            && due - now <= TimeSpan.FromHours(alerts.ExpiryWarnHours);

        var threshold = (long)Math.Round(alerts.VolumeWarnGb * BytesPerGb);
        var warnVolume = !service.VolumeWarned
            && threshold > 0
            && state.QuotaBytes > 0
            && remaining <= threshold;

        return new V2RayVerdict(warnExpiry, warnVolume, false, expiresAt, remaining);
    }

    private async Task SendAsync(IEmailSender email, string to, string subject, string text, string html, string orderCode)
    {
        try
        {
            await email.SendAsync(to, subject, text, html);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed sending the WireGuard service email for order {OrderCode}", orderCode);
        }
    }

    private static string FormatGb(long bytes)
    {
        if (bytes >= BytesPerGb)
        {
            var gb = bytes / (double)BytesPerGb;
            return JalaliDate.ToPersianDigits($"{Math.Round(gb, gb >= 10 ? 0 : 1)}") + " گیگابایت";
        }
        var mb = (long)Math.Round(bytes / (1024.0 * 1024.0));
        return JalaliDate.ToPersianDigits(mb.ToString()) + " مگابایت";
    }
}

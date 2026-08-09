using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Services;

// What one account's numbers mean, once the thresholds have been applied to them. Deciding this separately
// from acting on it is what makes the rules — which are the whole point of the feature and the easiest part
// to get wrong — checkable without a panel or a mail server in the way.
public readonly record struct V2RayVerdict(
    bool WarnExpiry, bool WarnVolume, bool Delete, DateTime? ExpiresAt, long RemainingBytes)
{
    public bool Warns => WarnExpiry || WarnVolume;
}

// Watches every provisioned V2Ray account and does the two things a customer expects a shop to do without
// being asked: warn them before a service runs out, and tidy the server up once one is over.
//
// Three rules shape this, and each of them is easy to get subtly wrong:
//
//   1. TIME and TRAFFIC are warned about separately. They run out independently — a plan can be down to its
//      last gigabyte with three weeks left, or expire untouched — so each carries its own once-only flag.
//      When both fall due in the same pass they are claimed together and produce ONE message.
//   2. Only TIME leads to deletion. A customer whose traffic ran out still owns days they paid for, and
//      renewing has to give them back the very same config. Deleting on exhausted traffic would take the
//      link out from under them.
//   3. Deletion is per ACCOUNT, never per customer. Each purchased config is its own client on the panel,
//      named for its own order unit, so the buyer with fifty configs loses exactly the one that lapsed.
//
// A panel that cannot be reached is skipped whole. Nothing is warned about and — far more importantly —
// nothing is deleted on the strength of an answer we never got.
public class V2RayMonitorWorker : BackgroundService
{
    // Ten minutes puts the warnings well inside any threshold an operator would set (the smallest useful one
    // is hours) while costing two requests per panel per cycle, not per account.
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    private const long BytesPerGb = 1024L * 1024L * 1024L;

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<V2RayMonitorWorker> _logger;

    public V2RayMonitorWorker(IServiceScopeFactory scopes, ILogger<V2RayMonitorWorker> logger)
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
                // A bad cycle must never kill the worker: the next one re-reads everything from scratch.
                _logger.LogError(ex, "V2Ray monitoring sweep failed; will retry on the next cycle.");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IDataStore>();
        var connector = scope.ServiceProvider.GetRequiredService<IV2RayPanelConnector>();
        var email = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        // Re-read every pass, so changing a threshold in the admin panel takes effect on the next cycle
        // without a restart.
        var alerts = store.GetV2RayAlertSettings();
        if (!alerts.Enabled) return;

        var services = store.GetV2RayServices();
        if (services.Count == 0) return;

        var now = DateTime.UtcNow;
        foreach (var group in services.GroupBy(s => s.PanelId))
        {
            if (ct.IsCancellationRequested) return;

            var panel = store.GetV2RayPanel(group.Key);
            if (panel is null || !panel.Enabled) continue;

            var creds = new V2RayCredentials(panel.Url, panel.Username,
                SensitiveField.Reveal(panel.Password), SensitiveField.Reveal(panel.ApiToken));

            var snapshot = await connector.GetSnapshotAsync(panel.Provider, creds, ct);
            if (!snapshot.Ok || snapshot.Clients is null)
            {
                // Deliberately quiet and deliberately total: an unreachable panel tells us nothing about any
                // of its accounts, and acting on that silence is how services get deleted by accident.
                _logger.LogWarning("Skipped V2Ray panel {Panel} this cycle: {Error}", panel.Id, snapshot.Error);
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
        IDataStore store, IV2RayPanelConnector connector, IEmailSender email,
        V2RayAlertSettings alerts, V2RayPanel panel, V2RayCredentials creds,
        V2RayPanelSnapshot snapshot, V2RayServiceRef service, DateTime now, CancellationToken ct)
    {
        // The panel answered and doesn't have this client: an operator removed it by hand. Recorded so the
        // config page stops showing stale numbers and the sweep stops looking for it — but silently, because
        // announcing a removal we didn't perform would be guessing at somebody else's intent.
        if (!snapshot.Clients!.TryGetValue(service.Email, out var state))
        {
            store.MarkV2RayPanelDeleted(service.OrderId, service.UnitId, "روی پنل پیدا نشد", notify: false);
            _logger.LogInformation("V2Ray account {Email} is no longer on panel {Panel}; marked as removed.",
                service.Email, panel.Id);
            return;
        }

        var verdict = Decide(alerts, state, service, now);

        // ── Clean-up: TIME only, after the configured grace period ──────────────────────────────────
        if (verdict.Delete)
        {
            var result = await connector.DeleteClientAsync(panel.Provider, creds, service.Email, ct);
            if (!result.Ok)
            {
                // Left in place for the next cycle. A delete that failed is not a delete.
                _logger.LogWarning("Could not remove the expired V2Ray account {Email} from panel {Panel}: {Error}",
                    service.Email, panel.Id, result.Error);
                return;
            }

            var target = store.MarkV2RayPanelDeleted(
                service.OrderId, service.UnitId, "پس از پایان مهلت تمدید حذف شد", notify: true);
            _logger.LogInformation("Removed the expired V2Ray account {Email} from panel {Panel} for order {Code}.",
                service.Email, panel.Id, service.OrderCode);

            if (target is { Email.Length: > 0 })
            {
                var (text, html) = EmailTemplates.V2RayRemoved(target.OrderCode, FrontendUrl);
                await SendAsync(email, target.Email, $"پایان سرویس — سفارش {target.OrderCode}", text, html, service.OrderCode);
            }
            return;
        }

        // ── Warnings ────────────────────────────────────────────────────────────────────────────────
        if (!verdict.Warns) return;

        var remainingFa = FormatGb(Math.Max(0, verdict.RemainingBytes));
        var expiresFa = verdict.ExpiresAt is DateTime d ? JalaliDate.Format(d) : "";

        var claimed = store.ClaimV2RayWarning(
            service.OrderId, service.UnitId, verdict.WarnExpiry, verdict.WarnVolume, expiresFa, remainingFa);
        if (claimed is null) return;   // another pass got there first

        _logger.LogInformation("Warned {Email} about V2Ray service {Service} (time: {Time}, volume: {Volume}).",
            claimed.Email, service.Email, verdict.WarnExpiry, verdict.WarnVolume);

        if (string.IsNullOrWhiteSpace(claimed.Email)) return;   // the in-app notice already went out
        var (body, markup) = EmailTemplates.V2RayRunningOut(
            claimed.OrderCode,
            verdict.WarnExpiry ? expiresFa : null,
            verdict.WarnVolume ? remainingFa : null,
            $"{FrontendUrl}/config/{claimed.Token}");
        await SendAsync(email, claimed.Email, $"سرویس شما رو به پایان است — سفارش {claimed.OrderCode}", body, markup, claimed.OrderCode);
    }

    // The rules, in one place.
    //
    //   * Removal is driven by TIME alone, and only once the grace period after the expiry has fully
    //     elapsed. An account with no expiry at all is never removed, and neither is one that has merely run
    //     out of traffic — that customer still owns days they paid for.
    //   * The time warning fires inside the window and only while the service is still running; once it has
    //     ended the honest message is the removal notice, not a warning that it is about to happen.
    //   * The volume warning fires at or below the threshold, INCLUDING at zero, which is exactly the moment
    //     a customer wants to be told.
    //   * Each warning is skipped once its own flag is set, so a renewal (which clears both) re-arms them
    //     for the new term while a second sweep in the same term stays silent.
    public static V2RayVerdict Decide(V2RayAlertSettings alerts, V2RayClientState state, V2RayServiceRef service, DateTime now)
    {
        var expiresAt = state.ExpiryTimeMs > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(state.ExpiryTimeMs).UtcDateTime
            : (DateTime?)null;
        var remaining = state.Total > 0 ? state.Total - state.Used : 0;

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
            && state.Total > 0
            && remaining <= threshold;

        return new V2RayVerdict(warnExpiry, warnVolume, false, expiresAt, remaining);
    }

    // The in-app notice and the store flag are already committed by the time this runs, so a mail server that
    // is down costs one email — never a repeat warning, and never a stuck sweep.
    private async Task SendAsync(IEmailSender email, string to, string subject, string text, string html, string orderCode)
    {
        try
        {
            await email.SendAsync(to, subject, text, html);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed sending the V2Ray service email for order {OrderCode}", orderCode);
        }
    }

    // Persian digits, and megabytes below a gigabyte — "۰٫۳ گیگابایت" reads as nothing at all next to
    // "۳۰۰ مگابایت", and this number is the whole point of the warning.
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

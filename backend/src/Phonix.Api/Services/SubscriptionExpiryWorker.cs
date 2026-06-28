using Phonix.Api.Data;

namespace Phonix.Api.Services;

// Background service that reminds users to renew before a time-based subscription expires. It checks
// hourly; each pass reads the admin-configured threshold (PricingSettings.SubscriptionReminderHoursBefore)
// dynamically, so changing it in the panel takes effect on the very next cycle without a restart. The
// store does the due-detection + marking atomically under its lock (and persists immediately), so a
// reminder is sent at most once even across restarts; this worker only sends the emails afterwards.
public class SubscriptionExpiryWorker : BackgroundService
{
    private readonly IDataStore _store;
    private readonly IEmailSender _email;
    private readonly ILogger<SubscriptionExpiryWorker> _logger;

    // Hourly is within the "every 1 to 12 hours" requirement and fine-grained enough for an hours-based
    // threshold. The reminder window is `<= threshold` (not an exact match), so an order can't slip through
    // between two ticks, and the once-only flag prevents repeats.
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    private static string FrontendUrl => Environment.GetEnvironmentVariable("PHONIX_FRONTEND_URL") ?? "http://localhost:3000";

    public SubscriptionExpiryWorker(IDataStore store, IEmailSender email, ILogger<SubscriptionExpiryWorker> logger)
    {
        _store = store;
        _email = email;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // never let a bad cycle kill the worker — log and try again next interval.
                _logger.LogError(ex, "Subscription expiry check failed");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        var hoursBefore = _store.GetSettings().SubscriptionReminderHoursBefore;
        if (hoursBefore <= 0) return; // reminders disabled

        // marks them sent + fires the in-app notifications under the store lock; returns who to email.
        var due = _store.CollectDueRenewalReminders(hoursBefore);
        if (due.Count == 0) return;

        var renewUrl = $"{FrontendUrl}/account/orders";
        var sent = 0;
        foreach (var r in due)
        {
            if (ct.IsCancellationRequested) break;
            if (string.IsNullOrWhiteSpace(r.Email)) continue; // bell notification already delivered
            var (text, html) = EmailTemplates.SubscriptionReminder(r.OrderCode, r.ExpiresFa, renewUrl);
            try
            {
                await _email.SendAsync(r.Email, $"یادآوری تمدید اشتراک — سفارش {r.OrderCode}", text, html);
                sent++;
            }
            catch (Exception ex)
            {
                // the in-app reminder already went out and the order is flagged; just note the email failure.
                _logger.LogWarning(ex, "Failed sending renewal email for order {OrderCode}", r.OrderCode);
            }
        }

        _logger.LogInformation("Subscription reminders: notified {Notified} user(s), emailed {Emailed}", due.Count, sent);
    }
}

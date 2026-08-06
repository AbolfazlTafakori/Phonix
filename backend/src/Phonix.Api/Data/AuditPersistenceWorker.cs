namespace Phonix.Api.Data;

// Periodically flushes the audit trail to audit_store.json and guarantees a final save on shutdown.
// The audit trail keeps its own file and flush schedule so a slow/large audit write never blocks the main
// store (and vice-versa).
public sealed class AuditPersistenceWorker : BackgroundService
{
    private readonly AuditStore _audit;
    private readonly ILogger<AuditPersistenceWorker> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    public AuditPersistenceWorker(AuditStore audit, ILogger<AuditPersistenceWorker> logger)
    {
        _audit = audit;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
            // The write has to be inside a catch of its own. An unhandled exception from a BackgroundService
            // stops the HOST by default, so one transient disk error here — a full volume, a locked file —
            // would take the whole API down to protect a log flush that will simply be retried in 10 seconds.
            try
            {
                _audit.SaveIfChanged();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Audit trail could not be flushed; retrying on the next cycle.");
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Shutdown path: a failure here must not turn a clean stop into a crash either.
        try
        {
            _audit.Save();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Final audit-trail save on shutdown failed.");
        }
        await base.StopAsync(cancellationToken);
    }
}

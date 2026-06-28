namespace Phonix.Api.Data;

// Periodically flushes the audit trail to audit_store.json and guarantees a final save on shutdown.
// The audit trail keeps its own file and flush schedule so a slow/large audit write never blocks the main
// store (and vice-versa).
public sealed class AuditPersistenceWorker : BackgroundService
{
    private readonly AuditStore _audit;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    public AuditPersistenceWorker(AuditStore audit) => _audit = audit;

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
            _audit.SaveIfChanged();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _audit.Save();
        await base.StopAsync(cancellationToken);
    }
}

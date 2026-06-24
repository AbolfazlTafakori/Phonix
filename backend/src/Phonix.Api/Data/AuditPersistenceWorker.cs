namespace Phonix.Api.Data;

// Periodically flushes the audit trail to audit_store.json and guarantees a final save on shutdown.
// Independent of StorePersistenceWorker so the two files are written on their own schedules and a slow/large
// audit flush never blocks the main store (and vice-versa).
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

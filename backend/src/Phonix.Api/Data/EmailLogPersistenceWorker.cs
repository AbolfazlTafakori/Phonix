namespace Phonix.Api.Data;

// Periodically flushes the sent-email log to its own file and guarantees a final save on shutdown.
// Mirrors AuditPersistenceWorker: its own file and its own schedule, so a slow email-log write never blocks
// the main store and vice-versa.
public sealed class EmailLogPersistenceWorker : BackgroundService
{
    private readonly EmailLogStore _log;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    public EmailLogPersistenceWorker(EmailLogStore log) => _log = log;

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
            _log.SaveIfChanged();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _log.Save();
        await base.StopAsync(cancellationToken);
    }
}

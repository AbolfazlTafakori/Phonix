namespace Phonix.Api.Data;

// Periodically flushes the sent-email log to its own file and guarantees a final save on shutdown.
// Mirrors AuditPersistenceWorker: its own file and its own schedule, so a slow email-log write never blocks
// the main store and vice-versa.
public sealed class EmailLogPersistenceWorker : BackgroundService
{
    private readonly EmailLogStore _log;
    private readonly ILogger<EmailLogPersistenceWorker> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    public EmailLogPersistenceWorker(EmailLogStore log, ILogger<EmailLogPersistenceWorker> logger)
    {
        _log = log;
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
            // Same reasoning as AuditPersistenceWorker: an escaping exception stops the host, so a failed
            // flush must cost this cycle only, not the site.
            try
            {
                _log.SaveIfChanged();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email log could not be flushed; retrying on the next cycle.");
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            _log.Save();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Final email-log save on shutdown failed.");
        }
        await base.StopAsync(cancellationToken);
    }
}

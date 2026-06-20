namespace Phonix.Api.Data;

// Periodically flushes the store to disk and guarantees a final save on shutdown.
public class StorePersistenceWorker : BackgroundService
{
    private readonly StoreData _store;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    public StorePersistenceWorker(StoreData store) => _store = store;

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
            _store.SaveIfChanged();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _store.Save();
        await base.StopAsync(cancellationToken);
    }
}

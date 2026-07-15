namespace Phonix.Api.Services;

// Drives the receipt bot's inbound side: continuously long-polls Telegram for the admin's approve/reject
// taps and hands each cycle to the service. The poll offset is kept in memory only — losing it on a restart
// is harmless because applying a decision is idempotent (a re-seen tap on an already-decided transaction is
// a no-op), so at worst one recent update is re-examined after a restart.
public class TelegramReceiptWorker : BackgroundService
{
    private readonly ITelegramReceiptService _service;
    private readonly ILogger<TelegramReceiptWorker> _logger;
    // When the bot is disabled or unconfigured there is nothing to poll; idle at this cadence and re-check.
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(15);
    // Backoff after a transient failure so a persistent error (bad token, network down) can't hot-loop.
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(10);

    private long _offset;

    public TelegramReceiptWorker(ITelegramReceiptService service, ILogger<TelegramReceiptWorker> logger)
    {
        _service = service;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var next = await _service.ProcessUpdatesAsync(_offset, stoppingToken);
                if (next == _offset)
                    // Nothing advanced — either the bot is off/unconfigured or the long-poll returned empty.
                    // A short idle keeps CPU flat without adding noticeable latency to a real tap.
                    await Task.Delay(IdleDelay, stoppingToken);
                else
                    _offset = next; // loop straight back into the next long-poll
            }
            catch (TaskCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Telegram receipt poll cycle failed");
                try { await Task.Delay(ErrorDelay, stoppingToken); }
                catch (TaskCanceledException) { break; }
            }
        }
    }
}

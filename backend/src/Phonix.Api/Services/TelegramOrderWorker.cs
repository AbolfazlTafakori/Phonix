namespace Phonix.Api.Services;

// Drives the order bot's inbound side: long-polls Telegram for staff approve/reject taps and their replies
// carrying ready-made accounts. Mirrors TelegramReceiptWorker — the two bots poll independently, each with its
// own token, so neither ever consumes the other's updates. The offset is in memory only; losing it on restart
// is harmless because every store transition it applies is idempotent (a re-seen tap on an already delivered
// account is a no-op), so at worst one recent update is re-examined.
public class TelegramOrderWorker : BackgroundService
{
    private readonly ITelegramOrderService _service;
    private readonly ILogger<TelegramOrderWorker> _logger;
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(10);

    private long _offset;

    public TelegramOrderWorker(ITelegramOrderService service, ILogger<TelegramOrderWorker> logger)
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
                    await Task.Delay(IdleDelay, stoppingToken);
                else
                    _offset = next;
            }
            catch (TaskCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Telegram order poll cycle failed");
                try { await Task.Delay(ErrorDelay, stoppingToken); }
                catch (TaskCanceledException) { break; }
            }
        }
    }
}

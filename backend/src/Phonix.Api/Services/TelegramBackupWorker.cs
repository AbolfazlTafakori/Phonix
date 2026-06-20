using Phonix.Api.Data;

namespace Phonix.Api.Services;

// Periodically ships a full store backup to Telegram when the admin has enabled it and the
// configured interval has elapsed since the last successful send.
public class TelegramBackupWorker : BackgroundService
{
    private readonly StoreData _store;
    private readonly ITelegramBackupSender _sender;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);

    public TelegramBackupWorker(StoreData store, ITelegramBackupSender sender)
    {
        _store = store;
        _sender = sender;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            var settings = _store.GetTelegramSettings();
            if (!settings.BackupEnabled
                || string.IsNullOrWhiteSpace(settings.BotToken)
                || string.IsNullOrWhiteSpace(settings.ChatId))
                continue;

            var interval = TimeSpan.FromHours(Math.Max(1, settings.IntervalHours));
            var last = settings.LastBackupAtUtc ?? DateTime.MinValue;
            if (DateTime.UtcNow - last < interval) continue;

            await _sender.SendAsync("پشتیبان خودکار فونیکس", stoppingToken);
        }
    }
}

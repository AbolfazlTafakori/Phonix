using Phonix.Api.Data;

namespace Phonix.Api.Services;

// Periodically ships a full store backup to Telegram when the admin has enabled it and the
// configured interval has elapsed since the last successful send.
public class TelegramBackupWorker : BackgroundService
{
    private readonly IDataStore _store;
    private readonly ITelegramBackupSender _sender;
    private readonly ILogger<TelegramBackupWorker> _logger;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);

    public TelegramBackupWorker(IDataStore store, ITelegramBackupSender sender, ILogger<TelegramBackupWorker> logger)
    {
        _store = store;
        _sender = sender;
        _logger = logger;
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

            // Everything below is wrapped: an exception escaping a BackgroundService stops the HOST by
            // default, and this loop talks to Telegram — the single least reliable dependency the site has,
            // routinely unreachable from the Iranian node. A backup that cannot be shipped must cost this
            // cycle, never the site.
            try
            {
                var settings = _store.GetTelegramSettings();
                if (!settings.BackupEnabled
                    || string.IsNullOrWhiteSpace(settings.BotToken)
                    || string.IsNullOrWhiteSpace(settings.ChatId))
                    continue;

                var interval = TimeSpan.FromHours(Math.Max(1, settings.IntervalHours));
                var last = settings.LastBackupAtUtc ?? DateTime.MinValue;
                if (DateTime.UtcNow - last < interval) continue;

                // each section as its own (small) file, so none hits Telegram's size limit.
                foreach (var (section, label) in BackupSections.All)
                {
                    await _sender.SendSectionAsync(section, $"پشتیبان خودکار فونیکس — {label}", stoppingToken);
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); // gentle on Telegram rate limits
                }

                // Uploaded files too, kept separate: public site media, then users' (encrypted) documents. Large
                // archives are auto-split into parts under Telegram's per-file limit by the sender.
                await _sender.SendMediaAsync(sensitive: false, "پشتیبان خودکار فونیکس — رسانهٔ سایت", stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                await _sender.SendMediaAsync(sensitive: true, "پشتیبان خودکار فونیکس — مدارک کاربران", stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // ordinary shutdown, not a failure
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Automatic Telegram backup failed; retrying on the next check.");
            }
        }
    }
}

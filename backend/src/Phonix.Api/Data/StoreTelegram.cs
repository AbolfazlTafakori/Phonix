using Phonix.Api.Models;

namespace Phonix.Api.Data;

public partial class StoreData
{
    private TelegramSettings _telegramSettings = new();

    public TelegramSettings GetTelegramSettings()
    {
        lock (_gate) return _telegramSettings;
    }

    // only the user-editable fields are overwritten; the runtime status is preserved.
    public void UpdateTelegramSettings(TelegramSettings settings)
    {
        lock (_gate)
        {
            _telegramSettings.BackupEnabled = settings.BackupEnabled;
            _telegramSettings.AlertsEnabled = settings.AlertsEnabled;
            _telegramSettings.ReceiptBotEnabled = settings.ReceiptBotEnabled;
            _telegramSettings.BotToken = (settings.BotToken ?? "").Trim();
            _telegramSettings.ChatId = (settings.ChatId ?? "").Trim();
            _telegramSettings.ReceiptBotToken = (settings.ReceiptBotToken ?? "").Trim();
            _telegramSettings.ReceiptChatId = (settings.ReceiptChatId ?? "").Trim();
            _telegramSettings.OrderBotEnabled = settings.OrderBotEnabled;
            _telegramSettings.OrderBotToken = (settings.OrderBotToken ?? "").Trim();
            _telegramSettings.OrderChatId = (settings.OrderChatId ?? "").Trim();
            _telegramSettings.IntervalHours = settings.IntervalHours < 1 ? 1 : settings.IntervalHours;
            // the previous error referred to the old config, so it no longer applies (the last
            // successful backup time is kept as historical info).
            _telegramSettings.LastBackupError = "";
        }
    }

    public void RecordTelegramBackup(bool success, string error)
    {
        lock (_gate)
        {
            if (success) _telegramSettings.LastBackupAtUtc = DateTime.UtcNow;
            _telegramSettings.LastBackupError = success ? "" : error;
        }
    }
}

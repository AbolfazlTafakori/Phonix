namespace Phonix.Api.Models;

public class TelegramSettings
{
    public bool BackupEnabled { get; set; }
    // when on (and bot token + chat id are set), the app pushes error/startup alerts to the same chat.
    public bool AlertsEnabled { get; set; }
    public string BotToken { get; set; } = "";
    public string ChatId { get; set; } = "";
    public int IntervalHours { get; set; } = 24;

    // runtime status, written by the backup worker / test send (not edited directly in the form)
    public DateTime? LastBackupAtUtc { get; set; }
    public string LastBackupError { get; set; } = "";
}

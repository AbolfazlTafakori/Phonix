namespace Phonix.Api.Models;

public class TelegramSettings
{
    public bool BackupEnabled { get; set; }
    // when on (and bot token + chat id are set), the app pushes error/startup alerts to the same chat.
    public bool AlertsEnabled { get; set; }
    // when on (and the receipt bot token + chat id below are set), new deposit receipts are pushed to the
    // admin chat with inline approve/reject buttons, and the admin's tap is applied back from Telegram.
    public bool ReceiptBotEnabled { get; set; }
    public string BotToken { get; set; } = "";
    public string ChatId { get; set; } = "";
    // The receipt bot uses its OWN token + chat, kept fully separate from the backup/alerts bot above so the
    // two never share a chat or interfere (only the receipt bot long-polls for button taps).
    public string ReceiptBotToken { get; set; } = "";
    public string ReceiptChatId { get; set; } = "";
    public int IntervalHours { get; set; } = 24;

    // runtime status, written by the backup worker / test send (not edited directly in the form)
    public DateTime? LastBackupAtUtc { get; set; }
    public string LastBackupError { get; set; } = "";
}

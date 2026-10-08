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

    // when on (and the order bot token + chat id below are set), each purchased account of an approved order
    // is pushed to the orders group as its own message with inline approve/reject buttons.
    public bool OrderBotEnabled { get; set; }
    // A THIRD independent bot + chat: the orders group is a different room from the receipts one, and only
    // this bot long-polls it, so the two never read each other's button taps.
    public string OrderBotToken { get; set; } = "";
    public string OrderChatId { get; set; } = "";

    // A FOURTH bot, this one for customers: they link their account to it and receive their account mail
    // (deliveries, payment decisions, rejections, staff messages) in Telegram as well. Managed on its own panel
    // page, not through the form above.
    public bool CustomerBotEnabled { get; set; }
    // Whether customers see «اتصال به تلگرام» in their account. Off until staff have checked the bot works —
    // the bot can run and be tested without anyone being offered it yet.
    public bool CustomerBotPublic { get; set; }
    // The shop inside Telegram (Mini App): the bot's menu button and /start open the site in Telegram, and a
    // linked customer is signed in there automatically. Its own switch, off until staff have tried it.
    public bool CustomerBotShop { get; set; }
    // Buying inside the bot itself: the products marked for it can be bought there by anyone. Off by default.
    public bool CustomerBotSales { get; set; }
    // Running the shop from the bot: staff who linked their own account see «مدیریت فروشگاه» in its menu,
    // limited to the panel sections each of them holds. Off until an Admin turns it on.
    public bool CustomerBotAdmin { get; set; }
    // The bot's premium emoji: for each plain emoji a button starts with, the custom emoji Telegram shows in its
    // place. Telegram shows them only when the bot's owner has Telegram Premium, so they have their own switch.
    // Set from inside the bot by an Admin (management → ظاهر ربات).
    public bool CustomerBotPremium { get; set; }
    public Dictionary<string, string> CustomerBotEmoji { get; set; } = new();
    // How long the one-time code mailed for linking stays valid, in minutes (set in the panel).
    public int CustomerBotCodeMinutes { get; set; } = 15;
    public string CustomerBotToken { get; set; } = "";
    // The bot's @username, read from Telegram when the token is saved; it builds the t.me link customers open.
    public string CustomerBotUsername { get; set; } = "";

    // A FIFTH bot, for support: tickets and live chat are posted to a staff group, and staff answer them there by
    // replying. Managed on its own panel page.
    public bool SupportBotEnabled { get; set; }
    public string SupportBotToken { get; set; } = "";
    public string SupportBotUsername { get; set; } = "";
    public string SupportChatId { get; set; } = "";
    public int IntervalHours { get; set; } = 24;

    // runtime status, written by the backup worker / test send (not edited directly in the form)
    public DateTime? LastBackupAtUtc { get; set; }
    public string LastBackupError { get; set; } = "";
}

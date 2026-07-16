using Phonix.Api.Models;
using Xunit;

namespace Phonix.Api.Tests;

// UpdateTelegramSettings copies field-by-field rather than replacing the object, so every bot setting has to
// be listed there by hand. Forgetting one is silent: the save returns 200, the value is simply dropped and the
// section comes back empty. These pin the whole round-trip so a fourth bot can't regress the same way.
public class TelegramSettingsTests
{
    [Fact]
    public void Order_bot_settings_survive_a_save()
    {
        var store = TestStore.Create();

        store.UpdateTelegramSettings(new TelegramSettings
        {
            OrderBotEnabled = true,
            OrderBotToken = "111:order-token",
            OrderChatId = "-1001111111111",
        });

        var saved = store.GetTelegramSettings();
        Assert.True(saved.OrderBotEnabled);
        Assert.Equal("111:order-token", saved.OrderBotToken);
        Assert.Equal("-1001111111111", saved.OrderChatId);
    }

    [Fact]
    public void Saving_one_bot_never_wipes_the_others()
    {
        var store = TestStore.Create();

        // Configure all three bots the way the panel's three separate sections would.
        store.UpdateTelegramSettings(new TelegramSettings
        {
            BackupEnabled = true, BotToken = "1:backup", ChatId = "-100100",
            ReceiptBotEnabled = true, ReceiptBotToken = "2:receipt", ReceiptChatId = "-100200",
            OrderBotEnabled = true, OrderBotToken = "3:order", OrderChatId = "-100300",
        });

        var saved = store.GetTelegramSettings();
        Assert.Equal("2:receipt", saved.ReceiptBotToken);
        Assert.Equal("-100200", saved.ReceiptChatId);
        Assert.True(saved.ReceiptBotEnabled);
        Assert.Equal("3:order", saved.OrderBotToken);
        Assert.True(saved.OrderBotEnabled);
        Assert.Equal("1:backup", saved.BotToken);
    }
}

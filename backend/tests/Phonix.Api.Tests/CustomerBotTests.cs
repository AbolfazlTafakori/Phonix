using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Phonix.Api.Controllers;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Services;
using Xunit;

namespace Phonix.Api.Tests;

// The customer bot: a customer links their account with a one-time link from their account page, then gets
// their account mail in Telegram too. Driven against a scripted Telegram.
public class CustomerBotTests
{
    private const string Token = TelegramLaunch.Token;
    private const long Chat = 777001;

    // Telegram opens a Mini App only over https, so the shop exists only when the site's address is https.
    static CustomerBotTests() => Environment.SetEnvironmentVariable("PHONIX_FRONTEND_URL", "https://shop.test");

    private sealed class FakeTelegram : HttpMessageHandler
    {
        public List<(string Method, string Body)> Calls { get; } = new();
        public Queue<string> Updates { get; } = new();
        // Chats that blocked the bot: sendMessage to them answers 403, like the real API.
        public HashSet<long> Blocked { get; } = new();
        // People who never pressed Start: the bot may not write first, also a 403.
        public HashSet<long> NeverStarted { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var method = request.RequestUri!.AbsolutePath.Split('/').Last();
            var body = request.Content is null ? request.RequestUri.Query : await request.Content.ReadAsStringAsync(ct);
            var decoded = Uri.UnescapeDataString(body.Replace('+', ' '));
            lock (Calls) Calls.Add((method, decoded));
            if (method == "sendMessage" && Blocked.Any(b => decoded.Contains($"chat_id={b}")))
                return new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent("{\"ok\":false,\"description\":\"Forbidden: bot was blocked by the user\"}"),
                };
            if (method == "sendMessage" && NeverStarted.Any(b => decoded.Contains($"chat_id={b}")))
                return new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent("{\"ok\":false,\"description\":\"Forbidden: bot can't initiate conversation with a user\"}"),
                };
            var result = method switch
            {
                "getUpdates" => $"[{(Updates.Count > 0 ? Updates.Dequeue() : "")}]",
                "getMe" => "{\"id\":1,\"is_bot\":true,\"username\":\"PhoenixTestBot\"}",
                _ => "{\"message_id\":1}",
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{{\"ok\":true,\"result\":{result}}}") };
        }
    }

    private sealed class Factory(HttpMessageHandler h) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(h, disposeHandler: false);
    }

    private sealed class Outbox : IEmailSender
    {
        public List<string> To { get; } = new();
        public List<string> Bodies { get; } = new();
        public Task<bool> SendAsync(string to, string subject, string body, string? htmlBody = null)
        {
            To.Add(to);
            Bodies.Add(body);
            return Task.FromResult(true);
        }
    }

    // The code as the customer finds it in the email: six groups of four digits.
    private static string MailedCode(Outbox outbox) =>
        System.Text.RegularExpressions.Regex.Match(outbox.Bodies.Last(), @"\d{4}( \d{4}){5}").Value;

    private static (IDataStore Store, FakeTelegram Telegram, TelegramCustomerBot Bot) Setup(bool enabled = true, bool isPublic = true)
    {
        var store = TestStore.Create();
        store.SetCustomerBot(enabled, isPublic, Token, "PhoenixTestBot");
        var telegram = new FakeTelegram();
        return (store, telegram, new TelegramCustomerBot(store, new Factory(telegram), NullLogger<TelegramCustomerBot>.Instance));
    }

    private static string Message(string text, long chatId = Chat, string chatType = "private") =>
        JsonSerializer.Serialize(new
        {
            update_id = Random.Shared.Next(1, 1_000_000),
            message = new { message_id = 5, text, from = new { id = chatId, username = "ali_tg" }, chat = new { id = chatId, type = chatType } },
        });

    private static string Code(IDataStore store, int userId) =>
        store.CreateTelegramLinkCode(userId, TimeSpan.FromMinutes(15));

    [Fact]
    public async Task Sending_the_emailed_code_ties_the_chat_to_the_account()
    {
        var (store, telegram, bot) = Setup();
        var code = Code(store, 1);
        Assert.Matches(@"^\d{24}$", code);
        telegram.Updates.Enqueue(Message(code));

        await bot.ProcessUpdatesAsync(0);

        var user = store.GetUser(1)!;
        Assert.Equal(Chat, user.TelegramChatId);
        Assert.Equal("ali_tg", user.TelegramUsername);
        Assert.Contains(telegram.Calls, c => c.Method == "sendMessage" && c.Body.Contains("وصل شد"));
    }

    // Copied from the email it is grouped; typed on a Persian keyboard it is in Persian digits. Same code.
    [Fact]
    public async Task The_code_is_accepted_grouped_and_in_persian_digits()
    {
        var (store, telegram, bot) = Setup();
        var code = Code(store, 1);
        var persian = string.Concat(code.Select(c => (char)('\u06F0' + (c - '0'))));
        var grouped = string.Join(" - ", Enumerable.Range(0, 6).Select(i => persian.Substring(i * 4, 4)));
        telegram.Updates.Enqueue(Message(grouped));

        await bot.ProcessUpdatesAsync(0);

        Assert.Equal(Chat, store.GetUser(1)!.TelegramChatId);
    }

    [Fact]
    public async Task A_code_works_once_and_only_the_newest_one_works()
    {
        var (store, telegram, bot) = Setup();
        var older = Code(store, 1);
        var newer = Code(store, 1);

        telegram.Updates.Enqueue(Message(older, chatId: 701));
        await bot.ProcessUpdatesAsync(0);
        Assert.Null(store.GetUser(1)!.TelegramChatId);

        telegram.Updates.Enqueue(Message(newer, chatId: 702));
        await bot.ProcessUpdatesAsync(0);
        Assert.Equal(702, store.GetUser(1)!.TelegramChatId);

        // Already spent: a second chat can't use it to take the account over.
        telegram.Updates.Enqueue(Message(newer, chatId: 703));
        await bot.ProcessUpdatesAsync(0);
        Assert.Equal(702, store.GetUser(1)!.TelegramChatId);
    }

    [Fact]
    public async Task An_expired_code_links_nothing()
    {
        var (store, telegram, bot) = Setup();
        var code = store.CreateTelegramLinkCode(1, TimeSpan.FromSeconds(-1));
        telegram.Updates.Enqueue(Message(code, chatId: 704));

        await bot.ProcessUpdatesAsync(0);

        Assert.Null(store.GetUser(1)!.TelegramChatId);
        Assert.Contains(telegram.Calls, c => c.Body.Contains("منقضی"));
    }

    [Fact]
    public async Task Too_many_wrong_codes_lock_the_chat_for_a_while()
    {
        var (store, telegram, bot) = Setup();
        const long chat = 705;
        for (var i = 0; i < 5; i++)
        {
            telegram.Updates.Enqueue(Message(new string((char)('1' + i), 24), chatId: chat));
            await bot.ProcessUpdatesAsync(0);
        }
        // Even the right code is turned away until the window passes.
        telegram.Updates.Enqueue(Message(Code(store, 1), chatId: chat));
        await bot.ProcessUpdatesAsync(0);

        Assert.Null(store.GetUser(1)!.TelegramChatId);
        Assert.Contains("زیاد بود", telegram.Calls.Last(c => c.Method == "sendMessage").Body);
    }

    [Fact]
    public async Task Arriving_from_the_site_the_bot_asks_for_the_code()
    {
        var (_, telegram, bot) = Setup();
        telegram.Updates.Enqueue(Message("/start connect", chatId: 706));

        await bot.ProcessUpdatesAsync(0);

        var reply = telegram.Calls.Last(c => c.Method == "sendMessage").Body;
        Assert.Contains("ایمیل", reply);
        Assert.Contains("۲۴", reply);
    }

    [Fact]
    public async Task A_code_cut_short_gets_a_hint_not_a_failure()
    {
        var (_, telegram, bot) = Setup();
        telegram.Updates.Enqueue(Message("1234 5678 9012", chatId: 707));

        await bot.ProcessUpdatesAsync(0);

        Assert.Contains("کامل", telegram.Calls.Last(c => c.Method == "sendMessage").Body);
    }

    [Fact]
    public async Task Group_chats_are_ignored()
    {
        var (store, telegram, bot) = Setup();
        telegram.Updates.Enqueue(Message(Code(store, 1), chatId: -100123, chatType: "group"));

        await bot.ProcessUpdatesAsync(0);

        Assert.Null(store.GetUser(1)!.TelegramChatId);
        Assert.DoesNotContain(telegram.Calls, c => c.Method == "sendMessage");
    }

    [Fact]
    public async Task Stop_unlinks()
    {
        var (store, telegram, bot) = Setup();
        store.LinkTelegram(1, Chat, "ali_tg");
        telegram.Updates.Enqueue(Message("/stop"));

        await bot.ProcessUpdatesAsync(0);

        Assert.Null(store.GetUser(1)!.TelegramChatId);
    }

    [Fact]
    public void A_chat_linked_to_a_second_account_leaves_the_first()
    {
        var store = TestStore.Create();
        var other = store.GetUsers().First(u => u.Id != 1 && u.Role == UserRole.Customer).Id;
        store.LinkTelegram(1, Chat, "ali_tg");

        store.LinkTelegram(other, Chat, "ali_tg");

        Assert.Null(store.GetUser(1)!.TelegramChatId);
        Assert.Equal(Chat, store.GetUser(other)!.TelegramChatId);
    }

    [Fact]
    public async Task Account_mail_also_goes_to_the_linked_chat()
    {
        var (store, telegram, bot) = Setup();
        store.LinkTelegram(1, Chat, "ali_tg");
        var outbox = new Outbox();
        var mailer = new UserMailer(store, outbox, NullLogger<UserMailer>.Instance, bot);

        await mailer.StaffMessageAsync(1, "تمدید اشتراک", "اشتراک شما فردا تمام می‌شود.", "/account/orders");

        Assert.Contains(store.GetUser(1)!.Email, outbox.To);
        var tg = Assert.Single(telegram.Calls, c => c.Method == "sendMessage");
        Assert.Contains($"chat_id={Chat}", tg.Body);
        Assert.Contains("تمدید اشتراک", tg.Body);
        Assert.Contains("اشتراک شما فردا تمام می‌شود.", tg.Body);
    }

    [Fact]
    public async Task Nothing_goes_to_telegram_when_the_customer_turned_it_off_or_the_bot_is_off()
    {
        var (store, telegram, bot) = Setup();
        store.LinkTelegram(1, Chat, "ali_tg");
        store.UpdateUser(1, u => u.TelegramNotify = false);
        var mailer = new UserMailer(store, new Outbox(), NullLogger<UserMailer>.Instance, bot);
        await mailer.StaffMessageAsync(1, "t", "b", null);

        store.UpdateUser(1, u => u.TelegramNotify = true);
        store.SetCustomerBot(false, false, null, null);
        await mailer.StaffMessageAsync(1, "t", "b", null);

        Assert.DoesNotContain(telegram.Calls, c => c.Method == "sendMessage");
    }

    [Fact]
    public async Task A_customer_who_blocked_the_bot_is_unlinked()
    {
        var (store, telegram, bot) = Setup();
        store.LinkTelegram(1, Chat, "ali_tg");
        telegram.Blocked.Add(Chat);

        Assert.False(await bot.SendAsync(Chat, "hello"));

        Assert.Null(store.GetUser(1)!.TelegramChatId);
    }

    // ── controllers ──

    private static T As<T>(T controller, AppUser user) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Role, user.Role.ToString()),
                    new Claim(ClaimTypes.Name, user.Username),
                }, "test")),
            },
        };
        return controller;
    }

    private static AccountTelegramController Account(IDataStore store, TelegramCustomerBot bot, Outbox outbox, int userId = 1) =>
        As(new AccountTelegramController(store, bot, new UserMailer(store, outbox, NullLogger<UserMailer>.Instance, bot),
            new MemoryCache(new MemoryCacheOptions())), store.GetUser(userId)!);

    [Fact]
    public async Task Customers_are_offered_nothing_until_staff_switch_it_on()
    {
        var (store, _, bot) = Setup(enabled: true, isPublic: false);
        var outbox = new Outbox();
        var account = Account(store, bot, outbox);

        Assert.False(account.Get().Value!.Available);
        Assert.IsType<BadRequestObjectResult>((await account.SendCode()).Result);
        Assert.Empty(outbox.To);

        store.SetCustomerBot(true, true, null, null);
        Assert.True(account.Get().Value!.Available);
        var sent = (await account.SendCode()).Value!;
        Assert.Equal("https://t.me/PhoenixTestBot?start=connect", sent.BotUrl);
        Assert.Equal(15, sent.Minutes);
    }

    // The whole flow: the code goes to the account's own inbox, and sending it to the bot links the chat.
    [Fact]
    public async Task The_code_goes_to_the_accounts_email_and_links_from_the_bot()
    {
        var (store, telegram, bot) = Setup();
        store.SetCustomerBot(true, true, null, null, codeMinutes: 30);
        var outbox = new Outbox();

        var sent = (await Account(store, bot, outbox).SendCode()).Value!;

        Assert.Equal(store.GetUser(1)!.Email, Assert.Single(outbox.To));
        Assert.Equal(30, sent.Minutes);
        Assert.DoesNotContain(telegram.Calls, c => c.Method == "sendMessage");   // email only, never a chat
        telegram.Updates.Enqueue(Message(MailedCode(outbox), chatId: 801));
        await bot.ProcessUpdatesAsync(0);
        Assert.Equal(801, store.GetUser(1)!.TelegramChatId);
    }

    // The code proves the inbox, so the inbox has to be proven the owner's first.
    [Fact]
    public async Task No_code_goes_to_an_unverified_address()
    {
        var (store, _, bot) = Setup();
        store.UpdateUser(1, u => u.EmailVerified = false);
        var outbox = new Outbox();

        Assert.IsType<BadRequestObjectResult>((await Account(store, bot, outbox).SendCode()).Result);
        Assert.Empty(outbox.To);
    }

    [Fact]
    public async Task Codes_cannot_be_requested_back_to_back()
    {
        var (store, _, bot) = Setup();
        var outbox = new Outbox();
        var account = Account(store, bot, outbox);

        Assert.NotNull((await account.SendCode()).Value);
        var again = Assert.IsType<ObjectResult>((await account.SendCode()).Result);

        Assert.Equal(429, again.StatusCode);
        Assert.Single(outbox.To);
    }

    [Fact]
    public void The_panel_sets_how_long_a_code_lives_within_bounds()
    {
        var (store, _, _) = Setup();
        store.SetCustomerBot(true, true, null, null, codeMinutes: 5);
        Assert.Equal(5, store.GetTelegramSettings().CustomerBotCodeMinutes);

        store.SetCustomerBot(true, true, null, null, codeMinutes: 99_999);
        Assert.Equal(1440, store.GetTelegramSettings().CustomerBotCodeMinutes);
    }

    [Fact]
    public async Task The_panel_never_sends_the_token_back_and_can_remove_it()
    {
        var (store, _, bot) = Setup();
        var panel = As(new CustomerBotController(store, bot), store.GetUsers().First(u => u.Role == UserRole.Admin));

        var status = panel.Get();
        Assert.True(status.HasToken);
        Assert.DoesNotContain("AAFakeTokenForTestsOnly", JsonSerializer.Serialize(status));
        Assert.Equal("PhoenixTestBot", status.Username);

        var removed = await panel.RemoveToken();
        Assert.False(removed.HasToken);
        Assert.False(removed.Enabled);
        Assert.False(removed.Public);
        Assert.IsType<BadRequestObjectResult>((await panel.Save(new CustomerBotInput(true, true, null), default)).Result);
    }

    [Fact]
    public async Task A_malformed_token_is_refused_before_it_is_saved()
    {
        var (store, _, bot) = Setup();
        store.SetCustomerBot(false, false, "", null);
        var panel = As(new CustomerBotController(store, bot), store.GetUsers().First(u => u.Role == UserRole.Admin));

        var result = await panel.Save(new CustomerBotInput(true, false, "not-a-token"), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.False(panel.Get().HasToken);
    }

    [Fact]
    public async Task A_valid_token_is_checked_with_telegram_and_its_username_kept()
    {
        var (store, _, bot) = Setup();
        store.SetCustomerBot(false, false, "", null);
        var panel = As(new CustomerBotController(store, bot), store.GetUsers().First(u => u.Role == UserRole.Admin));

        var saved = Assert.IsType<CustomerBotStatusDto>((await panel.Save(new CustomerBotInput(true, false, Token), default)).Value);

        Assert.True(saved.HasToken);
        Assert.True(saved.Enabled);
        Assert.False(saved.Public); // running, not yet offered to customers
        Assert.Equal("PhoenixTestBot", saved.Username);
    }

    // ── the shop inside Telegram ──

    [Fact]
    public async Task Switching_the_shop_on_and_off_moves_the_menu_button()
    {
        var (store, telegram, bot) = Setup();
        var panel = As(new CustomerBotController(store, bot), store.GetUsers().First(u => u.Role == UserRole.Admin));

        var on = Assert.IsType<CustomerBotStatusDto>((await panel.Save(new CustomerBotInput(true, false, null, Shop: true), default)).Value);
        Assert.True(on.Shop);
        Assert.Null(on.Warning);
        var set = Assert.Single(telegram.Calls, c => c.Method == "setChatMenuButton");
        Assert.Contains("\"type\":\"web_app\"", set.Body);
        Assert.Contains("https://shop.test/", set.Body);

        var off = Assert.IsType<CustomerBotStatusDto>((await panel.Save(new CustomerBotInput(true, false, null, Shop: false), default)).Value);
        Assert.False(off.Shop);
        Assert.Contains("\"type\":\"default\"", telegram.Calls.Last(c => c.Method == "setChatMenuButton").Body);
    }

    [Fact]
    public void The_shop_goes_off_with_the_bot()
    {
        var (store, _, _) = Setup();
        store.SetCustomerBot(true, false, null, null, shop: true);
        Assert.True(store.GetTelegramSettings().CustomerBotShop);

        store.SetCustomerBot(false, false, null, null);

        Assert.False(store.GetTelegramSettings().CustomerBotShop);
        Assert.Null(TelegramCustomerBot.ShopToken(store));
    }

    [Fact]
    public async Task Start_offers_the_shop_button_only_while_the_shop_is_on()
    {
        var (store, telegram, bot) = Setup();
        telegram.Updates.Enqueue(Message("/start"));
        await bot.ProcessUpdatesAsync(0);
        Assert.DoesNotContain("web_app", telegram.Calls.Last(c => c.Method == "sendMessage").Body);

        store.SetCustomerBot(true, true, null, null, shop: true);
        telegram.Updates.Enqueue(Message("/start"));
        await bot.ProcessUpdatesAsync(0);

        var reply = telegram.Calls.Last(c => c.Method == "sendMessage").Body;
        Assert.Contains("\"web_app\":{\"url\":\"https://shop.test/\"}", reply);
    }

    [Fact]
    public async Task Inside_the_shop_a_signed_in_customer_links_that_telegram_with_the_mailed_code()
    {
        var (store, telegram, bot) = Setup(isPublic: false);
        store.SetCustomerBot(true, false, null, null, shop: true);
        var outbox = new Outbox();
        var account = Account(store, bot, outbox);
        Assert.True(account.Get().Value!.Shop);
        await account.SendCode();

        var result = await account.LinkFromShop(new TelegramShopLinkInput(TelegramLaunch.InitData(Chat), MailedCode(outbox)), default);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(Chat, store.GetUser(1)!.TelegramChatId);
        Assert.Equal("ali_tg", store.GetUser(1)!.TelegramUsername);
        Assert.Contains(telegram.Calls, c => c.Method == "sendMessage" && c.Body.Contains($"chat_id={Chat}"));
    }

    [Fact]
    public async Task Linking_from_the_shop_needs_genuine_launch_data_and_this_accounts_code()
    {
        var (store, _, bot) = Setup();
        var account = Account(store, bot, new Outbox());
        var genuine = TelegramLaunch.InitData(Chat);

        // Shop off: nothing to link from.
        Assert.IsType<BadRequestObjectResult>(await account.LinkFromShop(new TelegramShopLinkInput(genuine, Code(store, 1)), default));

        store.SetCustomerBot(true, true, null, null, shop: true);
        var stale = TelegramLaunch.InitData(Chat, signedAt: DateTime.UtcNow.AddHours(-3));
        var forged = TelegramLaunch.InitData(Chat, token: "987654321:BBOtherBotTokenForTests_abcdefghijklmno");
        Assert.IsType<BadRequestObjectResult>(await account.LinkFromShop(new TelegramShopLinkInput(stale, Code(store, 1)), default));
        Assert.IsType<BadRequestObjectResult>(await account.LinkFromShop(new TelegramShopLinkInput(forged, Code(store, 1)), default));
        // Genuine launch data, but no code — or a code mailed to someone else.
        Assert.IsType<BadRequestObjectResult>(await account.LinkFromShop(new TelegramShopLinkInput(genuine, ""), default));
        var someoneElse = store.GetUsers().First(u => u.Id != 1 && u.Role == UserRole.Customer).Id;
        Assert.IsType<BadRequestObjectResult>(await account.LinkFromShop(new TelegramShopLinkInput(genuine, Code(store, someoneElse)), default));
        Assert.Null(store.GetUser(1)!.TelegramChatId);
    }

    // Someone who linked from the shop without ever pressing Start can't be written to first — that is not a
    // reason to throw their link (and with it their sign-in) away. Blocking the bot still is.
    [Fact]
    public async Task A_customer_who_never_pressed_start_stays_linked()
    {
        var (store, telegram, bot) = Setup();
        store.LinkTelegram(1, Chat, "ali_tg");
        telegram.NeverStarted.Add(Chat);

        Assert.False(await bot.SendAsync(Chat, "hello"));

        Assert.Equal(Chat, store.GetUser(1)!.TelegramChatId);
    }

    [Fact]
    public async Task Removing_the_token_puts_the_menu_button_back_first()
    {
        var (store, telegram, bot) = Setup();
        store.SetCustomerBot(true, true, null, null, shop: true);
        var panel = As(new CustomerBotController(store, bot), store.GetUsers().First(u => u.Role == UserRole.Admin));

        var removed = await panel.RemoveToken();

        Assert.False(removed.Shop);
        Assert.False(removed.HasToken);
        Assert.Contains("\"type\":\"default\"", Assert.Single(telegram.Calls, c => c.Method == "setChatMenuButton").Body);
    }
}

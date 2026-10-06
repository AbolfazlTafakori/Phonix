using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
    private const string Token = "123456789:AAFakeTokenForTestsOnly_abcdefghijklmnop";
    private const long Chat = 777001;

    private sealed class FakeTelegram : HttpMessageHandler
    {
        public List<(string Method, string Body)> Calls { get; } = new();
        public Queue<string> Updates { get; } = new();
        // Chats that blocked the bot: sendMessage to them answers 403, like the real API.
        public HashSet<long> Blocked { get; } = new();

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
        public Task<bool> SendAsync(string to, string subject, string body, string? htmlBody = null) { To.Add(to); return Task.FromResult(true); }
    }

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

    private static string LinkToken(IDataStore store, int userId) =>
        store.CreateToken(userId, TelegramCustomerBot.LinkPurpose, TelegramCustomerBot.LinkLifetime);

    [Fact]
    public async Task Opening_the_link_ties_the_chat_to_the_account()
    {
        var (store, telegram, bot) = Setup();
        telegram.Updates.Enqueue(Message($"/start {LinkToken(store, 1)}"));

        await bot.ProcessUpdatesAsync(0);

        var user = store.GetUser(1)!;
        Assert.Equal(Chat, user.TelegramChatId);
        Assert.Equal("ali_tg", user.TelegramUsername);
        Assert.Contains(telegram.Calls, c => c.Method == "sendMessage" && c.Body.Contains("وصل شد"));
    }

    [Fact]
    public async Task A_link_works_once()
    {
        var (store, telegram, bot) = Setup();
        var token = LinkToken(store, 1);
        telegram.Updates.Enqueue(Message($"/start {token}"));
        await bot.ProcessUpdatesAsync(0);
        store.UnlinkTelegram(1);

        telegram.Updates.Enqueue(Message($"/start {token}", chatId: 888));
        await bot.ProcessUpdatesAsync(0);

        Assert.Null(store.GetUser(1)!.TelegramChatId);
        Assert.Contains(telegram.Calls, c => c.Body.Contains("نامعتبر"));
    }

    [Fact]
    public async Task A_guessed_or_made_up_start_value_links_nothing()
    {
        var (store, telegram, bot) = Setup();
        telegram.Updates.Enqueue(Message("/start " + new string('A', 64)));

        await bot.ProcessUpdatesAsync(0);

        Assert.DoesNotContain(store.GetUsers(), u => u.TelegramChatId is not null);
    }

    [Fact]
    public async Task Group_chats_are_ignored()
    {
        var (store, telegram, bot) = Setup();
        telegram.Updates.Enqueue(Message($"/start {LinkToken(store, 1)}", chatId: -100123, chatType: "group"));

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

    [Fact]
    public void Customers_are_offered_nothing_until_staff_switch_it_on()
    {
        var (store, _, bot) = Setup(enabled: true, isPublic: false);
        var account = As(new AccountTelegramController(store, bot), store.GetUser(1)!);

        Assert.False(account.Get().Value!.Available);
        Assert.IsType<BadRequestObjectResult>(account.Link());

        store.SetCustomerBot(true, true, null, null);
        Assert.True(account.Get().Value!.Available);
        var url = Assert.IsType<OkObjectResult>(account.Link()).Value!.ToString()!;
        Assert.Contains("https://t.me/PhoenixTestBot?start=", url);
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

        var removed = panel.RemoveToken();
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
}

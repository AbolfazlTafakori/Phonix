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

// The support bot: tickets and live chat posted to a staff group, answered there by replying. Driven against a
// scripted Telegram.
public class SupportBotTests
{
    private const string Token = "555666777:AASupportBotTokenForTestsOnly_abcdefghij";
    private const string Group = "-1009988776655";

    private sealed class FakeTelegram : HttpMessageHandler
    {
        public List<(string Method, string Body)> Calls { get; } = new();
        public Queue<string> Updates { get; } = new();
        private int _nextMessageId = 300;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var method = request.RequestUri!.AbsolutePath.Split('/').Last();
            var body = request.Content is null ? request.RequestUri.Query : await request.Content.ReadAsStringAsync(ct);
            var decoded = Uri.UnescapeDataString(body.Replace('+', ' '));
            lock (Calls) Calls.Add((method, decoded));
            var result = method switch
            {
                "getUpdates" => $"[{(Updates.Count > 0 ? Updates.Dequeue() : "")}]",
                "getMe" => "{\"id\":2,\"is_bot\":true,\"username\":\"PhoenixSupportBot\"}",
                "sendMessage" => $"{{\"message_id\":{_nextMessageId++}}}",
                _ => "true",
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
        public List<(string To, string Subject)> Sent { get; } = new();
        public Task<bool> SendAsync(string to, string subject, string body, string? htmlBody = null)
        {
            lock (Sent) Sent.Add((to, subject));
            return Task.FromResult(true);
        }
    }

    private sealed record Rig(IDataStore Store, FakeTelegram Telegram, Outbox Outbox, TelegramSupportBot Bot);

    private static Rig Setup()
    {
        var store = TestStore.Create();
        store.SetSupportBot(true, Token, "PhoenixSupportBot", Group);
        var telegram = new FakeTelegram();
        var outbox = new Outbox();
        var bot = new TelegramSupportBot(store, new UserMailer(store, outbox, NullLogger<UserMailer>.Instance),
            new Factory(telegram), NullLogger<TelegramSupportBot>.Instance);
        return new Rig(store, telegram, outbox, bot);
    }

    // A staff member replying, in the group, to one of the bot's messages.
    private static string ReplyTo(string repliedText, string answer, string chatId = Group) =>
        JsonSerializer.Serialize(new
        {
            update_id = Random.Shared.Next(1, 1_000_000),
            message = new
            {
                message_id = 900, text = answer, from = new { id = 42, username = "maryam" }, chat = new { id = long.Parse(chatId), type = "supergroup" },
                reply_to_message = new { message_id = 300, text = repliedText, from = new { id = 2, is_bot = true } },
            },
        });

    private static string Tap(string data, string chatId = Group) =>
        JsonSerializer.Serialize(new
        {
            update_id = Random.Shared.Next(1, 1_000_000),
            callback_query = new { id = "cb", data, from = new { id = 42 }, message = new { message_id = 300, chat = new { id = long.Parse(chatId) } } },
        });

    private static Ticket OpenTicket(IDataStore store) =>
        store.CreateTicket(1, "علی محمدی", "اکانت کار نمی‌کند", "فنی", "سلام، اکانت نتفلیکس وارد نمی‌شود.", TicketPriority.High, "");

    [Fact]
    public async Task A_new_ticket_goes_to_the_group_with_its_tag_and_a_close_button()
    {
        var r = Setup();
        var ticket = OpenTicket(r.Store);

        await r.Bot.NotifyTicketOpenedAsync(ticket);

        var post = Assert.Single(r.Telegram.Calls, c => c.Method == "sendMessage");
        Assert.Contains($"chat_id={Group}", post.Body);
        Assert.Contains(ticket.Code, post.Body);
        Assert.Contains("اکانت نتفلیکس وارد نمی‌شود", post.Body);
        Assert.Contains($"#ticket_{ticket.Id}", post.Body);
        Assert.Contains($"sup:close:t:{ticket.Id}", post.Body);
        Assert.Equal(300, r.Store.GetTicket(ticket.Id)!.TelegramMessageId);
    }

    [Fact]
    public async Task Replying_in_the_group_answers_the_ticket_and_emails_the_customer()
    {
        var r = Setup();
        var ticket = OpenTicket(r.Store);

        r.Telegram.Updates.Enqueue(ReplyTo($"🎫 تیکت جدید\n#ticket_{ticket.Id}", "سلام، لطفاً یک بار از همه‌ی دستگاه‌ها خارج شوید."));
        await r.Bot.ProcessUpdatesAsync(0);
        await Task.Delay(100); // the email is fire-and-forget

        var answered = r.Store.GetTicket(ticket.Id)!;
        var answer = answered.Messages.Last();
        Assert.True(answer.IsAdmin);
        Assert.Equal("پشتیبانی فونیکس", answer.Author);   // the customer sees the brand, not a staff member's Telegram name
        Assert.Contains("خارج شوید", answer.Body);
        Assert.Equal(TicketStatus.Answered, answered.Status);
        Assert.Contains(r.Outbox.Sent, m => m.To == r.Store.GetUser(1)!.Email);
        Assert.Contains(r.Telegram.Calls, c => c.Method == "sendMessage" && c.Body.Contains("ارسال شد"));
    }

    // Anyone can add a bot to a group: only the configured staff group may answer.
    [Fact]
    public async Task A_reply_from_any_other_chat_changes_nothing()
    {
        var r = Setup();
        var ticket = OpenTicket(r.Store);

        r.Telegram.Updates.Enqueue(ReplyTo($"#ticket_{ticket.Id}", "پاسخ جعلی", chatId: "-100111222333"));
        await r.Bot.ProcessUpdatesAsync(0);

        Assert.Single(r.Store.GetTicket(ticket.Id)!.Messages);
        Assert.DoesNotContain(r.Telegram.Calls, c => c.Method == "sendMessage");
    }

    [Fact]
    public async Task Live_chat_messages_are_posted_and_answered_from_the_group()
    {
        var r = Setup();
        var conversation = r.Store.SendUserMessage(1, "علی محمدی", "سلام، کد تخفیف دارید؟");

        await r.Bot.NotifyChatMessageAsync(conversation, conversation.Messages.Last());
        var post = Assert.Single(r.Telegram.Calls, c => c.Method == "sendMessage");
        Assert.Contains($"#chat_{conversation.Id}", post.Body);
        Assert.Contains("کد تخفیف دارید", post.Body);

        r.Telegram.Updates.Enqueue(ReplyTo(post.Body, "بله، کد WELCOME را وارد کنید."));
        await r.Bot.ProcessUpdatesAsync(0);

        var after = r.Store.GetConversation(conversation.Id)!;
        Assert.True(after.Messages.Last().FromAdmin);
        Assert.Contains("WELCOME", after.Messages.Last().Body);
    }

    [Fact]
    public async Task An_answer_to_a_closed_chat_is_not_sent()
    {
        var r = Setup();
        var conversation = r.Store.SendUserMessage(1, "علی محمدی", "سلام");
        r.Store.CloseConversation(conversation.Id);

        r.Telegram.Updates.Enqueue(ReplyTo($"#chat_{conversation.Id}", "سلام، بفرمایید"));
        await r.Bot.ProcessUpdatesAsync(0);

        Assert.Single(r.Store.GetConversation(conversation.Id)!.Messages);
        Assert.Contains(r.Telegram.Calls, c => c.Method == "sendMessage" && c.Body.Contains("بسته شده"));
    }

    [Fact]
    public async Task The_close_button_closes_the_ticket()
    {
        var r = Setup();
        var ticket = OpenTicket(r.Store);

        r.Telegram.Updates.Enqueue(Tap($"sup:close:t:{ticket.Id}"));
        await r.Bot.ProcessUpdatesAsync(0);

        Assert.Equal(TicketStatus.Closed, r.Store.GetTicket(ticket.Id)!.Status);
        Assert.Contains(r.Telegram.Calls, c => c.Method == "editMessageReplyMarkup" && c.Body.Contains("sup:done:"));
    }

    [Fact]
    public async Task The_close_button_only_works_in_the_staff_group()
    {
        var r = Setup();
        var ticket = OpenTicket(r.Store);

        r.Telegram.Updates.Enqueue(Tap($"sup:close:t:{ticket.Id}", chatId: "-100111222333"));
        await r.Bot.ProcessUpdatesAsync(0);

        Assert.NotEqual(TicketStatus.Closed, r.Store.GetTicket(ticket.Id)!.Status);
    }

    [Fact]
    public async Task Id_tells_the_group_its_id_for_setup()
    {
        var r = Setup();
        r.Store.SetSupportBot(true, null, null, "");   // not set up yet: /id is how the owner finds it

        r.Telegram.Updates.Enqueue(JsonSerializer.Serialize(new
        {
            update_id = 7,
            message = new { message_id = 5, text = "/id", from = new { id = 42 }, chat = new { id = -100123123123L, type = "supergroup" } },
        }));
        await r.Bot.ProcessUpdatesAsync(0);

        Assert.Contains(r.Telegram.Calls, c => c.Method == "sendMessage" && c.Body.Contains("-100123123123"));
    }

    // An answer given on the site shows in the group, under the ticket, so the team knows it is handled.
    [Fact]
    public async Task A_panel_answer_is_posted_under_the_ticket_in_the_group()
    {
        var r = Setup();
        var ticket = OpenTicket(r.Store);
        await r.Bot.NotifyTicketOpenedAsync(ticket);
        var answered = r.Store.ReplyTicket(ticket.Id, "پشتیبانی فونیکس", "حل شد.", isAdmin: true)!;

        await r.Bot.NotifyTicketMessageAsync(answered, answered.Messages.Last(), "reza");

        var post = r.Telegram.Calls.Last(c => c.Method == "sendMessage");
        Assert.Contains("از طریق سایت (reza)", post.Body);
        Assert.Contains("reply_to_message_id=300", post.Body);
        Assert.DoesNotContain("sup:close", post.Body);
    }

    [Fact]
    public async Task Nothing_is_posted_while_the_bot_is_off()
    {
        var r = Setup();
        r.Store.SetSupportBot(false, null, null, Group);

        await r.Bot.NotifyTicketOpenedAsync(OpenTicket(r.Store));

        Assert.DoesNotContain(r.Telegram.Calls, c => c.Method == "sendMessage");
    }

    // ── panel ──

    private static SupportBotController Panel(Rig r)
    {
        var controller = new SupportBotController(r.Store, r.Bot);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, "2"), new Claim(ClaimTypes.Role, "Admin"), new Claim(ClaimTypes.Name, "reza"),
                }, "test")),
            },
        };
        return controller;
    }

    [Fact]
    public async Task The_panel_never_sends_the_token_back()
    {
        var r = Setup();
        var status = Panel(r).Get();

        Assert.True(status.HasToken);
        Assert.Equal(Group, status.ChatId);
        Assert.DoesNotContain("SupportBotTokenForTestsOnly", JsonSerializer.Serialize(status));
    }

    // Telegram gives a bot's updates to one reader: sharing a token with another bot steals its taps.
    [Fact]
    public async Task Another_bots_token_is_refused()
    {
        var r = Setup();
        const string orderToken = "111222333:AAOrderBotTokenForTestsOnly_abcdefghijk";
        r.Store.UpdateTelegramSettings(new TelegramSettings { OrderBotEnabled = true, OrderBotToken = orderToken, OrderChatId = "-100555" });

        var result = await Panel(r).Save(new SupportBotInput(true, orderToken, Group), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(Token, r.Store.GetTelegramSettings().SupportBotToken);
    }

    [Fact]
    public async Task A_group_id_must_be_a_number()
    {
        var r = Setup();

        var result = await Panel(r).Save(new SupportBotInput(true, null, "@mygroup"), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }
}

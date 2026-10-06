using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Services;
using SkiaSharp;
using Xunit;

namespace Phonix.Api.Tests;

// Level-1 bank cards and level-2 identity documents go to the receipt bot's chat with «تأیید / رد» buttons. A
// rejection asks for a reason, and the admin's reply is what the customer reads. These drive the bot against
// a scripted Telegram: what it posts, and what each tap and reply does to the request.
public class VerificationBotTests
{
    private const string Token = "123456:test-receipt-token";
    private const string Chat = "-1001234567890";

    // A scripted Telegram: records every call, answers send* with a message id, and hands getUpdates whatever
    // the test queued.
    private sealed class FakeTelegram : HttpMessageHandler
    {
        public List<(string Method, string Body)> Calls { get; } = new();
        public Queue<string> Updates { get; } = new();
        private int _nextMessageId = 500;
        // Like the real API: only a message that carries a photo has a caption to edit.
        private readonly HashSet<int> _photoMessages = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var method = request.RequestUri!.AbsolutePath.Split('/').Last();
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            var decoded = Uri.UnescapeDataString(body.Replace('+', ' '));
            lock (Calls) Calls.Add((method, decoded));
            if (method == "editMessageCaption"
                && System.Text.RegularExpressions.Regex.Match(decoded, @"message_id=(\d+)") is { Success: true } m
                && !_photoMessages.Contains(int.Parse(m.Groups[1].Value)))
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("{\"ok\":false,\"description\":\"Bad Request: there is no caption in the message to edit\"}"),
                };
            int Photo() { var id = _nextMessageId++; _photoMessages.Add(id); return id; }
            var result = method switch
            {
                "getUpdates" => $"[{(Updates.Count > 0 ? Updates.Dequeue() : "")}]",
                "sendMediaGroup" => $"[{{\"message_id\":{Photo()}}},{{\"message_id\":{Photo()}}}]",
                "sendPhoto" => $"{{\"message_id\":{Photo()}}}",
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
        public List<(string To, string Subject, string Text)> Sent { get; } = new();
        public Task<bool> SendAsync(string to, string subject, string body, string? htmlBody = null)
        {
            lock (Sent) Sent.Add((to, subject, body));
            return Task.FromResult(true);
        }
    }

    private sealed class NoopOrderBot : ITelegramOrderService
    {
        public Task NotifyOrderAsync(Order order, CancellationToken ct = default) => Task.CompletedTask;
        public Task AnnounceApprovedOrderAsync(Transaction tx, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyUnitAsync(Order order, OrderUnit unit, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> ProcessUpdatesAsync(long offset, CancellationToken ct = default) => Task.FromResult(offset);
        public Task<(bool ok, string? error)> SendTestAsync(CancellationToken ct = default) => Task.FromResult((true, (string?)null));
    }

    private sealed record Rig(IDataStore Store, LocalFileStorageService Files, FakeTelegram Telegram, Outbox Outbox, TelegramReceiptService Bot);

    private static Rig Setup()
    {
        var store = TestStore.Create();
        store.UpdateTelegramSettings(new TelegramSettings { ReceiptBotEnabled = true, ReceiptBotToken = Token, ReceiptChatId = Chat });
        var root = Path.Combine(Path.GetTempPath(), "phonix-verification-bot", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("PHONIX_UPLOADS_DIR", root);
        var files = new LocalFileStorageService();
        var telegram = new FakeTelegram();
        var outbox = new Outbox();
        var bot = new TelegramReceiptService(store, files, new UserMailer(store, outbox, NullLogger<UserMailer>.Instance),
            new NoopOrderBot(), new StockFulfillmentService(store, NullLogger<StockFulfillmentService>.Instance),
            new V2RayFulfillmentService(store, null!, null!, NullLogger<V2RayFulfillmentService>.Instance),
            new Factory(telegram), NullLogger<TelegramReceiptService>.Instance);
        return new Rig(store, files, telegram, outbox, bot);
    }

    private static async Task<string> Photo(LocalFileStorageService files, int owner, string category)
    {
        using var bmp = new SKBitmap(40, 25);
        bmp.Erase(SKColors.SteelBlue);
        using var data = bmp.Encode(SKEncodedImageFormat.Jpeg, 90);
        var bytes = data.ToArray();
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "card.jpg") { Headers = new HeaderDictionary() };
        return (await files.SaveAsync(owner, category, file)).Id!;
    }

    // A 16-digit number that passes the Luhn check the store requires.
    private static string LuhnCard(string first15)
    {
        var sum = 0;
        for (var i = 0; i < 15; i++)
        {
            var d = first15[14 - i] - '0';
            if (i % 2 == 0) { d *= 2; if (d > 9) d -= 9; }
            sum += d;
        }
        return first15 + (10 - sum % 10) % 10;
    }

    private static AppUser Customer(IDataStore s) => s.GetUser(1)!; // ali

    private static async Task<BankCard> FileCard(Rig r)
    {
        var image = await Photo(r.Files, Customer(r.Store).Id, "cards");
        var card = r.Store.AddCard(Customer(r.Store).Id, LuhnCard("603799123456789"), "علی رضایی", image).Card!;
        await r.Bot.NotifyCardAsync(card);
        return card;
    }

    // A button tap, as Telegram delivers it in getUpdates.
    private static string Tap(string data, string chatId = Chat, int messageId = 500) =>
        JsonSerializer.Serialize(new
        {
            update_id = Random.Shared.Next(1, 1_000_000),
            callback_query = new
            {
                id = "cb1", data, from = new { id = 42 },
                message = new { message_id = messageId, chat = new { id = long.Parse(chatId) } },
            },
        });

    // The admin's answer to a reason prompt: a message replying to the prompt's text.
    private static string Reply(string promptText, string reason, string chatId = Chat) =>
        JsonSerializer.Serialize(new
        {
            update_id = Random.Shared.Next(1, 1_000_000),
            message = new
            {
                message_id = 900, text = reason, from = new { id = 42 }, chat = new { id = long.Parse(chatId) },
                reply_to_message = new { message_id = 800, text = promptText },
            },
        });

    [Fact]
    public async Task A_new_card_is_posted_with_its_photo_details_and_buttons()
    {
        var r = Setup();
        var card = await FileCard(r);

        var post = Assert.Single(r.Telegram.Calls, c => c.Method == "sendPhoto");
        Assert.Contains(Chat, post.Body);
        Assert.Contains("احراز هویت سطح ۱", post.Body);
        Assert.Contains("علی رضایی", post.Body);
        Assert.Contains($"card:ok:{card.Id}", post.Body);
        Assert.Contains($"card:no:{card.Id}", post.Body);
    }

    [Fact]
    public async Task Approving_a_card_raises_the_customer_to_level_one_and_emails_them()
    {
        var r = Setup();
        r.Store.UpdateUser(Customer(r.Store).Id, u => u.VerificationLevel = 0);
        var card = await FileCard(r);

        r.Telegram.Updates.Enqueue(Tap($"card:ok:{card.Id}"));
        await r.Bot.ProcessUpdatesAsync(0);

        Assert.Equal(BankCardStatus.Approved, r.Store.GetCard(card.Id)!.Status);
        Assert.True(Customer(r.Store).VerificationLevel >= 1);
        Assert.Contains(r.Outbox.Sent, m => m.To == Customer(r.Store).Email);
        Assert.Contains(r.Telegram.Calls, c => c.Method == "editMessageCaption" && c.Body.Contains("تأیید شد"));
    }

    [Fact]
    public async Task Rejecting_asks_for_a_reason_and_the_reply_reaches_the_customer()
    {
        var r = Setup();
        var card = await FileCard(r);

        r.Telegram.Updates.Enqueue(Tap($"card:no:{card.Id}"));
        await r.Bot.ProcessUpdatesAsync(0);

        // The tap alone decides nothing: it asks for the reason.
        Assert.Equal(BankCardStatus.Pending, r.Store.GetCard(card.Id)!.Status);
        var prompt = r.Telegram.Calls.Last(c => c.Method == "sendMessage").Body;
        Assert.Contains($"#CARD-REJ:{card.Id}:500", prompt);

        const string reason = "عکس از صفحه‌ی گوشی گرفته شده است.\nلطفاً از خود کارت فیزیکی عکس بگیرید.";
        r.Telegram.Updates.Enqueue(Reply($"✍️ دلیل؟\n#CARD-REJ:{card.Id}:500", reason));
        await r.Bot.ProcessUpdatesAsync(0);

        var decided = r.Store.GetCard(card.Id)!;
        Assert.Equal(BankCardStatus.Rejected, decided.Status);
        Assert.Equal(reason, decided.RejectionReason);
        Assert.Contains(r.Outbox.Sent, m => m.To == Customer(r.Store).Email && m.Text.Contains("از خود کارت فیزیکی عکس بگیرید"));
        Assert.Contains(r.Telegram.Calls, c => c.Method == "editMessageCaption" && c.Body.Contains("دلیل رد"));
    }

    [Fact]
    public async Task A_tap_from_any_other_chat_changes_nothing()
    {
        var r = Setup();
        var card = await FileCard(r);

        r.Telegram.Updates.Enqueue(Tap($"card:ok:{card.Id}", chatId: "-1009999999999"));
        await r.Bot.ProcessUpdatesAsync(0);

        Assert.Equal(BankCardStatus.Pending, r.Store.GetCard(card.Id)!.Status);
        Assert.Empty(r.Outbox.Sent);
    }

    [Fact]
    public async Task A_card_already_decided_in_the_panel_is_not_decided_again()
    {
        var r = Setup();
        var card = await FileCard(r);
        r.Store.SetCardStatus(card.Id, BankCardStatus.Approved, null);

        r.Telegram.Updates.Enqueue(Tap($"card:no:{card.Id}"));
        await r.Bot.ProcessUpdatesAsync(0);

        Assert.Equal(BankCardStatus.Approved, r.Store.GetCard(card.Id)!.Status);
        Assert.DoesNotContain(r.Telegram.Calls, c => c.Method == "sendMessage" && c.Body.Contains("#CARD-REJ"));
    }

    [Fact]
    public async Task Identity_documents_go_up_as_an_album_with_the_details_and_buttons_after_them()
    {
        var r = Setup();
        var owner = Customer(r.Store).Id;
        var kyc = r.Store.SubmitKyc(new KycRequest
        {
            UserId = owner, FullName = "علی رضایی", NationalId = "0012345679", BirthDate = "1370/01/01",
            CardImage = await Photo(r.Files, owner, "kyc"), SelfieImage = await Photo(r.Files, owner, "kyc"),
        });

        await r.Bot.NotifyKycAsync(kyc);

        Assert.Single(r.Telegram.Calls, c => c.Method == "sendMediaGroup");
        var details = Assert.Single(r.Telegram.Calls, c => c.Method == "sendMessage");
        Assert.Contains("احراز هویت سطح ۲", details.Body);
        Assert.Contains("0012345679", details.Body);
        Assert.Contains("reply_to_message_id=500", details.Body); // a reply to the album
        Assert.Contains($"kyc:ok:{kyc.Id}", details.Body);
    }

    [Fact]
    public async Task Approving_identity_documents_raises_the_customer_to_level_two()
    {
        var r = Setup();
        var owner = Customer(r.Store).Id;
        var kyc = r.Store.SubmitKyc(new KycRequest
        {
            UserId = owner, FullName = "علی رضایی", NationalId = "0012345679", BirthDate = "1370/01/01",
            CardImage = await Photo(r.Files, owner, "kyc"), SelfieImage = await Photo(r.Files, owner, "kyc"),
        });

        r.Telegram.Updates.Enqueue(Tap($"kyc:ok:{kyc.Id}", messageId: 502));
        await r.Bot.ProcessUpdatesAsync(0);

        Assert.Equal(2, Customer(r.Store).VerificationLevel);
        // A text message has no caption, so the outcome lands through editMessageText.
        Assert.Contains(r.Telegram.Calls, c => c.Method == "editMessageText" && c.Body.Contains("تأیید شد"));
    }

    [Fact]
    public async Task Rejecting_identity_documents_records_the_reason()
    {
        var r = Setup();
        var owner = Customer(r.Store).Id;
        var kyc = r.Store.SubmitKyc(new KycRequest
        {
            UserId = owner, FullName = "علی رضایی", NationalId = "0012345679", BirthDate = "1370/01/01",
            CardImage = await Photo(r.Files, owner, "kyc"), SelfieImage = await Photo(r.Files, owner, "kyc"),
        });

        r.Telegram.Updates.Enqueue(Reply($"#KYC-REJ:{kyc.Id}:502", "تصویر کارت ملی تار است."));
        await r.Bot.ProcessUpdatesAsync(0);

        var decided = r.Store.GetAllKyc().Single(k => k.Id == kyc.Id);
        Assert.Equal(KycStatus.Rejected, decided.Status);
        Assert.Equal("تصویر کارت ملی تار است.", decided.RejectionReason);
        Assert.Contains(r.Outbox.Sent, m => m.Text.Contains("تصویر کارت ملی تار است."));
    }

    [Fact]
    public async Task Receipt_rejections_still_work_alongside()
    {
        var r = Setup();
        var tx = r.Store.AddTransaction(new Transaction
        {
            UserId = Customer(r.Store).Id, UserName = "ali", Type = TxTypes.WalletTopUp, Amount = 50_000, Status = TxStatus.Pending, Method = "کارت",
        });

        r.Telegram.Updates.Enqueue(Reply($"#REJ:{tx.Id}:600", "مبلغ واریز با رسید یکی نیست."));
        await r.Bot.ProcessUpdatesAsync(0);

        Assert.Equal(TxStatus.Rejected, r.Store.GetTransaction(tx.Id)!.Status);
    }
}

// Submitting level-2 documents: the selfie is optional (the form says so), and a submission reaches the bot.
public class KycSubmitTests
{
    private sealed class RecordingBot : ITelegramReceiptService
    {
        public List<KycRequest> Kyc { get; } = new();
        public Task NotifyDepositAsync(Transaction tx, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyCardAsync(BankCard card, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyKycAsync(KycRequest kyc, CancellationToken ct = default) { Kyc.Add(kyc); return Task.CompletedTask; }
        public Task<long> ProcessUpdatesAsync(long offset, CancellationToken ct = default) => Task.FromResult(offset);
        public Task<(bool ok, string? error)> SendTestAsync(CancellationToken ct = default) => Task.FromResult((true, (string?)null));
    }

    private sealed class NoopEmail : IEmailSender
    {
        public Task<bool> SendAsync(string to, string subject, string body, string? htmlBody = null) => Task.FromResult(true);
    }

    private static async Task<string> Upload(LocalFileStorageService files, int owner)
    {
        using var bmp = new SKBitmap(30, 20);
        bmp.Erase(SKColors.DarkGreen);
        using var data = bmp.Encode(SKEncodedImageFormat.Jpeg, 90);
        var bytes = data.ToArray();
        return (await files.SaveAsync(owner, "kyc", new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "id.jpg") { Headers = new HeaderDictionary() })).Id!;
    }

    private static (Phonix.Api.Controllers.KycController Controller, LocalFileStorageService Files, RecordingBot Bot, IDataStore Store) Setup(int userId)
    {
        var store = TestStore.Create();
        store.UpdateUser(userId, u => u.VerificationLevel = 1);
        var root = Path.Combine(Path.GetTempPath(), "phonix-kyc-submit", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("PHONIX_UPLOADS_DIR", root);
        var files = new LocalFileStorageService();
        var bot = new RecordingBot();
        var user = store.GetUser(userId)!;
        var controller = new Phonix.Api.Controllers.KycController(store, files, new UserMailer(store, new NoopEmail(), NullLogger<UserMailer>.Instance), bot)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]
                    {
                        new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, user.Id.ToString()),
                        new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, user.Role.ToString()),
                    }, "test")),
                },
            },
        };
        return (controller, files, bot, store);
    }

    [Fact]
    public async Task Documents_without_the_optional_selfie_are_accepted_and_sent_to_the_bot()
    {
        var (c, files, bot, _) = Setup(1);
        var card = await Upload(files, 1);

        var result = c.Submit(new Phonix.Api.Controllers.KycInput("علی رضایی", "0012345679", "1370/01/01", card, ""));

        var kyc = Assert.IsType<KycRequest>(result.Value);
        Assert.Equal("", kyc.SelfieImage);
        Assert.Equal(KycStatus.Pending, kyc.Status);
        Assert.Equal(kyc.Id, Assert.Single(bot.Kyc).Id);
    }

    [Fact]
    public async Task A_selfie_uploaded_by_someone_else_is_refused()
    {
        var (c, files, bot, store) = Setup(1);
        var card = await Upload(files, 1);
        var someoneElses = await Upload(files, store.GetUsers().First(u => u.Id != 1 && u.Role == UserRole.Customer).Id);

        var result = c.Submit(new Phonix.Api.Controllers.KycInput("علی رضایی", "0012345679", "1370/01/01", card, someoneElses));

        Assert.IsType<Microsoft.AspNetCore.Mvc.BadRequestObjectResult>(result.Result);
        Assert.Empty(bot.Kyc);
    }
}

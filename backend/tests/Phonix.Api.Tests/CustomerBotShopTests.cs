using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Services;
using SkiaSharp;
using Xunit;

namespace Phonix.Api.Tests;

// The catalogue inside the customer bot: every product can be browsed by anyone; the products staff opened for it
// are bought there without a site account; everything else needs a registered, verified, linked account and then
// continues in the site's own checkout. Driven against a scripted Telegram.
public class CustomerBotShopTests
{
    private const string Token = TelegramLaunch.Token;
    private const long Chat = 912345;
    private const int Spotify = 2;   // seed: category 2, no plans, in stock, level 1
    private const int Netflix = 1;   // seed: category 1, plans, level 1 — not opened

    static CustomerBotShopTests() => Environment.SetEnvironmentVariable("PHONIX_FRONTEND_URL", "https://shop.test");

    private sealed class FakeTelegram : HttpMessageHandler
    {
        public List<(string Method, string Body)> Calls { get; } = new();
        public Queue<string> Updates { get; } = new();
        private static readonly byte[] Photo = MakeJpeg();

        private static byte[] MakeJpeg()
        {
            using var bmp = new SKBitmap(60, 40);
            bmp.Erase(SKColors.SteelBlue);
            using var data = bmp.Encode(SKEncodedImageFormat.Jpeg, 90);
            return data.ToArray();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath.StartsWith("/file/"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Photo) };
            var method = request.RequestUri.AbsolutePath.Split('/').Last();
            var body = request.Content is null ? request.RequestUri.Query : await request.Content.ReadAsStringAsync(ct);
            var decoded = Uri.UnescapeDataString(body.Replace('+', ' '));
            lock (Calls) Calls.Add((method, decoded));
            var result = method switch
            {
                "getUpdates" => $"[{(Updates.Count > 0 ? Updates.Dequeue() : "")}]",
                "getFile" => "{\"file_id\":\"F1\",\"file_path\":\"photos/file_1.jpg\",\"file_size\":2048}",
                _ => "{\"message_id\":1}",
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{{\"ok\":true,\"result\":{result}}}") };
        }
    }

    private sealed class Factory(HttpMessageHandler h) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(h, disposeHandler: false);
    }

    private sealed class NoMail : IEmailSender
    {
        public Task<bool> SendAsync(string to, string subject, string body, string? htmlBody = null) => Task.FromResult(true);
    }

    private sealed record Rig(IDataStore Store, FakeTelegram Telegram, TelegramCustomerBot Bot);

    private static Rig Setup(bool sales = true)
    {
        var store = TestStore.Create();
        store.SetCustomerBot(true, false, Token, "PhoenixTestBot", sales: sales);
        var product = store.GetProduct(Spotify)!;
        product.TelegramGuestSale = true;
        store.UpdateProduct(product);
        var root = Path.Combine(Path.GetTempPath(), "phonix-bot-shop", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("PHONIX_UPLOADS_DIR", root);
        var telegram = new FakeTelegram();
        var bot = new TelegramCustomerBot(store, new Factory(telegram), NullLogger<TelegramCustomerBot>.Instance, new LocalFileStorageService());
        return new Rig(store, telegram, bot);
    }

    private static string Text(string text, long chatId = Chat) => JsonSerializer.Serialize(new
    {
        update_id = Random.Shared.Next(1, 1_000_000),
        message = new { message_id = 5, text, from = new { id = chatId, first_name = "Sara", username = "sara_tg" }, chat = new { id = chatId, type = "private" } },
    });

    private static string ReceiptPhoto(string caption = "پیگیری ۱۲۳۴۵۶", long chatId = Chat) => JsonSerializer.Serialize(new
    {
        update_id = Random.Shared.Next(1, 1_000_000),
        message = new
        {
            message_id = 6, caption, from = new { id = chatId, first_name = "Sara", username = "sara_tg" }, chat = new { id = chatId, type = "private" },
            photo = new[] { new { file_id = "small", width = 90, height = 60 }, new { file_id = "F1", width = 900, height = 600 } },
        },
    });

    private static string Tap(string data, long chatId = Chat) => JsonSerializer.Serialize(new
    {
        update_id = Random.Shared.Next(1, 1_000_000),
        callback_query = new { id = "cb", data, from = new { id = chatId }, message = new { message_id = 7, chat = new { id = chatId, type = "private" } } },
    });

    private static async Task Run(Rig r, params string[] updates)
    {
        foreach (var u in updates)
        {
            r.Telegram.Updates.Enqueue(u);
            await r.Bot.ProcessUpdatesAsync(0);
        }
    }

    private static string LastReply(Rig r) => r.Telegram.Calls.Last(c => c.Method is "sendMessage" or "sendPhoto").Body;

    [Fact]
    public async Task Anyone_can_buy_an_opened_product_without_a_site_account()
    {
        var r = Setup();

        await Run(r, Text("🛍 محصولات"));
        Assert.Contains("shop:c:2", LastReply(r));
        await Run(r, Tap("shop:c:2"));
        Assert.Contains($"shop:p:{Spotify}", LastReply(r));
        await Run(r, Tap($"shop:p:{Spotify}"));
        Assert.Contains($"shop:b:{Spotify}:0", LastReply(r));

        await Run(r, Tap($"shop:b:{Spotify}:0"));
        var instructions = LastReply(r);
        Assert.Contains("علی محمدی", instructions);   // the card holder buyers pay to
        Assert.Contains("عکس رسید", instructions);

        await Run(r, ReceiptPhoto());

        var guest = r.Store.FindTelegramGuest(Chat)!;
        Assert.StartsWith("tg_", guest.Username);
        Assert.Equal("", guest.Email);
        var order = Assert.Single(r.Store.GetUserOrders(guest.Id));
        Assert.Equal(TelegramCustomerBot.PlacedViaBot, order.PlacedVia);
        Assert.Equal(OrderStatus.PendingApproval, order.Status);
        var payment = r.Store.GetUserTransactions(guest.Id).Single(t => t.OrderCode == order.Code);
        Assert.Equal(TxStatus.Pending, payment.Status);
        Assert.Contains("تلگرام", payment.SourceHolder);
        Assert.Equal("123456", payment.TrackingNumber);       // read from the caption, Persian digits and all
        Assert.False(string.IsNullOrWhiteSpace(payment.ReceiptUrl));
        Assert.Contains(order.Code, LastReply(r));
    }

    // Looking needs no account: every product is there, opened for the bot or not.
    [Fact]
    public async Task Every_product_can_be_browsed_by_anyone()
    {
        var r = Setup();

        await Run(r, Tap("shop:c:1"));
        Assert.Contains($"shop:p:{Netflix}", LastReply(r));

        await Run(r, Tap($"shop:p:{Netflix}"));
        var page = LastReply(r);
        var plan = r.Store.GetProduct(Netflix)!.Plans.First(p => p.IsActive);
        Assert.Contains($"shop:b:{Netflix}:{plan.Id}", page);
        Assert.Contains("حساب سایت", page);   // says up front what it takes to buy
    }

    // Opening a product is a staff decision: anything else is never sold here, whatever the buttons say.
    [Fact]
    public async Task A_product_not_opened_for_the_bot_is_never_sold_there()
    {
        var r = Setup();
        var planId = r.Store.GetProduct(Netflix)!.Plans.First(p => p.IsActive).Id;

        await Run(r, Tap($"shop:b:{Netflix}:{planId}"), ReceiptPhoto());

        Assert.Null(r.Store.FindTelegramGuest(Chat));
        Assert.DoesNotContain(r.Telegram.Calls, c => c.Body.Contains("عکس رسید"));
    }

    // Without an account the payment step doesn't open: the customer is told to register, verify and link.
    [Fact]
    public async Task Without_an_account_the_payment_step_says_what_to_do()
    {
        var r = Setup();
        var planId = r.Store.GetProduct(Netflix)!.Plans.First(p => p.IsActive).Id;

        await Run(r, Tap($"shop:b:{Netflix}:{planId}"));

        var reply = LastReply(r);
        Assert.Contains("ثبت‌نام", reply);
        Assert.Contains("اتصال به تلگرام", reply);
        Assert.Contains("https://shop.test/signup", reply);
    }

    [Fact]
    public async Task A_linked_customer_whose_level_is_too_low_is_told_which_step_is_missing()
    {
        var r = Setup();
        var negar = r.Store.GetUserByUsername("negar")!;   // seed: identity level 0
        r.Store.LinkTelegram(negar.Id, Chat, "negar_tg");
        var planId = r.Store.GetProduct(Netflix)!.Plans.First(p => p.IsActive).Id;

        await Run(r, Tap($"shop:b:{Netflix}:{planId}"));

        var reply = LastReply(r);
        Assert.Contains("کافی نیست", reply);
        Assert.Contains("/account/cards", reply);
    }

    // A linked customer who qualifies continues in the site's own checkout, opened inside Telegram.
    [Fact]
    public async Task A_qualified_linked_customer_continues_in_the_sites_checkout()
    {
        var r = Setup();
        r.Store.LinkTelegram(1, Chat, "ali_tg");   // ali: identity level 2
        r.Store.SetCustomerBot(true, false, null, null, shop: true, sales: true);
        var planId = r.Store.GetProduct(Netflix)!.Plans.First(p => p.IsActive).Id;

        await Run(r, Tap($"shop:b:{Netflix}:{planId}"));

        var reply = LastReply(r);
        Assert.Contains("\"web_app\":{\"url\":\"https://shop.test/products/1\"}", reply);
        Assert.Null(r.Store.FindTelegramGuest(Chat));
    }

    [Fact]
    public async Task A_linked_customer_buys_on_their_site_account()
    {
        var r = Setup();
        r.Store.LinkTelegram(1, Chat, "ali_tg");

        await Run(r, Tap($"shop:b:{Spotify}:0"), ReceiptPhoto());

        Assert.Null(r.Store.FindTelegramGuest(Chat));
        Assert.Contains(r.Store.GetUserOrders(1), o => o.PlacedVia == TelegramCustomerBot.PlacedViaBot);
    }

    // The open door's main guard: no stack of unreviewed receipts from one person.
    [Fact]
    public async Task Only_one_unreviewed_purchase_at_a_time()
    {
        var r = Setup();
        await Run(r, Tap($"shop:b:{Spotify}:0"), ReceiptPhoto());

        await Run(r, Tap($"shop:b:{Spotify}:0"));

        Assert.Contains("در انتظار بررسی", LastReply(r));
        await Run(r, ReceiptPhoto());
        Assert.Single(r.Store.GetUserOrders(r.Store.FindTelegramGuest(Chat)!.Id));
    }

    // What the buyer was shown is what they pay, even if the price moves while they are at the bank.
    [Fact]
    public async Task The_quoted_price_is_held_until_the_receipt_arrives()
    {
        var r = Setup();
        var shown = r.Store.GetProduct(Spotify)!.FinalPrice;
        await Run(r, Tap($"shop:b:{Spotify}:0"));

        var product = r.Store.GetProduct(Spotify)!;
        product.Price += 50_000;
        r.Store.UpdateProduct(product);
        await Run(r, ReceiptPhoto());

        var order = r.Store.GetUserOrders(r.Store.FindTelegramGuest(Chat)!.Id).Single();
        Assert.Equal(shown, order.Items.Single().UnitPrice);
    }

    // A guest has no account page: the bought account arrives in the chat, like every message for the account.
    [Fact]
    public async Task The_delivered_account_reaches_a_guest_in_telegram()
    {
        var r = Setup();
        await Run(r, Tap($"shop:b:{Spotify}:0"), ReceiptPhoto());
        var guest = r.Store.FindTelegramGuest(Chat)!;
        var order = r.Store.GetUserOrders(guest.Id).Single();
        var payment = r.Store.GetUserTransactions(guest.Id).Single(t => t.OrderCode == order.Code);
        r.Store.DecideTransaction(payment.Id, TxStatus.Approved, DecisionVia.Telegram, null, "@maryam");
        var (delivered, _) = r.Store.DeliverUnit(order.Id, order.Units[0].Id, "user: sara@spotify\npass: 123", "reza");

        var mailer = new UserMailer(r.Store, new NoMail(), NullLogger<UserMailer>.Instance, r.Bot);
        await mailer.OrderUnitDeliveredAsync(delivered!, order.Units[0].Id);

        var message = LastReply(r);
        Assert.Contains($"chat_id={Chat}", message);
        Assert.Contains("sara@spotify", message);
    }

    [Fact]
    public async Task A_guests_support_message_reaches_the_live_chat_and_the_answer_comes_back()
    {
        var r = Setup();
        await Run(r, Text("💬 پشتیبانی"), Text("سلام، رسیدم را فرستادم ولی هنوز تأیید نشده."));

        var guest = r.Store.FindTelegramGuest(Chat)!;
        var conversation = r.Store.GetUserConversation(guest.Id)!;
        Assert.True(conversation.ViaTelegram);
        Assert.Contains("رسیدم را فرستادم", conversation.Messages.Last().Body);

        var mailer = new UserMailer(r.Store, new NoMail(), NullLogger<UserMailer>.Instance, r.Bot);
        await mailer.SupportReplyAsync(guest.Id, "رسید شما تأیید شد.");
        Assert.Contains("رسید شما تأیید شد", LastReply(r));
    }

    [Fact]
    public async Task Identity_verified_products_and_plans_that_ask_for_details_are_not_offered()
    {
        var store = TestStore.Create();
        var verified = new Product { Name = "verify", Stock = 5, IsActive = true, RequiredLevel = 2, TelegramGuestSale = true };
        Assert.False(TelegramCustomerBot.GuestSellable(verified));

        var withForm = new Product { Name = "form", Stock = 5, IsActive = true, TelegramGuestSale = true };
        withForm.Plans.Add(new ProductPlan { Id = 1, IsActive = true, CollectsInfo = true });
        withForm.Plans.Add(new ProductPlan { Id = 2, IsActive = true, CollectSeatInfo = true });
        Assert.False(TelegramCustomerBot.GuestSellable(withForm));

        withForm.Plans.Add(new ProductPlan { Id = 3, IsActive = true });
        Assert.True(TelegramCustomerBot.GuestSellable(withForm));
        Assert.Equal(new[] { 3 }, TelegramCustomerBot.GuestPlans(withForm).Select(p => p.Id));

        // And the order itself refuses, whatever reaches it.
        var guest = store.EnsureTelegramGuest(55, "x");
        var placed = store.PlaceOrder(guest, new[] { (1, 1, (int?)store.GetProduct(1)!.Plans.First(p => p.IsActive).Id) }, "کارت به کارت",
            fromWallet: false, paymentMethodId: 1, bot: new BotCheckout("1__0123456789abcdef0123456789abcdef.jpg", null, "x"));
        Assert.NotNull(placed.Error);
    }

    [Fact]
    public async Task Nothing_is_sold_while_sales_are_off()
    {
        var r = Setup(sales: false);

        await Run(r, Tap($"shop:b:{Spotify}:0"), ReceiptPhoto());

        Assert.Null(r.Store.FindTelegramGuest(Chat));
    }

    // A guest account is Telegram's alone: it never signs anyone into the site.
    [Fact]
    public void A_guest_account_is_not_a_linked_account()
    {
        var store = TestStore.Create();
        var guest = store.EnsureTelegramGuest(Chat, "Sara");

        Assert.Null(store.FindUserByTelegramChat(Chat));
        Assert.Equal(guest.Id, store.EnsureTelegramGuest(Chat, "Sara again").Id);   // one per Telegram user
        Assert.Contains("_", guest.Username);   // a name no site signup can take
    }
}

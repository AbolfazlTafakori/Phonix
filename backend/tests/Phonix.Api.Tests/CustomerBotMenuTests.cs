using Microsoft.Extensions.Logging.Abstractions;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Services;
using Xunit;
using static Phonix.Api.Tests.CustomerBotShopTests;

namespace Phonix.Api.Tests;

// How the customer bot is laid out and moves: one inline main menu, every button editing the screen it was pressed
// on, buying in three numbered steps, an order as a card, the account, notices that carry their buttons — and
// running the shop from the bot for linked staff, within the sections each of them holds. Driven against a
// scripted Telegram.
public class CustomerBotMenuTests
{
    // A chat of its own: the bot keeps what a chat is in the middle of per chat id, across tests running in parallel.
    private const long Me = 913000;

    private static string Last(Rig r, string method) => r.Telegram.Calls.Last(c => c.Method == method).Body;

    private static string Screen(Rig r) => LastReply(r);

    private static int IdOf(Rig r, string username) => r.Store.GetUserByUsername(username)!.Id;

    private static Order Bought(Rig r, int userId)
    {
        var product = r.Store.GetProduct(Spotify)!;
        if (product.Stock < 50)
        {
            product.Stock = 100;
            r.Store.UpdateProduct(product);
        }
        return r.Store.PlaceOrder(r.Store.GetUser(userId)!, new[] { (Spotify, 1, (int?)null) }, "کارت بانکی", fromWallet: false).Order!;
    }

    private static Order Delivered(Rig r, int userId, string content)
    {
        var order = Bought(r, userId);
        r.Store.SetOrderStatus(order.Id, OrderStatus.Preparing);
        return r.Store.DeliverUnit(order.Id, order.Units[0].Id, content, "reza").order!;
    }

    private static Rig Staffed(string username = "reza", IServiceProvider? services = null)
    {
        var r = Setup(services: services);
        r.Store.SetCustomerBot(true, false, null, null, admin: true);
        r.Store.LinkTelegram(IdOf(r, username), Me, $"{username}_tg");
        return r;
    }

    private static async Task WaitFor(Rig r, Func<(string Method, string Body), bool> sent)
    {
        for (var i = 0; i < 100; i++)
        {
            lock (r.Telegram.Calls)
                if (r.Telegram.Calls.Any(sent)) return;
            await Task.Delay(50);
        }
        Assert.Fail("Telegram never got the expected message.");
    }

    // ── The menu ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Start_lays_the_home_keyboard_and_opens_the_inline_main_menu()
    {
        var r = Setup();

        await Run(r, Text("/start", Me));

        var sent = r.Telegram.Calls.Where(c => c.Method == "sendMessage").ToList();
        Assert.Contains("\"is_persistent\":true", sent[^2].Body);   // the one-button keyboard under the message box
        var menu = sent[^1].Body;
        foreach (var button in new[] { "shop:cats", "ord:l:1", "acc", "sup", "tut" })
            Assert.Contains($"\"callback_data\":\"{button}\"", menu);
        Assert.DoesNotContain("\"callback_data\":\"adm\"", menu);
    }

    [Fact]
    public async Task A_button_edits_the_screen_it_was_pressed_on()
    {
        var r = Setup();

        await Run(r, Tap("shop:cats", Me));

        var edit = Last(r, "editMessageText");
        Assert.Contains("message_id=7", edit);
        Assert.Contains("shop:c:2", edit);
        Assert.DoesNotContain(r.Telegram.Calls, c => c.Method == "sendMessage");
    }

    [Fact]
    public async Task Buying_is_three_numbered_steps_each_with_a_way_back()
    {
        var r = Setup();

        await Run(r, Tap("shop:cats", Me));
        Assert.Contains("اول از سه", Screen(r));

        await Run(r, Tap("shop:c:2", Me));
        var products = Screen(r);
        Assert.Contains("دوم از سه", products);
        Assert.Contains("\"callback_data\":\"shop:cats\"", products);   // one step back
        Assert.Contains("\"callback_data\":\"home\"", products);        // and always the main menu

        await Run(r, Tap($"shop:p:{Spotify}", Me));
        var product = Screen(r);
        Assert.Contains("سوم از سه", product);
        Assert.Contains("\"callback_data\":\"shop:c:2\"", product);
    }

    [Fact]
    public async Task The_old_keyboard_still_works_and_is_swapped_for_the_new_one()
    {
        var r = Setup();

        await Run(r, Text("🔗 حساب سایت", Me));

        var sent = r.Telegram.Calls.Where(c => c.Method == "sendMessage").ToList();
        Assert.Contains("\"is_persistent\":true", sent[^2].Body);
        Assert.Contains("https://shop.test/signup", sent[^1].Body);   // the account screen it asked for
    }

    // ── Orders ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Orders_come_a_page_at_a_time()
    {
        var r = Setup();
        r.Store.LinkTelegram(1, Me, "ali_tg");
        for (var i = 0; i < 8; i++) Bought(r, 1);

        await Run(r, Tap("ord:l:1", Me));
        var first = Screen(r);
        Assert.Contains("\"callback_data\":\"ord:l:2\"", first);
        Assert.Contains("\"callback_data\":\"ord:s\"", first);

        await Run(r, Tap("ord:l:2", Me));
        Assert.Contains("\"callback_data\":\"ord:l:1\"", Screen(r));
    }

    [Fact]
    public async Task An_order_is_a_card_with_its_facts_as_a_table_and_the_account_ready_to_copy()
    {
        var r = Setup();
        r.Store.LinkTelegram(1, Me, "ali_tg");
        var order = Delivered(r, 1, "user: ali@spotify <pass>");

        await Run(r, Tap($"ord:v:{order.Id}", Me));

        var card = Screen(r);
        Assert.Contains("parse_mode=HTML", card);
        Assert.Contains("<pre>user: ali@spotify &lt;pass&gt;</pre>", card);   // escaped, in a block one tap copies
        Assert.Contains("\"callback_data\":\"noop\"", card);                  // the [value | label] table
        Assert.Contains(order.Code, card);
    }

    [Fact]
    public async Task Someone_elses_order_is_never_shown()
    {
        var r = Setup();
        r.Store.LinkTelegram(1, Me, "ali_tg");
        var theirs = Delivered(r, IdOf(r, "zahra"), "user: zahra@spotify");

        await Run(r, Tap($"ord:v:{theirs.Id}", Me));

        Assert.DoesNotContain("zahra@spotify", Screen(r));
        Assert.Contains("یافت نشد", Screen(r));
    }

    [Fact]
    public async Task An_order_is_found_by_the_digits_of_its_code()
    {
        var r = Setup();
        r.Store.LinkTelegram(1, Me, "ali_tg");
        var order = Bought(r, 1);
        var digits = new string(order.Code.Where(char.IsAsciiDigit).ToArray());

        await Run(r, Tap("ord:s", Me), Text(digits, Me));

        Assert.Contains(order.Code, Screen(r));
        Assert.Contains("\"callback_data\":\"noop\"", Screen(r));
    }

    // ── The account ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_account_turns_notices_off_and_on()
    {
        var r = Setup();
        r.Store.LinkTelegram(1, Me, "ali_tg");

        await Run(r, Tap("acc", Me));
        Assert.Contains("acc:notify", Screen(r));

        await Run(r, Tap("acc:notify", Me));
        Assert.False(r.Store.GetUser(1)!.TelegramNotify);
        await Run(r, Tap("acc:notify", Me));
        Assert.True(r.Store.GetUser(1)!.TelegramNotify);
    }

    [Fact]
    public async Task Unlinking_from_the_bot_asks_first()
    {
        var r = Setup();
        r.Store.LinkTelegram(1, Me, "ali_tg");

        await Run(r, Tap("acc:unlink", Me));
        Assert.Equal(Me, r.Store.GetUser(1)!.TelegramChatId);

        await Run(r, Tap("acc:unlink:y", Me));
        Assert.Null(r.Store.GetUser(1)!.TelegramChatId);
    }

    // ── Notices ─────────────────────────────────────────────────────────────────────────────────────────────

    // The notice stays readable in the chat: its button opens the order as a new message rather than editing it.
    [Fact]
    public async Task A_notice_opens_its_order_as_a_new_message()
    {
        var r = Setup();
        r.Store.LinkTelegram(1, Me, "ali_tg");
        var order = Bought(r, 1);
        var mailer = new UserMailer(r.Store, new NoMail(), NullLogger<UserMailer>.Instance, r.Bot);

        await mailer.OrderPlacedAsync(order);
        Assert.Contains($"\"callback_data\":\"ord:v:{order.Id}:n\"", Last(r, "sendMessage"));

        var before = r.Telegram.Calls.Count;
        await Run(r, Tap($"ord:v:{order.Id}:n", Me));
        var after = r.Telegram.Calls.Skip(before).ToList();
        Assert.DoesNotContain(after, c => c.Method is "editMessageText" or "deleteMessage");
        Assert.Contains(after, c => c.Method == "sendMessage" && c.Body.Contains(order.Code));
    }

    // Someone who bought in the bot has no email: Telegram is the only place a warning can reach them.
    [Fact]
    public async Task A_service_running_out_reaches_a_guest_with_its_renewal_page()
    {
        var r = Setup();
        var guest = r.Store.EnsureTelegramGuest(Me, "Sara");

        await TelegramCustomerBot.NotifyFromWorkerAsync(r.Store, r.Bot, NullLogger.Instance, guest.Id, 42,
            TelegramCustomerBot.RunningOutNotice("PX-1", "۱۴۰۵/۰۸/۰۱", null), renewSitePath: "/config/tok123");

        var sent = Last(r, "sendMessage");
        Assert.Contains($"chat_id={Me}", sent);
        Assert.Contains("PX-1", sent);
        Assert.Contains("https://shop.test/config/tok123", sent);
    }

    // ── Running the shop from the bot ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Staff_management_shows_only_when_switched_on_and_only_to_staff()
    {
        var r = Setup();
        r.Store.LinkTelegram(IdOf(r, "reza"), Me, "reza_tg");
        await Run(r, Tap("home", Me));
        Assert.DoesNotContain("\"callback_data\":\"adm\"", Screen(r));

        r.Store.SetCustomerBot(true, false, null, null, admin: true);
        await Run(r, Tap("home", Me));
        Assert.Contains("\"callback_data\":\"adm\"", Screen(r));

        // A customer never sees it, and its buttons do nothing for them.
        const long customer = 913001;
        r.Store.LinkTelegram(1, customer, "ali_tg");
        await Run(r, Tap("home", customer));
        Assert.DoesNotContain("\"callback_data\":\"adm\"", Screen(r));
        await Run(r, Tap($"adm:pa:{Spotify}", customer));
        Assert.True(r.Store.GetProduct(Spotify)!.IsActive);
    }

    [Fact]
    public void Staff_management_switches_off_with_the_bot_and_stays_off()
    {
        var store = TestStore.Create();
        store.SetCustomerBot(true, false, Token, "PhoenixTestBot", admin: true);
        Assert.True(store.GetTelegramSettings().CustomerBotAdmin);

        store.SetCustomerBot(false, false, null, null);
        Assert.False(store.GetTelegramSettings().CustomerBotAdmin);
        store.SetCustomerBot(true, false, null, null);
        Assert.False(store.GetTelegramSettings().CustomerBotAdmin);
    }

    [Fact]
    public async Task An_admin_switches_a_product_off_and_it_is_in_the_audit_log()
    {
        var previous = Environment.GetEnvironmentVariable("PHONIX_AUDIT_FILE");
        Environment.SetEnvironmentVariable("PHONIX_AUDIT_FILE", Path.Combine(Path.GetTempPath(), $"phonix-audit-{Guid.NewGuid():N}.json"));
        var audit = new AuditStore();
        Environment.SetEnvironmentVariable("PHONIX_AUDIT_FILE", previous);
        var r = Staffed(services: new CustomerBotShopTests.Services(audit, new CatalogCache()));

        await Run(r, Tap($"adm:pr:{Spotify}", Me));
        Assert.Contains($"\"callback_data\":\"adm:pa:{Spotify}\"", Screen(r));
        await Run(r, Tap($"adm:pa:{Spotify}", Me));

        Assert.False(r.Store.GetProduct(Spotify)!.IsActive);
        var entry = Assert.Single(audit.GetAuditLogs(null, null, null, null, 1, 50).Items);
        Assert.Equal("products", entry.Entity);
        Assert.Equal(Spotify.ToString(), entry.EntityId);
        Assert.Equal("reza", entry.ActorName);
    }

    [Fact]
    public async Task Support_staff_get_only_the_sections_granted_to_them()
    {
        var r = Staffed("mohammad");
        var support = IdOf(r, "mohammad");
        r.Store.UpdateUser(support, u => u.Permissions = new List<string>());

        await Run(r, Tap("adm", Me));
        var menu = Screen(r);
        Assert.Contains("\"callback_data\":\"adm:q\"", menu);
        Assert.DoesNotContain("adm:pc", menu);
        Assert.DoesNotContain("adm:set", menu);

        await Run(r, Tap($"adm:pa:{Spotify}", Me), Tap("adm:set:sales", Me));
        Assert.True(r.Store.GetProduct(Spotify)!.IsActive);
        Assert.True(r.Store.GetTelegramSettings().CustomerBotSales);

        // Granted in the panel, it works on the next tap — and the bot's own switches stay an Admin's.
        r.Store.UpdateUser(support, u => u.Permissions = AdminMenuKeys());
        await Run(r, Tap($"adm:pa:{Spotify}", Me), Tap("adm:set:sales", Me));
        Assert.False(r.Store.GetProduct(Spotify)!.IsActive);
        Assert.True(r.Store.GetTelegramSettings().CustomerBotSales);
    }

    private static List<string> AdminMenuKeys() => Phonix.Api.Admin.AdminMenu.AssignableKeys.ToList();

    [Fact]
    public async Task An_admin_opens_and_closes_guest_sales_from_the_bot()
    {
        var r = Staffed();

        await Run(r, Tap("adm:set:sales", Me));
        Assert.False(r.Store.GetTelegramSettings().CustomerBotSales);
        await Run(r, Tap("adm:set:sales", Me));
        Assert.True(r.Store.GetTelegramSettings().CustomerBotSales);
    }

    // Staff learn what happened to each account; the account itself stays in the panel and the customer's chat.
    [Fact]
    public async Task Staff_see_an_order_but_never_the_delivered_account()
    {
        var r = Staffed();
        var order = Delivered(r, 1, "user: secret@spotify");

        await Run(r, Tap("adm:os", Me), Text(order.Code, Me));

        var card = Screen(r);
        Assert.Contains(order.Code, card);
        Assert.DoesNotContain("secret@spotify", card);
    }

    [Fact]
    public async Task A_broadcast_goes_out_only_after_its_preview_is_confirmed_and_only_to_customers_who_want_it()
    {
        var r = Staffed();
        r.Store.LinkTelegram(1, 913101, "ali_tg");                       // a linked customer
        r.Store.EnsureTelegramGuest(913102, "Guest");                    // someone who bought in the bot
        var zahra = IdOf(r, "zahra");
        r.Store.LinkTelegram(zahra, 913103, "z_tg");
        r.Store.UpdateUser(zahra, u => u.TelegramNotify = false);       // turned notices off
        const string broadcast = "text=📣 تخفیف آخر هفته";

        await Run(r, Tap("adm:bcy", Me));                                 // nothing previewed: nothing goes
        await Run(r, Tap("adm:bc", Me), Text("تخفیف آخر هفته", Me));
        Assert.Contains("\"callback_data\":\"adm:bcy\"", Last(r, "sendMessage"));
        Assert.DoesNotContain(r.Telegram.Calls, c => c.Body.Contains(broadcast));

        await Run(r, Tap("adm:bcy", Me));
        await WaitFor(r, c => c.Method == "sendMessage" && c.Body.Contains($"chat_id={Me}") && c.Body.Contains("ارسال شد"));

        List<(string Method, string Body)> sent;
        lock (r.Telegram.Calls) sent = r.Telegram.Calls.Where(c => c.Body.Contains(broadcast)).ToList();
        Assert.Contains(sent, c => c.Body.Contains("chat_id=913101"));
        Assert.Contains(sent, c => c.Body.Contains("chat_id=913102"));
        Assert.DoesNotContain(sent, c => c.Body.Contains("chat_id=913103"));
        Assert.DoesNotContain(sent, c => c.Body.Contains($"chat_id={Me}"));   // staff are not customers

        // The preview was used up: confirming again sends nothing more.
        await Run(r, Tap("adm:bcy", Me));
        lock (r.Telegram.Calls) Assert.Equal(sent.Count, r.Telegram.Calls.Count(c => c.Body.Contains(broadcast)));
    }
}

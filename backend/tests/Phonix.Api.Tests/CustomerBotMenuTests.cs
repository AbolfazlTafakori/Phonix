using System.Text.Json;
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
    public async Task Buying_goes_in_numbered_steps_each_with_a_way_back()
    {
        var r = Setup();

        await Run(r, Tap("shop:cats", Me));
        Assert.Contains("مرحله اول", Screen(r));

        await Run(r, Tap("shop:c:2", Me));
        var products = Screen(r);
        Assert.Contains("مرحله دوم", products);
        Assert.Contains("\"callback_data\":\"shop:cats\"", products);   // one step back
        Assert.Contains("\"callback_data\":\"home\"", products);        // and always the main menu

        await Run(r, Tap($"shop:p:{Spotify}", Me));
        var product = Screen(r);
        Assert.Contains("مرحله سوم", product);
        Assert.Contains("\"callback_data\":\"shop:c:2\"", product);
    }

    // ── How it looks ────────────────────────────────────────────────────────────────────────────────────────

    // The buttons of the screen the customer is looking at: what each says and does, its colour and premium emoji.
    private static List<JsonElement> Buttons(string call)
    {
        var markup = call.Split('&').First(f => f.StartsWith("reply_markup="))["reply_markup=".Length..];
        using var doc = JsonDocument.Parse(markup);
        var root = doc.RootElement.TryGetProperty("inline_keyboard", out var inline) ? inline : doc.RootElement.GetProperty("keyboard");
        return root.EnumerateArray().SelectMany(row => row.EnumerateArray()).Select(b => b.Clone()).ToList();
    }

    private static string TextOf(JsonElement b) => b.GetProperty("text").GetString()!;

    private static string? Prop(JsonElement b, string name) => b.TryGetProperty(name, out var v) ? v.GetString() : null;

    [Fact]
    public async Task Categories_and_products_are_names_alone_and_the_price_is_on_the_products_own_page()
    {
        var r = Setup();

        await Run(r, Tap("shop:cats", Me));
        Assert.DoesNotContain(Buttons(Screen(r)), b => TextOf(b).Contains('(') || TextOf(b).Contains("تومان"));

        await Run(r, Tap("shop:c:2", Me));
        Assert.DoesNotContain(Buttons(Screen(r)), b => TextOf(b).Contains("تومان"));

        await Run(r, Tap($"shop:p:{Spotify}", Me));
        var buy = Assert.Single(Buttons(Screen(r)), b => Prop(b, "callback_data") == $"shop:b:{Spotify}:0");
        Assert.Contains("تومان", TextOf(buy));
        Assert.Equal("success", Prop(buy, "style"));   // going ahead is green
    }

    [Fact]
    public async Task Buttons_are_coloured_by_what_they_do()
    {
        var r = Setup();
        r.Store.LinkTelegram(1, Me, "ali_tg");

        await Run(r, Tap("home", Me));
        var menu = Buttons(Screen(r));
        Assert.Equal("success", Prop(menu.Single(b => Prop(b, "callback_data") == "shop:cats"), "style"));
        Assert.Equal("primary", Prop(menu.Single(b => Prop(b, "callback_data") == "ord:l:1"), "style"));

        await Run(r, Tap("acc", Me));
        Assert.Equal("danger", Prop(Buttons(Screen(r)).Single(b => Prop(b, "callback_data") == "acc:unlink"), "style"));
    }

    [Fact]
    public async Task A_premium_emoji_replaces_the_plain_one_a_button_starts_with()
    {
        var r = Setup();
        r.Store.SetCustomerBotLook(true, new Dictionary<string, string> { ["🛍"] = "5368324170671202286" });

        await Run(r, Tap("home", Me));

        var buy = Buttons(Screen(r)).Single(b => Prop(b, "callback_data") == "shop:cats");
        Assert.Equal("5368324170671202286", Prop(buy, "icon_custom_emoji_id"));
        Assert.DoesNotContain("🛍", TextOf(buy));
    }

    [Fact]
    public async Task When_telegram_refuses_premium_emoji_the_same_screen_goes_out_plain()
    {
        var r = Setup();
        r.Store.SetCustomerBotLook(true, new Dictionary<string, string> { ["🛍"] = "5368324170671202286" });
        r.Telegram.RefusePremium = true;

        await Run(r, Tap("home", Me));

        var buy = Buttons(Screen(r)).Single(b => Prop(b, "callback_data") == "shop:cats");
        Assert.Null(Prop(buy, "icon_custom_emoji_id"));
        Assert.StartsWith("🛍", TextOf(buy));

        // And premium is left alone for a while rather than refused on every screen.
        var before = r.Telegram.Calls.Count;
        await Run(r, Tap("acc", Me));
        Assert.DoesNotContain(r.Telegram.Calls.Skip(before), c => c.Body.Contains("icon_custom_emoji_id"));
    }

    // An Admin sends premium emoji to the bot; each takes the place of the plain emoji it stands for.
    [Fact]
    public async Task An_admin_sets_the_premium_emoji_by_sending_them()
    {
        var r = Staffed();
        await Run(r, Tap("adm:look:s", Me));

        const string text = "🛍 📦";   // 🛍 at 0 (2 UTF-16 units), 📦 at 3
        r.Telegram.Updates.Enqueue(JsonSerializer.Serialize(new
        {
            update_id = 991,
            message = new
            {
                message_id = 9, text, from = new { id = Me, first_name = "Reza" }, chat = new { id = Me, type = "private" },
                entities = new object[]
                {
                    new { type = "custom_emoji", offset = 0, length = 2, custom_emoji_id = "111" },
                    new { type = "custom_emoji", offset = 3, length = 2, custom_emoji_id = "222" },
                },
            },
        }));
        await r.Bot.ProcessUpdatesAsync(0);

        var look = r.Store.GetTelegramSettings();
        Assert.True(look.CustomerBotPremium);
        Assert.Equal("111", look.CustomerBotEmoji["🛍"]);
        Assert.Equal("222", look.CustomerBotEmoji["📦"]);
    }

    [Fact]
    public async Task Only_an_admin_changes_the_look()
    {
        var r = Staffed("mohammad");
        r.Store.UpdateUser(IdOf(r, "mohammad"), u => u.Permissions = AdminMenuKeys());

        await Run(r, Tap("adm:look:t", Me));

        Assert.False(r.Store.GetTelegramSettings().CustomerBotPremium);
    }

    // ── Configs: location, then plan ───────────────────────────────────────────────────────────────────────

    // A V2Ray product sold from two locations, opened for the bot.
    private static (int ProductId, int Netherlands, int Germany) Configs(Rig r)
    {
        var nl = r.Store.AddV2RayCategory(new V2RayCategory { Name = "هلند 🇳🇱", SortOrder = 1 });
        var de = r.Store.AddV2RayCategory(new V2RayCategory { Name = "آلمان 🇩🇪", SortOrder = 2 });
        var nlPlan = r.Store.AddV2RayPlan(new V2RayPlan { CategoryId = nl.Id, Title = "۳۰ گیگ یک‌ماهه", PanelId = 7, InboundIds = new() { 1 }, VolumeGb = 30, DurationDays = 30, Price = 150_000 });
        var dePlan = r.Store.AddV2RayPlan(new V2RayPlan { CategoryId = de.Id, Title = "۵۰ گیگ یک‌ماهه", PanelId = 8, InboundIds = new() { 1 }, VolumeGb = 50, DurationDays = 30, Price = 220_000 });
        var product = r.Store.AddProduct(new Product
        {
            Name = "کانفیگ V2Ray", CategoryId = 1, Price = 150_000, IsActive = true, V2RayCategoryId = nl.Id, TelegramGuestSale = true,
        });
        return (product.Id, nlPlan.Id, dePlan.Id);
    }

    [Fact]
    public async Task Buying_a_config_is_a_location_then_a_plan_with_its_price()
    {
        var r = Setup();
        var (product, nl, de) = Configs(r);

        await Run(r, Tap("home", Me));
        Assert.Contains(Buttons(Screen(r)), b => Prop(b, "callback_data") == "cfg");

        await Run(r, Tap("cfg", Me));   // the only config product: straight to its locations
        var locations = Buttons(Screen(r));
        Assert.Contains(locations, b => Prop(b, "callback_data") == $"cfg:t:{product}:0");
        Assert.Contains(locations, b => Prop(b, "callback_data") == $"cfg:t:{product}:1");
        Assert.Contains("مرحله اول", Screen(r));

        await Run(r, Tap($"cfg:t:{product}:1", Me));
        var plans = Buttons(Screen(r));
        var plan = Assert.Single(plans, b => Prop(b, "callback_data")?.StartsWith("shop:b:") == true);
        Assert.Equal($"shop:b:{product}:{de}", Prop(plan, "callback_data"));
        Assert.Contains("تومان", TextOf(plan));
        Assert.Contains(plans, b => Prop(b, "callback_data") == $"cfg:p:{product}");   // back to the locations
        Assert.Contains("مرحله دوم", Screen(r));
    }

    // ── My services ─────────────────────────────────────────────────────────────────────────────────────────

    private static (Order Order, OrderUnit Unit) ServiceOf(Rig r, int product, int plan, int panel, string token)
    {
        var guest = r.Store.EnsureTelegramGuest(Me, "Sara");
        var order = r.Store.PlaceOrder(guest, new[] { (product, 1, (int?)plan) }, "کارت به کارت", fromWallet: false, paymentMethodId: 1,
            bot: new BotCheckout("1__0123456789abcdef0123456789abcdef.jpg", null, "Sara")).Order!;
        var unit = order.Units[0];
        r.Store.SetUnitV2Ray(order.Id, unit.Id, new V2RayAccount
        {
            PanelId = panel, PlanId = plan, Email = "sara-1", Uuid = "u-1", Token = token, SubUrl = "https://sub.test/s/abc",
            Protocol = "vless", Network = "tcp", VolumeGb = 30, DurationDays = 30,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-25), ExpiresAtUtc = DateTime.UtcNow.AddDays(5),
        });
        return (r.Store.GetOrder(order.Id)!, r.Store.GetOrder(order.Id)!.Units[0]);
    }

    [Fact]
    public async Task A_service_is_a_card_with_its_link_and_renewal()
    {
        var r = Setup();
        var (product, nl, _) = Configs(r);
        var (order, unit) = ServiceOf(r, product, nl, 7, "tok-1");

        await Run(r, Tap("svc:l:1", Me));
        Assert.Contains(Buttons(Screen(r)), b => Prop(b, "callback_data") == $"svc:v:{order.Id}:{unit.Id}");

        await Run(r, Tap($"svc:v:{order.Id}:{unit.Id}", Me));
        var card = Buttons(Screen(r));
        Assert.Contains(card, b => Prop(b, "callback_data") == "noop");                         // the facts table
        Assert.Contains(card, b => Prop(b, "callback_data") == $"svc:s:{order.Id}:{unit.Id}");
        Assert.Contains(card, b => Prop(b, "callback_data") == $"svc:r:{order.Id}:{unit.Id}");
        Assert.Contains(card, b => Prop(b, "url") == "https://shop.test/config/tok-1");          // live usage and QR

        await Run(r, Tap($"svc:s:{order.Id}:{unit.Id}", Me));
        Assert.Contains("<code>https://sub.test/s/abc</code>", Last(r, "sendMessage"));
    }

    // Renewing here: the same server only, paid by receipt, placed as a renewal of that very service.
    [Fact]
    public async Task A_service_is_renewed_in_the_bot_on_its_own_server()
    {
        var r = Setup();
        var (product, nl, de) = Configs(r);
        var (order, unit) = ServiceOf(r, product, nl, 7, "tok-2");
        r.Store.SetOrderStatus(order.Id, OrderStatus.Preparing);   // its own receipt was reviewed long ago

        await Run(r, Tap($"svc:r:{order.Id}:{unit.Id}", Me));
        var plans = Buttons(Screen(r)).Where(b => Prop(b, "callback_data")?.StartsWith("svc:rp:") == true).ToList();
        Assert.Equal($"svc:rp:{order.Id}:{unit.Id}:{nl}", Prop(Assert.Single(plans), "callback_data"));   // not the other server's

        await Run(r, Tap($"svc:rp:{order.Id}:{unit.Id}:{de}", Me));   // forged: another server
        Assert.DoesNotContain("عکس رسید", Screen(r));

        await Run(r, Tap($"svc:rp:{order.Id}:{unit.Id}:{nl}", Me));
        Assert.Contains("فاکتور تمدید", Screen(r));
        await Run(r, ReceiptPhoto(chatId: Me));

        var renewal = r.Store.GetUserOrders(order.UserId).OrderByDescending(o => o.Id).First();
        Assert.NotEqual(order.Id, renewal.Id);
        Assert.Equal("tok-2", renewal.Units.Single().V2RayRenewToken);
    }

    [Fact]
    public async Task Someone_elses_service_is_never_shown_or_renewed()
    {
        var r = Setup();
        var (product, nl, _) = Configs(r);
        var (order, unit) = ServiceOf(r, product, nl, 7, "tok-3");
        const long stranger = 913050;

        await Run(r, Tap($"svc:v:{order.Id}:{unit.Id}", stranger), Tap($"svc:s:{order.Id}:{unit.Id}", stranger),
            Tap($"svc:rp:{order.Id}:{unit.Id}:{nl}", stranger));

        Assert.DoesNotContain(r.Telegram.Calls, c => c.Body.Contains($"chat_id={stranger}") && (c.Body.Contains("sub.test") || c.Body.Contains("فاکتور")));
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

using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Phonix.Api.Data;
using Phonix.Api.Security;
using Xunit;

namespace Phonix.Api.Tests;

// Signing in from the shop inside Telegram, over the real HTTP pipeline: only genuine launch data for a
// Telegram the customer linked themselves opens their account.
[Collection("api")]
public class TelegramLoginTests : IClassFixture<PhonixAppFactory>
{
    private const long TelegramUser = 555_000_111;

    static TelegramLoginTests() => Environment.SetEnvironmentVariable("PHONIX_FRONTEND_URL", "https://shop.test");

    private readonly PhonixAppFactory _factory;
    private readonly IDataStore _store;

    public TelegramLoginTests(PhonixAppFactory factory)
    {
        _factory = factory;
        _store = factory.Services.GetRequiredService<IDataStore>();
        _store.SetCustomerBot(true, false, TelegramLaunch.Token, "PhoenixTestBot", shop: true);
    }

    private record Result(bool Linked, string? Token, UserRef? User);
    private record UserRef(int Id, string Username);

    private Task<HttpResponseMessage> SignIn(string initData) =>
        _factory.CreateClient().PostAsJsonAsync("/api/auth/telegram", new { initData });

    private static bool SetsSession(HttpResponseMessage res) =>
        res.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(c => c.StartsWith(AuthCookies.Token + "="));

    [Fact]
    public async Task A_linked_customer_is_signed_in()
    {
        _store.LinkTelegram(1, TelegramUser, "ali_tg");

        var res = await SignIn(TelegramLaunch.InitData(TelegramUser));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = (await res.Content.ReadFromJsonAsync<Result>())!;
        Assert.True(body.Linked);
        Assert.Equal(1, body.User!.Id);
        Assert.True(SetsSession(res));
    }

    [Fact]
    public async Task An_unlinked_telegram_gets_no_session()
    {
        var res = await SignIn(TelegramLaunch.InitData(TelegramUser + 7));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = (await res.Content.ReadFromJsonAsync<Result>())!;
        Assert.False(body.Linked);
        Assert.Null(body.User);
        Assert.False(SetsSession(res));
    }

    [Fact]
    public async Task Forged_or_stale_launch_data_is_refused()
    {
        _store.LinkTelegram(1, TelegramUser, "ali_tg");

        var forged = await SignIn(TelegramLaunch.InitData(TelegramUser, token: "987654321:BBOtherBotTokenForTests_abcdefghijklmno"));
        var stale = await SignIn(TelegramLaunch.InitData(TelegramUser, signedAt: DateTime.UtcNow.AddDays(-1)));

        Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        Assert.False(SetsSession(forged) || SetsSession(stale));
    }

    [Fact]
    public async Task A_blocked_account_stays_out()
    {
        var sara = _store.GetUsers().First(u => u.Blocked);
        _store.LinkTelegram(sara.Id, TelegramUser + 9, null);

        var res = await SignIn(TelegramLaunch.InitData(TelegramUser + 9));

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.False(SetsSession(res));
    }

    [Fact]
    public async Task Nothing_signs_in_while_the_shop_is_off()
    {
        _store.LinkTelegram(1, TelegramUser, "ali_tg");
        _store.SetCustomerBot(true, true, null, null, shop: false);

        var res = await SignIn(TelegramLaunch.InitData(TelegramUser));

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.False(SetsSession(res));
    }
}

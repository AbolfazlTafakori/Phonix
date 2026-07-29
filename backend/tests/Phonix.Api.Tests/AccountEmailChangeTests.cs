using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Phonix.Api.Tests;

// The change-email flow never touches the account's Email column until the NEW address proves itself by
// clicking the emailed link — see AccountController.ChangeEmail/ConfirmEmailChange. SMTP is unconfigured in
// the test store, so EmailSender no-ops instead of actually sending; these tests read the token straight out
// of the Tokens table (what the email link would have carried) rather than mocking the mail pipeline.
[Collection("api")]
public class AccountEmailChangeTests : IClassFixture<PhonixAppFactory>
{
    private readonly PhonixAppFactory _factory;
    private readonly HttpClient _client;
    private readonly string _dbPath;

    public AccountEmailChangeTests(PhonixAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        var dataFile = Environment.GetEnvironmentVariable("PHONIX_DATA_FILE")!;
        _dbPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dataFile))!, "store.db");
    }

    // A brand-new client with its own empty cookie jar — simulates the confirmation link being opened on a
    // device/browser that never had a session here, as opposed to _client, which still carries the cookie
    // AuthCookies.Issue set during register/login. Using _client for this would trip the CSRF guard (a
    // cookie-authenticated unsafe request with no Authorization header and no CSRF header), which has nothing
    // to do with the thing this test is actually checking.
    private HttpClient FreshClient() => _factory.CreateClient();

    private static HttpRequestMessage Authed(HttpMethod method, string url, string? token, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (token is not null) req.Headers.Add("Authorization", $"Bearer {token}");
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    private async Task<(int Id, string Token)> RegisterAndLoginAsync(string username, string email, string password)
    {
        var reg = await _client.PostAsJsonAsync("/api/auth/register", new { name = username, username, email, phone = "", password });
        Assert.Equal(HttpStatusCode.OK, reg.StatusCode);
        var body = await reg.Content.ReadFromJsonAsync<AuthResult>();
        return (body!.User!.Id, body.Token!);
    }

    private record AuthResult(string? Token, UserRef? User);
    private record UserRef(int Id);
    private record MeDto(string Email, bool EmailVerified);

    // Reads the token straight from the Tokens table — the same value that would have been embedded in the
    // confirmation link mailed to the new address.
    private string ReadToken(int userId, string purpose)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn.QueryFirst<string>(
            "SELECT Token FROM Tokens WHERE UserId = @userId AND Purpose = @purpose ORDER BY rowid DESC LIMIT 1",
            new { userId, purpose });
    }

    [Fact]
    public async Task Email_only_changes_after_the_new_address_confirms_it()
    {
        var (id, token) = await RegisterAndLoginAsync("emailchange1", "old-emailchange1@example.com", "pass1234");

        var change = await _client.SendAsync(Authed(HttpMethod.Post, "/api/account/change-email", token,
            new { currentPassword = "pass1234", newEmail = "new-emailchange1@example.com" }));
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);

        // Not applied yet: /me still shows the old address.
        var before = await (await _client.SendAsync(Authed(HttpMethod.Get, "/api/account/me", token))).Content.ReadFromJsonAsync<MeDto>();
        Assert.Equal("old-emailchange1@example.com", before!.Email);

        var confirmToken = ReadToken(id, "change-email");
        var confirm = await FreshClient().PostAsJsonAsync("/api/account/confirm-email-change", new { token = confirmToken });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);

        // Applied now, and marked verified — the click itself is the proof.
        var after = await (await _client.SendAsync(Authed(HttpMethod.Get, "/api/account/me", token))).Content.ReadFromJsonAsync<MeDto>();
        Assert.Equal("new-emailchange1@example.com", after!.Email);
        Assert.True(after.EmailVerified);
    }

    [Fact]
    public async Task Confirm_email_change_works_with_no_session_at_all()
    {
        // The link is clicked from whatever inbox/device the new address happens to be open on — it may
        // never have had a session on this app, so the confirm endpoint must not require one.
        var (id, token) = await RegisterAndLoginAsync("emailchange2", "old-emailchange2@example.com", "pass1234");
        await _client.SendAsync(Authed(HttpMethod.Post, "/api/account/change-email", token,
            new { currentPassword = "pass1234", newEmail = "new-emailchange2@example.com" }));

        var confirmToken = ReadToken(id, "change-email");
        var confirm = await FreshClient().PostAsJsonAsync("/api/account/confirm-email-change", new { token = confirmToken });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
    }

    [Fact]
    public async Task Change_email_rejects_the_wrong_current_password()
    {
        var (_, token) = await RegisterAndLoginAsync("emailchange3", "old-emailchange3@example.com", "pass1234");
        var change = await _client.SendAsync(Authed(HttpMethod.Post, "/api/account/change-email", token,
            new { currentPassword = "wrong-password", newEmail = "new-emailchange3@example.com" }));
        Assert.Equal(HttpStatusCode.BadRequest, change.StatusCode);
    }

    [Fact]
    public async Task Change_email_rejects_an_address_already_taken()
    {
        await RegisterAndLoginAsync("emailchange4a", "taken-emailchange4@example.com", "pass1234");
        var (_, token) = await RegisterAndLoginAsync("emailchange4b", "old-emailchange4b@example.com", "pass1234");

        var change = await _client.SendAsync(Authed(HttpMethod.Post, "/api/account/change-email", token,
            new { currentPassword = "pass1234", newEmail = "taken-emailchange4@example.com" }));
        Assert.Equal(HttpStatusCode.BadRequest, change.StatusCode);
    }

    [Fact]
    public async Task Confirm_email_change_rejects_an_unknown_token()
    {
        var res = await _client.PostAsJsonAsync("/api/account/confirm-email-change", new { token = "not-a-real-token" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}

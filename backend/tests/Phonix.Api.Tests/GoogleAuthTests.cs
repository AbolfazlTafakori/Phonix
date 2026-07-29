using System.Net;
using System.Net.Http.Json;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Phonix.Api.Tests;

// Fakes Google's tokeninfo endpoint: whatever JSON the caller puts in the "credential" field is echoed back
// verbatim as the "Google" response — the test drives what Google is pretended to have said, without needing
// a real signed JWT or network access.
internal sealed class FakeGoogleTokenInfoHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var query = QueryHelpers.ParseQuery(request.RequestUri!.Query);
        var idToken = query.TryGetValue("id_token", out var v) ? v.ToString() : "";
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(idToken, Encoding.UTF8, "application/json"),
        };
        return Task.FromResult(response);
    }
}

// Dedicated host with Google sign-in configured and its outbound call to Google intercepted.
public class GoogleAuthAppFactory : WebApplicationFactory<Program>
{
    public GoogleAuthAppFactory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "phonix-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var dataFile = Path.Combine(dir, "store.json");
        TestSeed.WriteLegacyFile(dataFile);
        Environment.SetEnvironmentVariable("PHONIX_DATA_FILE", dataFile);
        Environment.SetEnvironmentVariable("PHONIX_LOG_DIR", dir);
        Environment.SetEnvironmentVariable("PHONIX_DISABLE_TARPIT", "true");
        Environment.SetEnvironmentVariable("PHONIX_AUTH_RATE_LIMIT", "100000");
        Environment.SetEnvironmentVariable("PHONIX_REQUIRE_ADMIN_2FA", "false");
        Environment.SetEnvironmentVariable("PHONIX_REQUIRE_CAPTCHA", "false");
        Environment.SetEnvironmentVariable("PHONIX_GOOGLE_CLIENT_ID", "test-client-id");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            // Overrides Program.cs's plain AddHttpClient() default client so AuthController.Google()'s
            // _httpFactory.CreateClient().GetAsync(...) hits the fake instead of the real internet.
            services.AddHttpClient(string.Empty).ConfigurePrimaryHttpMessageHandler(() => new FakeGoogleTokenInfoHandler());
        });
    }
}

[Collection("api")]
public class GoogleAuthTests : IClassFixture<GoogleAuthAppFactory>
{
    private readonly GoogleAuthAppFactory _factory;
    private readonly HttpClient _client;
    private readonly string _dbPath;

    public GoogleAuthTests(GoogleAuthAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        var dataFile = Environment.GetEnvironmentVariable("PHONIX_DATA_FILE")!;
        _dbPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dataFile))!, "store.db");
    }

    // A brand-new client with its own empty cookie jar. A "Sign in with Google" click always represents
    // someone arriving fresh — the account-takeover scenario specifically depends on the Google requester
    // being a DIFFERENT person/browser than whoever registered first, so this isn't just working around the
    // CSRF guard, it's the accurate simulation. (Reusing _client after an earlier register on it would also
    // trip the double-submit CSRF guard here, same as in AccountEmailChangeTests.)
    private HttpClient FreshClient() => _factory.CreateClient();

    // Reads the verify-email token straight from the Tokens table — the same value that would have been
    // embedded in the confirmation link mailed to the user, so the test can complete a real verification
    // without needing an actual mailbox.
    private string ReadVerifyToken(int userId)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn.QueryFirst<string>(
            "SELECT Token FROM Tokens WHERE UserId = @userId AND Purpose = 'verify' ORDER BY rowid DESC LIMIT 1",
            new { userId });
    }

    private static string GoogleCredential(string email, bool emailVerified = true, string aud = "test-client-id", string? name = null) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            aud,
            email,
            email_verified = emailVerified ? "true" : "false",
            name = name ?? email.Split('@')[0],
            sub = "1234567890",
        });

    private record AuthResult(string? Token, UserRef? User);
    private record UserRef(int Id, string? Email);

    [Fact]
    public async Task First_time_google_sign_in_creates_a_verified_account()
    {
        var res = await _client.PostAsJsonAsync("/api/auth/google", new { credential = GoogleCredential("newgoogleuser@example.com") });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<AuthResult>();
        Assert.NotNull(body!.Token);
        Assert.Equal("newgoogleuser@example.com", body.User!.Email);
    }

    [Fact]
    public async Task Wrong_audience_token_is_rejected()
    {
        var res = await _client.PostAsJsonAsync("/api/auth/google",
            new { credential = GoogleCredential("someone@example.com", aud: "some-other-app") });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    // Regression for the account-takeover bug: registering with someone else's email (never proving you own
    // it) must not let that same email's later, real Google sign-in be silently absorbed into your account.
    [Fact]
    public async Task Google_sign_in_refuses_to_merge_into_an_unverified_squatted_account()
    {
        var victimEmail = "squatted-victim@example.com";

        // The "attacker" registers first with the victim's real email — password-based signup never proves
        // email ownership, so this account sits there EmailVerified=false.
        var register = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            name = "attacker", username = "squatattacker", email = victimEmail, phone = "", password = "attackerpass1",
        });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        // The real owner later signs in with Google using that same, genuinely-owned address — from their own
        // browser, which never had a cookie from the attacker's earlier register call.
        var googleSignIn = await FreshClient().PostAsJsonAsync("/api/auth/google", new { credential = GoogleCredential(victimEmail) });

        // Must NOT silently log them into the attacker-controlled account.
        Assert.Equal(HttpStatusCode.Conflict, googleSignIn.StatusCode);
    }

    [Fact]
    public async Task Google_sign_in_still_works_for_an_already_verified_existing_account()
    {
        var email = "already-verified@example.com";
        var register = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            name = "real", username = "realowner", email, phone = "", password = "realpassword1",
        });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        var regBody = await register.Content.ReadFromJsonAsync<AuthResult>();

        // Verify through the real channel — the same token that would have been mailed to this address.
        // Both calls below use a fresh, cookie-less client: neither verify-email nor google sign-in is tied
        // to the registering session (both are [AllowAnonymous], reachable from any device/browser), and
        // reusing _client here would trip the double-submit CSRF guard for the same reason noted above.
        var verifyToken = ReadVerifyToken(regBody!.User!.Id);
        var verify = await FreshClient().PostAsJsonAsync("/api/auth/verify-email", new { token = verifyToken });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

        // Now that the address is genuinely proven, Google sign-in with the SAME email must succeed and log
        // into this same account — the fix must not have made every Google sign-in fail closed.
        var googleSignIn = await FreshClient().PostAsJsonAsync("/api/auth/google", new { credential = GoogleCredential(email) });
        Assert.Equal(HttpStatusCode.OK, googleSignIn.StatusCode);
        var body = await googleSignIn.Content.ReadFromJsonAsync<AuthResult>();
        Assert.Equal(regBody.User.Id, body!.User!.Id);
    }
}

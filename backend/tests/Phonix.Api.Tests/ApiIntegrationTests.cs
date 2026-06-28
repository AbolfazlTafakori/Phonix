using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Phonix.Api.Tests;

// Both integration factories flip the same process-wide env vars (the 2FA gate especially), so the two
// classes share one collection to run sequentially rather than racing in parallel.
[CollectionDefinition("api")]
public class ApiCollection { }

// RFC 6238 TOTP code generator mirroring the server's TotpService, so tests can complete a real 2FA enrolment.
internal static class TestTotp
{
    public static string Code(string base32Secret)
    {
        var key = Base32Decode(base32Secret);
        var step = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var data = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian) Array.Reverse(data);
        var hash = HMACSHA1.HashData(key, data);
        var b = hash[^1] & 0x0f;
        var bin = ((hash[b] & 0x7f) << 24) | ((hash[b + 1] & 0xff) << 16) | ((hash[b + 2] & 0xff) << 8) | (hash[b + 3] & 0xff);
        return (bin % 1_000_000).ToString().PadLeft(6, '0');
    }

    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private static byte[] Base32Decode(string input)
    {
        var clean = input.Trim().TrimEnd('=').ToUpperInvariant();
        var output = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in clean)
        {
            buffer = (buffer << 5) | Alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8) { bits -= 8; output.Add((byte)((buffer >> bits) & 0xff)); }
        }
        return output.ToArray();
    }
}

// Boots the real API in-memory (TestServer) against an isolated temp store, and drives it over HTTP.
public class PhonixAppFactory : WebApplicationFactory<Program>
{
    public PhonixAppFactory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "phonix-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable("PHONIX_DATA_FILE", Path.Combine(dir, "store.json"));
        Environment.SetEnvironmentVariable("PHONIX_LOG_DIR", dir);
        Environment.SetEnvironmentVariable("PHONIX_DISABLE_TARPIT", "true");
        Environment.SetEnvironmentVariable("PHONIX_AUTH_RATE_LIMIT", "100000");
        // The general suite exercises admin endpoints without enrolling 2FA, so the mandatory-setup gate is
        // off here; its enforcement is covered separately by MandatoryTwoFactorTests.
        Environment.SetEnvironmentVariable("PHONIX_REQUIRE_ADMIN_2FA", "false");
        Environment.SetEnvironmentVariable("PHONIX_REQUIRE_CAPTCHA", "false");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development");
}

[Collection("api")]
public class ApiIntegrationTests : IClassFixture<PhonixAppFactory>
{
    private readonly HttpClient _client;

    public ApiIntegrationTests(PhonixAppFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Health_endpoint_reports_healthy()
    {
        var res = await _client.GetAsync("/health");
        res.EnsureSuccessStatusCode();
        Assert.Contains("Healthy", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_succeeds_with_seeded_admin()
    {
        var res = await _client.PostAsJsonAsync("/api/auth/login", new { identifier = "reza", password = "1234" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("\"token\"", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_fails_with_wrong_password()
    {
        var res = await _client.PostAsJsonAsync("/api/auth/login", new { identifier = "reza", password = "nope" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Blocked_account_is_forbidden()
    {
        // sara (seed) is Blocked = true.
        var res = await _client.PostAsJsonAsync("/api/auth/login", new { identifier = "sara", password = "1234" });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Products_endpoint_exposes_delivery_template()
    {
        var res = await _client.GetAsync("/api/products");
        res.EnsureSuccessStatusCode();
        Assert.Contains("deliveryTemplate", await res.Content.ReadAsStringAsync());
    }

    // admin=true performs an admin-PANEL login (yields an admin-scoped session that may use the panel);
    // admin=false is a plain main-site login. Most tests drive the panel, so it defaults to true.
    private async Task<string> LoginTokenAsync(string identifier, string password, bool admin = true)
    {
        var res = await _client.PostAsJsonAsync("/api/auth/login", new { identifier, password, admin });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body!.Token);
        return body.Token!;
    }

    private static HttpRequestMessage Authed(HttpMethod method, string url, string token, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("Authorization", $"Bearer {token}");
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    private record LoginResponse(string? Token);

    [Fact]
    public async Task Permissions_catalog_excludes_dashboard_and_system_sections()
    {
        // Regression guard: a static-init ordering bug once made this endpoint 500. It must list the
        // assignable Support sections and never the always-on dashboard or Admin-only system sections.
        var admin = await LoginTokenAsync("reza", "1234");
        var res = await _client.SendAsync(Authed(HttpMethod.Get, "/api/staff/permissions", admin));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var keys = await res.Content.ReadAsStringAsync();
        Assert.Contains("\"orders-receipts\"", keys);
        Assert.Contains("\"tickets\"", keys);
        Assert.DoesNotContain("\"dashboard\"", keys);
        Assert.DoesNotContain("\"staff\"", keys);
        Assert.DoesNotContain("\"backup\"", keys);
    }

    private async Task<int> RegisterAsync(string username, string email, string password)
    {
        var res = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            name = username, username, email, phone = "", password,
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<AuthResult>())!.User!.Id;
    }

    private record AuthResult(UserRef? User);
    private record UserRef(int Id);

    [Fact]
    public async Task Limited_staff_is_gated_to_granted_sections_and_updates_live()
    {
        var admin = await LoginTokenAsync("reza", "1234");

        // Promote an EXISTING account (by username) to Support, limited to orders + tickets.
        await RegisterAsync("permtest", "permtest@example.com", "test1234");
        var create = await _client.SendAsync(Authed(HttpMethod.Post, "/api/staff", admin, new
        {
            username = "permtest", role = "Support", permissions = new[] { "orders-receipts", "tickets" },
        }));
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<CreatedStaff>();
        var staff = await LoginTokenAsync("permtest", "test1234");

        // Menu is filtered to the granted sections (plus the always-on dashboard).
        var menu = await (await _client.SendAsync(Authed(HttpMethod.Get, "/api/admin/menu", staff)))
            .Content.ReadAsStringAsync();
        Assert.Contains("\"orders-receipts\"", menu);
        Assert.Contains("\"tickets\"", menu);
        Assert.DoesNotContain("\"discounts\"", menu);
        // 2FA security is available to every staff level, even a limited one with no system access.
        Assert.Contains("\"security\"", menu);

        // Granted sections allowed; everything else (and the Admin-only system area) is 403.
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(Authed(HttpMethod.Get, "/api/orders", staff))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(Authed(HttpMethod.Get, "/api/discounts", staff))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(Authed(HttpMethod.Get, "/api/staff", staff))).StatusCode);

        // Re-grant: swap tickets→discounts. The live session reflects it with no re-login.
        var update = await _client.SendAsync(Authed(HttpMethod.Put, $"/api/staff/{created!.Id}", admin, new
        {
            role = "Support", permissions = new[] { "orders-receipts", "discounts" },
        }));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(Authed(HttpMethod.Get, "/api/discounts", staff))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(Authed(HttpMethod.Get, "/api/tickets", staff))).StatusCode);

        // Cleanup.
        Assert.Equal(HttpStatusCode.NoContent, (await _client.SendAsync(Authed(HttpMethod.Delete, $"/api/staff/{created.Id}", admin))).StatusCode);
    }

    private record CreatedStaff(int Id);

    [Fact]
    public async Task Email_must_be_unique_across_accounts()
    {
        await RegisterAsync("uniqone", "shared@example.com", "pass1234");
        // A second registration with the same email is rejected.
        var dup = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            name = "uniqtwo", username = "uniqtwo", email = "shared@example.com", phone = "", password = "pass1234",
        });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);

        // And an admin can't move that email onto another existing account either.
        var admin = await LoginTokenAsync("reza", "1234");
        var otherId = await RegisterAsync("uniqthree", "three@example.com", "pass1234");
        var res = await _client.SendAsync(Authed(HttpMethod.Put, $"/api/users/{otherId}", admin, new { email = "shared@example.com" }));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Admin_can_open_a_ticket_on_behalf_of_a_user()
    {
        var admin = await LoginTokenAsync("reza", "1234");
        var userId = await RegisterAsync("tickuser", "tickuser@example.com", "pass1234");

        var create = await _client.SendAsync(Authed(HttpMethod.Post, "/api/tickets/admin", admin, new
        {
            userId, subject = "اطلاع‌رسانی", department = "عمومی", body = "سلام، این تیکت را پشتیبانی باز کرد.",
        }));
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        // The ticket belongs to the user, opened as an admin message and already answered.
        var ticket = await create.Content.ReadAsStringAsync();
        Assert.Contains("\"answered\"", ticket.ToLowerInvariant());

        // It surfaces in that user's own ticket list (a plain main-site login — not staff).
        var user = await LoginTokenAsync("tickuser", "pass1234", admin: false);
        var mine = await (await _client.SendAsync(Authed(HttpMethod.Get, $"/api/tickets/user/{userId}", user))).Content.ReadAsStringAsync();
        Assert.Contains("اطلاع‌رسانی", mine);
    }

    [Fact]
    public async Task Owner_can_disable_a_staff_members_2fa()
    {
        var admin = await LoginTokenAsync("reza", "1234");
        await RegisterAsync("twofa1", "twofa1@example.com", "pass1234");
        var create = await _client.SendAsync(Authed(HttpMethod.Post, "/api/staff", admin, new { username = "twofa1", role = "Support", permissions = new[] { "orders-receipts" } }));
        var staffId = (await create.Content.ReadFromJsonAsync<CreatedStaff>())!.Id;

        // The staff member enrols in 2FA.
        var staff = await LoginTokenAsync("twofa1", "pass1234");
        var setup = await (await _client.SendAsync(Authed(HttpMethod.Post, "/api/auth/2fa/setup", staff))).Content.ReadFromJsonAsync<SetupDto>();
        var enable = await _client.SendAsync(Authed(HttpMethod.Post, "/api/auth/2fa/enable", staff, new { code = TestTotp.Code(setup!.Secret) }));
        Assert.Equal(HttpStatusCode.NoContent, enable.StatusCode);

        // Now a fresh PANEL login requires the second factor.
        var locked = await _client.PostAsJsonAsync("/api/auth/login", new { identifier = "twofa1", password = "pass1234", admin = true });
        Assert.Contains("\"requiresTwoFactor\":true", await locked.Content.ReadAsStringAsync());

        // The owner disables it (no code needed) — rescue for a lost authenticator.
        var disable = await _client.SendAsync(Authed(HttpMethod.Post, $"/api/staff/{staffId}/2fa/disable", admin));
        Assert.Equal(HttpStatusCode.NoContent, disable.StatusCode);

        // Panel login no longer demands a code.
        var open = await _client.PostAsJsonAsync("/api/auth/login", new { identifier = "twofa1", password = "pass1234", admin = true });
        Assert.Contains("\"requiresTwoFactor\":false", await open.Content.ReadAsStringAsync());

        // An admin can't disable their own this way.
        var self = await _client.SendAsync(Authed(HttpMethod.Post, $"/api/staff/{await CurrentIdAsync(admin)}/2fa/disable", admin));
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
    }

    private async Task<int> CurrentIdAsync(string token)
    {
        var me = await (await _client.SendAsync(Authed(HttpMethod.Get, "/api/account/me", token))).Content.ReadFromJsonAsync<UserRef>();
        return me!.Id;
    }

    private record SetupDto(string Secret, string OtpAuthUri);

    [Fact]
    public async Task Main_site_login_does_not_grant_panel_access_but_panel_login_does()
    {
        // A plain main-site login by an admin: succeeds, but the session is NOT admin-scoped.
        var mainSession = await LoginTokenAsync("reza", "1234", admin: false);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(Authed(HttpMethod.Get, "/api/users", mainSession))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(Authed(HttpMethod.Get, "/api/auth/admin-context", mainSession))).StatusCode);

        // The admin-panel login yields an admin-scoped session that the panel accepts.
        var panelSession = await LoginTokenAsync("reza", "1234", admin: true);
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(Authed(HttpMethod.Get, "/api/auth/admin-context", panelSession))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(Authed(HttpMethod.Get, "/api/users", panelSession))).StatusCode);

        // A non-staff account is refused at the panel login itself.
        await RegisterAsync("plainuser", "plainuser@example.com", "pass1234");
        var refused = await _client.PostAsJsonAsync("/api/auth/login", new { identifier = "plainuser", password = "pass1234", admin = true });
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }
}

// Dedicated host with the mandatory-2FA gate ENABLED (the default), isolated from the main suite.
public class MandatoryTwoFactorAppFactory : WebApplicationFactory<Program>
{
    public MandatoryTwoFactorAppFactory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "phonix-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable("PHONIX_DATA_FILE", Path.Combine(dir, "store.json"));
        Environment.SetEnvironmentVariable("PHONIX_LOG_DIR", dir);
        Environment.SetEnvironmentVariable("PHONIX_DISABLE_TARPIT", "true");
        Environment.SetEnvironmentVariable("PHONIX_AUTH_RATE_LIMIT", "100000");
        Environment.SetEnvironmentVariable("PHONIX_REQUIRE_ADMIN_2FA", "true");
        Environment.SetEnvironmentVariable("PHONIX_REQUIRE_CAPTCHA", "false");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development");
}

// Host with the image CAPTCHA enforced on the auth endpoints (the default), isolated from the main suite.
public class CaptchaAppFactory : WebApplicationFactory<Program>
{
    public CaptchaAppFactory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "phonix-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable("PHONIX_DATA_FILE", Path.Combine(dir, "store.json"));
        Environment.SetEnvironmentVariable("PHONIX_LOG_DIR", dir);
        Environment.SetEnvironmentVariable("PHONIX_DISABLE_TARPIT", "true");
        Environment.SetEnvironmentVariable("PHONIX_AUTH_RATE_LIMIT", "100000");
        Environment.SetEnvironmentVariable("PHONIX_REQUIRE_ADMIN_2FA", "false");
        Environment.SetEnvironmentVariable("PHONIX_REQUIRE_CAPTCHA", "true");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development");
}

[Collection("api")]
public class CaptchaTests : IClassFixture<CaptchaAppFactory>
{
    private readonly HttpClient _client;
    public CaptchaTests(CaptchaAppFactory factory) => _client = factory.CreateClient();

    private record CaptchaDto(string Id, string Image);

    [Fact]
    public async Task Captcha_endpoint_issues_an_image_challenge()
    {
        var c = await (await _client.GetAsync("/api/captcha")).Content.ReadFromJsonAsync<CaptchaDto>();
        Assert.False(string.IsNullOrWhiteSpace(c!.Id));
        Assert.StartsWith("data:image/svg+xml;base64,", c.Image);
    }

    [Fact]
    public async Task Login_is_rejected_without_a_valid_captcha()
    {
        // Correct credentials, but no captcha answer → blocked before the password is even considered.
        var res = await _client.PostAsJsonAsync("/api/auth/login", new { identifier = "reza", password = "1234" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("کد امنیتی", await res.Content.ReadAsStringAsync());

        // A made-up captcha id is likewise rejected.
        var res2 = await _client.PostAsJsonAsync("/api/auth/login", new { identifier = "reza", password = "1234", captchaId = "deadbeef", captchaText = "ABCDE" });
        Assert.Equal(HttpStatusCode.BadRequest, res2.StatusCode);
    }
}

[Collection("api")]
public class MandatoryTwoFactorTests : IClassFixture<MandatoryTwoFactorAppFactory>
{
    private readonly HttpClient _client;
    public MandatoryTwoFactorTests(MandatoryTwoFactorAppFactory factory) => _client = factory.CreateClient();

    private static HttpRequestMessage Authed(HttpMethod method, string url, string token, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("Authorization", $"Bearer {token}");
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    private async Task<string> LoginAsync(string id, string pw)
    {
        var res = await _client.PostAsJsonAsync("/api/auth/login", new { identifier = id, password = pw, admin = true });
        return (await res.Content.ReadFromJsonAsync<TokenHolder>())!.Token!;
    }

    private record TokenHolder(string? Token);
    private record SetupDto(string Secret, string OtpAuthUri);

    [Fact]
    public async Task Staff_without_2fa_is_blocked_until_they_enrol()
    {
        // reza (seed admin) has no 2FA. Login itself is allowed (it's how they reach the setup).
        var admin = await LoginAsync("reza", "1234");

        // Admin actions are blocked with the setup marker until 2FA is on.
        var blocked = await _client.SendAsync(Authed(HttpMethod.Get, "/api/users", admin));
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Contains("requiresTwoFactorSetup", await blocked.Content.ReadAsStringAsync());

        // But the endpoints needed to enrol stay reachable.
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(Authed(HttpMethod.Get, "/api/account/me", admin))).StatusCode);
        var setup = await (await _client.SendAsync(Authed(HttpMethod.Post, "/api/auth/2fa/setup", admin))).Content.ReadFromJsonAsync<SetupDto>();
        var enable = await _client.SendAsync(Authed(HttpMethod.Post, "/api/auth/2fa/enable", admin, new { code = TestTotp.Code(setup!.Secret) }));
        Assert.Equal(HttpStatusCode.NoContent, enable.StatusCode);

        // Once enrolled, the gate releases and admin actions work.
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(Authed(HttpMethod.Get, "/api/users", admin))).StatusCode);
    }
}

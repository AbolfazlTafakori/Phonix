using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Phonix.Api.Tests;

// Boots the real API in-memory (TestServer) against an isolated temp store, and drives it over HTTP.
public class PhonixAppFactory : WebApplicationFactory<Program>
{
    public PhonixAppFactory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "phonix-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable("PHONIX_DATA_FILE", Path.Combine(dir, "store.json"));
        Environment.SetEnvironmentVariable("PHONIX_LOG_DIR", dir);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development");
}

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
}

using Phonix.Api.Security;
using Phonix.Api.Services;
using Xunit;

namespace Phonix.Api.Tests;

// The two pure, security-relevant pieces of the V2Ray panel wiring: who counts as the owner (which gates the
// whole settings section) and which panel URLs are accepted before anything is stored or contacted.
public class OwnerAccountTests : IDisposable
{
    private readonly string? _original = Environment.GetEnvironmentVariable("PHONIX_OWNER_USERNAME");

    public void Dispose() => Environment.SetEnvironmentVariable("PHONIX_OWNER_USERNAME", _original);

    [Fact]
    public void Matches_the_configured_owner_case_insensitively()
    {
        Environment.SetEnvironmentVariable("PHONIX_OWNER_USERNAME", "PayamOwner");
        Assert.True(OwnerAccount.IsOwner("payamowner"));
        Assert.True(OwnerAccount.IsOwner("  PayamOwner "));
        Assert.False(OwnerAccount.IsOwner("someadmin"));
    }

    [Fact]
    public void Nobody_is_owner_when_the_env_var_is_unset()
    {
        // The safe default: an unset owner closes owner-only sections rather than opening them to every admin.
        Environment.SetEnvironmentVariable("PHONIX_OWNER_USERNAME", null);
        Assert.False(OwnerAccount.IsOwner("anyone"));
        Assert.False(OwnerAccount.IsOwner(""));
        Assert.False(OwnerAccount.IsOwner(null));
    }
}

public class V2RayUrlTests
{
    [Theory]
    [InlineData("https://domain.com:8080/webpath", "https://domain.com:8080/webpath")]
    [InlineData("https://sub.domain.com:2053/OiiZNse2", "https://sub.domain.com:2053/OiiZNse2")]
    [InlineData("http://domain.com:54321", "http://domain.com:54321")]
    [InlineData("https://sub.domain.com:8080/path/", "https://sub.domain.com:8080/path")] // trailing slash trimmed
    [InlineData("  https://domain.com:443  ", "https://domain.com:443")]                    // trimmed
    public void Accepts_the_documented_url_shapes(string input, string expected)
    {
        Assert.Equal(expected, IV2RayPanelConnector.NormalizeUrl(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("domain.com:8080")]        // no scheme
    [InlineData("ftp://domain.com")]       // wrong scheme
    [InlineData("javascript:alert(1)")]    // not http(s)
    [InlineData("/panel/only/a/path")]     // not absolute
    public void Rejects_anything_that_is_not_a_usable_http_url(string input)
    {
        Assert.Null(IV2RayPanelConnector.NormalizeUrl(input));
    }
}

using Phonix.Api.Security;
using Xunit;

namespace Phonix.Api.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Verifies_the_correct_password()
    {
        var hash = PasswordHasher.Hash("S3cret!pass");
        Assert.True(PasswordHasher.Verify("S3cret!pass", hash));
    }

    [Fact]
    public void Rejects_a_wrong_password()
    {
        var hash = PasswordHasher.Hash("S3cret!pass");
        Assert.False(PasswordHasher.Verify("wrong", hash));
    }

    [Fact]
    public void Same_password_produces_different_hashes()
    {
        // a unique random salt per hash means equal passwords never share a hash.
        Assert.NotEqual(PasswordHasher.Hash("same"), PasswordHasher.Hash("same"));
    }

    [Fact]
    public void Verify_returns_false_for_malformed_stored_value()
    {
        Assert.False(PasswordHasher.Verify("anything", "not-a-valid-hash"));
    }
}

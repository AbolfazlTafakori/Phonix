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

    // Regression: PBKDF2-HMAC re-hashes a key longer than its block size on every single iteration, so an
    // unbounded password let a pre-auth caller force real CPU cost per login attempt (a multi-megabyte
    // "password" re-hashed 210k times). Verify must reject an oversized password before ever touching
    // Pbkdf2, so the length check itself is the only cost paid.
    //
    // Asserting just False(Verify(huge, hash)) would pass even WITHOUT the guard — a wrong password fails the
    // comparison regardless of why it was wrong, so that alone never proves the short-circuit exists. Timing
    // is the only observable signal: a real 210k-iteration Pbkdf2 call costs real, measurable time, so if the
    // oversized call actually reached Pbkdf2 it would cost comparably to a normal Verify call, not
    // dramatically less.
    [Fact]
    public void Verify_rejects_an_oversized_password_fast_without_running_pbkdf2()
    {
        var hash = PasswordHasher.Hash("S3cret!pass");
        var huge = new string('a', PasswordPolicy.MaxLength + 1);

        var normal = System.Diagnostics.Stopwatch.StartNew();
        PasswordHasher.Verify("some-other-password", hash);
        normal.Stop();

        var oversized = System.Diagnostics.Stopwatch.StartNew();
        var result = PasswordHasher.Verify(huge, hash);
        oversized.Stop();

        Assert.False(result);
        Assert.True(oversized.ElapsedMilliseconds < Math.Max(5, normal.ElapsedMilliseconds / 3),
            $"expected the oversized-password check to short-circuit well before a real Pbkdf2 call " +
            $"(normal verify took {normal.ElapsedMilliseconds}ms, oversized took {oversized.ElapsedMilliseconds}ms)");
    }

    [Fact]
    public void Verify_still_accepts_a_password_at_exactly_the_max_length()
    {
        var pw = new string('a', PasswordPolicy.MaxLength - 1) + "1"; // satisfies letter+digit policy too
        var hash = PasswordHasher.Hash(pw);
        Assert.True(PasswordHasher.Verify(pw, hash));
    }
}

public class PasswordPolicyTests
{
    [Fact]
    public void Rejects_a_password_over_the_max_length()
    {
        var huge = new string('a', PasswordPolicy.MaxLength + 1) + "1";
        Assert.NotNull(PasswordPolicy.Validate(huge));
    }

    [Fact]
    public void Accepts_a_password_at_exactly_the_max_length()
    {
        var pw = new string('a', PasswordPolicy.MaxLength - 1) + "1";
        Assert.Null(PasswordPolicy.Validate(pw));
    }
}

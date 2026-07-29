using Microsoft.Extensions.Caching.Memory;
using Phonix.Api.Security;
using Xunit;

namespace Phonix.Api.Tests;

// RFC 6238 gives a code that is valid for a whole 30-second step, and TotpService accepts one step of skew
// either side — so a bare TOTP check accepts the same code for ~90 seconds, from anyone who has it. And a
// six-digit code with a ±1 window leaves three live values in a million, which a per-IP rate limit does not
// bound at all once an attacker has more than one address. TwoFactorGuard is what closes both.
public class TwoFactorGuardTests
{
    private static TwoFactorGuard NewGuard() =>
        new(new MemoryCache(new MemoryCacheOptions()));

    private static string Secret() => TotpService.GenerateSecret();

    [Fact]
    public void A_correct_code_passes_once_and_is_then_burned()
    {
        var guard = NewGuard();
        var secret = Secret();
        var code = TestTotp.Code(secret);

        Assert.Equal(TwoFactorResult.Ok, guard.Verify(7, secret, code));
        // Still arithmetically valid — its step has not elapsed — but it has already been spent by this
        // account, so a captured code cannot be walked in behind the real user.
        Assert.Equal(TwoFactorResult.Replayed, guard.Verify(7, secret, code));
    }

    [Fact]
    public void Burning_a_step_is_per_account_not_global()
    {
        var guard = NewGuard();
        var secret = Secret();
        var code = TestTotp.Code(secret);

        Assert.Equal(TwoFactorResult.Ok, guard.Verify(7, secret, code));
        // Two people whose authenticators happen to produce the same digits at the same instant must not
        // lock each other out; the claim is keyed by (user, step).
        Assert.Equal(TwoFactorResult.Ok, guard.Verify(8, secret, code));
    }

    [Fact]
    public void Repeated_wrong_codes_lock_the_account_not_just_the_address()
    {
        var guard = NewGuard();
        var secret = Secret();

        for (var i = 0; i < 8; i++)
            Assert.Equal(TwoFactorResult.Invalid, guard.Verify(9, secret, "000000"));

        // Past the threshold the correct code is refused too: the lockout is on the account being attacked,
        // which is the thing a distributed guessing run actually targets.
        Assert.Equal(TwoFactorResult.LockedOut, guard.Verify(9, secret, TestTotp.Code(secret)));
        // ...and only that account.
        Assert.Equal(TwoFactorResult.Ok, guard.Verify(10, secret, TestTotp.Code(secret)));
    }

    [Fact]
    public void A_successful_verification_clears_the_failure_streak()
    {
        var guard = NewGuard();
        var secret = Secret();

        for (var i = 0; i < 5; i++)
            Assert.Equal(TwoFactorResult.Invalid, guard.Verify(11, secret, "000000"));
        Assert.Equal(TwoFactorResult.Ok, guard.Verify(11, secret, TestTotp.Code(secret)));

        // A real person mistyping a few times before getting it right must not be one slip away from a
        // lockout for the rest of the window.
        for (var i = 0; i < 5; i++)
            Assert.Equal(TwoFactorResult.Invalid, guard.Verify(11, secret, "000000"));
        Assert.NotEqual(TwoFactorResult.LockedOut, guard.Verify(11, secret, "000000"));
    }

    [Fact]
    public void A_missing_or_malformed_secret_never_passes()
    {
        var guard = NewGuard();
        Assert.Equal(TwoFactorResult.Invalid, guard.Verify(12, null, "123456"));
        Assert.Equal(TwoFactorResult.Invalid, guard.Verify(12, "", "123456"));
        Assert.Equal(TwoFactorResult.Invalid, guard.Verify(13, Secret(), null));
        Assert.Equal(TwoFactorResult.Invalid, guard.Verify(13, Secret(), "not-a-code"));
    }
}

using Microsoft.Extensions.Caching.Memory;

namespace Phonix.Api.Security;

// Why a verified code isn't the same thing as a verified attempt.
public enum TwoFactorResult
{
    Ok,
    Invalid,
    // The code was arithmetically correct but its time step has already been spent by this account.
    Replayed,
    // Too many wrong codes for this account recently; further attempts are refused regardless of the code.
    LockedOut,
}

// The two things RFC 6238 on its own does not give the second factor:
//
//   1. SINGLE USE. A TOTP code stays valid for its whole 30-second step, and TotpService accepts one step of
//      skew either side, so a code is live for roughly 90 seconds. Anyone who observes it inside that window
//      — over the shoulder, through a phishing relay, from a logged request body — can submit it again.
//      Every accepted step is recorded here for the length of its own validity window, so the second use of
//      the same code is rejected even though the code itself is still "correct".
//
//   2. PER-ACCOUNT LOCKOUT. The per-IP limiter in Program.cs bounds one client, not one account: a code is
//      six digits and the skew window makes three of the million valid at any moment, so an attacker spread
//      across enough addresses can keep guessing at a rate no per-IP ceiling notices. Counting failures
//      against the ACCOUNT closes that, because the account is the thing being attacked.
//
// State is deliberately in-memory (IMemoryCache): both facts are short-lived, and losing them on restart
// costs at most one replay window and one lockout — the same trade the session/IP-ban layers already make.
public interface ITwoFactorGuard
{
    /// <summary>Verifies a code for this user, enforcing single-use per time step and a per-account lockout.</summary>
    TwoFactorResult Verify(int userId, string? secret, string? code);
}

public sealed class TwoFactorGuard : ITwoFactorGuard
{
    // Six digits with a ±1 step window leaves 3 live codes in 10^6. At 8 attempts per 15 minutes an account
    // is guessed with probability ~1 in 40,000 per year of sustained attack — while a real person fat-
    // fingering their code a few times is never locked out.
    private const int MaxFailures = 8;
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    // A step is only worth remembering while it could still be presented again, i.e. its own duration plus
    // the skew window on either side, with a margin. Past that the code is rejected by TotpService anyway.
    private static readonly TimeSpan StepMemory = TimeSpan.FromMinutes(3);

    private readonly IMemoryCache _cache;
    public TwoFactorGuard(IMemoryCache cache) => _cache = cache;

    private static string LockKey(int userId) => $"2fa:lock:{userId}";
    private static string FailKey(int userId) => $"2fa:fail:{userId}";
    private static string StepKey(int userId, long step) => $"2fa:step:{userId}:{step}";

    public TwoFactorResult Verify(int userId, string? secret, string? code)
    {
        if (_cache.TryGetValue(LockKey(userId), out _)) return TwoFactorResult.LockedOut;

        if (string.IsNullOrWhiteSpace(secret) || !TotpService.TryVerify(secret, code ?? "", out var step))
        {
            NoteFailure(userId);
            return TwoFactorResult.Invalid;
        }

        // Claim the step. A concurrent second request carrying the same code loses the race and is told so,
        // rather than both being let through.
        var key = StepKey(userId, step);
        lock (_cache)
        {
            if (_cache.TryGetValue(key, out _))
            {
                // A replay is an attack signal, not a typo, so it counts toward the lockout too.
                NoteFailure(userId);
                return TwoFactorResult.Replayed;
            }
            _cache.Set(key, true, StepMemory);
        }

        // A correct code clears the failure streak: the account demonstrably belongs to whoever just proved it.
        _cache.Remove(FailKey(userId));
        return TwoFactorResult.Ok;
    }

    private void NoteFailure(int userId)
    {
        var key = FailKey(userId);
        var count = (_cache.TryGetValue<int>(key, out var existing) ? existing : 0) + 1;
        // Sliding, so a slow trickle of guesses still accumulates as long as it keeps coming.
        _cache.Set(key, count, new MemoryCacheEntryOptions { SlidingExpiration = FailureWindow });
        if (count >= MaxFailures)
        {
            _cache.Set(LockKey(userId), true, LockoutDuration);
            _cache.Remove(key);
        }
    }
}

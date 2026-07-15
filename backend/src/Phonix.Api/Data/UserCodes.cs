using System.Security.Cryptography;

namespace Phonix.Api.Data;

// Telegram-style numeric account ids: random (so ids leak nothing about signup order or user count),
// starting at 6 digits and growing a digit once the current tier is crowded enough that random picks
// keep colliding. Both stores route user creation through Next() so the format lives in one place.
public static class UserCodes
{
    public const int StartDigits = 6;
    private const int MaxDigits = 12;
    // 24 misses in a row on a tier under ~half full has probability ≈ 0.5^24 — a tier practically never
    // escalates early, yet a genuinely saturated tier escalates after a bounded, cheap number of probes.
    private const int AttemptsPerTier = 24;

    public static string Next(Func<string, bool> isTaken)
    {
        for (var digits = StartDigits; digits <= MaxDigits; digits++)
        {
            var min = Pow10(digits - 1);
            var max = Pow10(digits);
            for (var attempt = 0; attempt < AttemptsPerTier; attempt++)
            {
                var candidate = RandomBetween(min, max).ToString();
                if (!isTaken(candidate)) return candidate;
            }
        }
        // 24 straight collisions on every tier up to 12 digits (a trillion ids) — practically unreachable.
        throw new InvalidOperationException("user-code space exhausted");
    }

    private static long Pow10(int n)
    {
        long v = 1;
        for (var i = 0; i < n; i++) v *= 10;
        return v;
    }

    // Uniform in [min, max) via rejection sampling, so no candidate is likelier than another.
    private static long RandomBetween(long min, long max)
    {
        var range = (ulong)(max - min);
        var limit = ulong.MaxValue - ulong.MaxValue % range;
        while (true)
        {
            var raw = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8));
            if (raw < limit) return min + (long)(raw % range);
        }
    }
}

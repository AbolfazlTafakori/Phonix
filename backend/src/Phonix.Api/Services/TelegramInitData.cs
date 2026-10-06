using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Phonix.Api.Services;

// The launch data Telegram hands a Mini App (Telegram.WebApp.initData): who opened it, signed by Telegram with
// a key only Telegram and the bot's owner have — derived from the bot token. Checking the signature is what
// makes "this request comes from Telegram user N" a fact rather than a claim the browser makes.
//
// https://core.telegram.org/bots/webapps#validating-data-received-via-the-mini-app
public static class TelegramInitData
{
    public sealed record WebAppUser(long Id, string? Username, string? FirstName);

    // How old launch data may be. A Mini App signs in the moment it opens, so this only has to cover that —
    // and the shorter it is, the less a leaked copy is worth.
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(1);
    // Telegram's clock and ours are both NTP-synced; this only absorbs the small difference between them.
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);
    private const int MaxLength = 8 * 1024;

    // The user the data vouches for, or null when it isn't genuine, is stale, or isn't a person.
    public static WebAppUser? Validate(string? initData, string botToken, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(initData) || initData.Length > MaxLength || string.IsNullOrWhiteSpace(botToken))
            return null;

        // Form encoding, as URLSearchParams reads it: '+' is a space. Each key may appear once — a repeated key
        // would let two different values sit behind one signature depending on which one a reader picks.
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in initData.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0) return null;
            string key, value;
            try
            {
                key = Uri.UnescapeDataString(pair[..eq].Replace('+', ' '));
                value = Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
            }
            catch (UriFormatException) { return null; }
            if (!fields.TryAdd(key, value)) return null;
        }

        if (!fields.Remove("hash", out var hash) || hash.Length != 64) return null;
        byte[] expected;
        try { expected = Convert.FromHexString(hash); }
        catch (FormatException) { return null; }

        // Every other field, sorted by key, one "key=value" per line — the exact string Telegram signed.
        var checkString = string.Join('\n', fields.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => $"{f.Key}={f.Value}"));
        var secret = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(botToken.Trim()));
        var actual = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(checkString));
        if (!CryptographicOperations.FixedTimeEquals(actual, expected)) return null;

        if (!fields.TryGetValue("auth_date", out var authDateText) || !long.TryParse(authDateText, out var authDate)) return null;
        var signedAt = DateTimeOffset.FromUnixTimeSeconds(authDate).UtcDateTime;
        if (nowUtc - signedAt > MaxAge || signedAt - nowUtc > ClockSkew) return null;

        if (!fields.TryGetValue("user", out var userJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(userJson);
            var u = doc.RootElement;
            if (!u.TryGetProperty("id", out var idEl) || !idEl.TryGetInt64(out var id) || id <= 0) return null;
            if (u.TryGetProperty("is_bot", out var bot) && bot.ValueKind == JsonValueKind.True) return null;
            return new WebAppUser(id,
                u.TryGetProperty("username", out var un) ? un.GetString() : null,
                u.TryGetProperty("first_name", out var fn) ? fn.GetString() : null);
        }
        catch (JsonException) { return null; }
    }
}

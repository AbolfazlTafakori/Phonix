using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Phonix.Api.Services;
using Xunit;

namespace Phonix.Api.Tests;

// Builds Mini App launch data the way Telegram does: form-encoded fields plus a hash over the sorted
// "key=value" lines, keyed by HMAC("WebAppData", bot token).
internal static class TelegramLaunch
{
    public const string Token = "123456789:AAFakeTokenForTestsOnly_abcdefghijklmnop";

    public static string InitData(long userId, string? username = "ali_tg", string firstName = "Ali",
        DateTime? signedAt = null, string token = Token, bool isBot = false)
    {
        var user = JsonSerializer.Serialize(new { id = userId, is_bot = isBot, first_name = firstName, username });
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_date"] = new DateTimeOffset(signedAt ?? DateTime.UtcNow).ToUnixTimeSeconds().ToString(),
            ["query_id"] = "AAHdF6IQAAAAAN0XohDhrOrc",
            ["user"] = user,
        };
        return Sign(fields, token);
    }

    public static string Sign(IDictionary<string, string> fields, string token = Token)
    {
        var check = string.Join('\n', fields.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => $"{f.Key}={f.Value}"));
        var secret = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(token));
        var hash = Convert.ToHexString(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(check))).ToLowerInvariant();
        // URLSearchParams-style: spaces as '+'.
        static string Enc(string v) => Uri.EscapeDataString(v).Replace("%20", "+");
        return string.Join('&', fields.Select(f => $"{Enc(f.Key)}={Enc(f.Value)}").Append($"hash={hash}"));
    }
}

public class TelegramInitDataTests
{
    private const string Token = TelegramLaunch.Token;

    [Fact]
    public void Genuine_launch_data_names_the_telegram_user()
    {
        var user = TelegramInitData.Validate(TelegramLaunch.InitData(777001, firstName: "Ali Reza"), Token, DateTime.UtcNow);

        Assert.NotNull(user);
        Assert.Equal(777001, user!.Id);
        Assert.Equal("ali_tg", user.Username);
        Assert.Equal("Ali Reza", user.FirstName);   // '+' read back as the space Telegram signed
    }

    [Fact]
    public void Changing_any_field_breaks_the_signature()
    {
        var data = TelegramLaunch.InitData(777001);
        var forged = data.Replace("777001", "777002");

        Assert.Null(TelegramInitData.Validate(forged, Token, DateTime.UtcNow));
    }

    [Fact]
    public void Data_signed_for_another_bot_is_refused()
    {
        var other = TelegramLaunch.InitData(777001, token: "987654321:BBOtherBotTokenForTests_abcdefghijklmno");

        Assert.Null(TelegramInitData.Validate(other, Token, DateTime.UtcNow));
    }

    [Fact]
    public void Stale_or_future_dated_data_is_refused()
    {
        var now = DateTime.UtcNow;
        Assert.Null(TelegramInitData.Validate(TelegramLaunch.InitData(1, signedAt: now.AddHours(-2)), Token, now));
        Assert.Null(TelegramInitData.Validate(TelegramLaunch.InitData(1, signedAt: now.AddHours(1)), Token, now));
        Assert.NotNull(TelegramInitData.Validate(TelegramLaunch.InitData(1, signedAt: now.AddMinutes(-50)), Token, now));
    }

    [Fact]
    public void A_repeated_field_is_refused()
    {
        var data = TelegramLaunch.InitData(777001);

        Assert.Null(TelegramInitData.Validate(data + "&auth_date=1", Token, DateTime.UtcNow));
    }

    [Fact]
    public void Missing_pieces_and_bots_are_refused()
    {
        var now = DateTime.UtcNow;
        Assert.Null(TelegramInitData.Validate(null, Token, now));
        Assert.Null(TelegramInitData.Validate("", Token, now));
        Assert.Null(TelegramInitData.Validate("user=%7B%22id%22%3A1%7D", Token, now));                  // no hash
        Assert.Null(TelegramInitData.Validate(TelegramLaunch.InitData(5, isBot: true), Token, now));
        var noUser = TelegramLaunch.Sign(new Dictionary<string, string>
        {
            ["auth_date"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
        });
        Assert.Null(TelegramInitData.Validate(noUser, Token, now));
    }
}

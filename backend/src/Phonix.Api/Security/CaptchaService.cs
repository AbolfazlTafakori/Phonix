using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Phonix.Api.Security;

public interface ICaptchaService
{
    // Mints a new challenge: returns its id and an SVG image (as a data URI) to show the user.
    (string Id, string Image) Issue();
    // Validates and CONSUMES an answer (single use, case-insensitive). False if missing, wrong, or expired.
    bool Validate(string? id, string? answer);
}

// A self-contained image CAPTCHA: random short codes rendered as a noisy SVG, validated server-side. Keeps
// automated credential-stuffing tools from hammering the login/register endpoints. Challenges live in memory
// only (a lost one just means "get a new image"), so this needs no persistence and survives as a singleton.
public sealed class CaptchaService : ICaptchaService
{
    private const int CodeLength = 5;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    // Visually unambiguous set — no 0/O, 1/l/I — but still a mix of upper, lower and digits.
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
    private static readonly string[] InkColors = { "#1733d6", "#9c0038", "#0f766e", "#6d28d9", "#b45309" };

    private sealed record Challenge(string Code, DateTime ExpiresAt);
    private readonly ConcurrentDictionary<string, Challenge> _challenges = new();

    public (string Id, string Image) Issue()
    {
        PurgeExpired();
        var code = RandomCode();
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        _challenges[id] = new Challenge(code, DateTime.UtcNow + Lifetime);
        return (id, "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(Render(code))));
    }

    public bool Validate(string? id, string? answer)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(answer)) return false;
        if (!_challenges.TryRemove(id, out var ch)) return false; // single use: gone whether right or wrong
        if (ch.ExpiresAt <= DateTime.UtcNow) return false;
        // Case-SENSITIVE on purpose: the answer must match the displayed mix of upper/lower case exactly.
        return string.Equals(ch.Code, answer.Trim(), StringComparison.Ordinal);
    }

    private static string RandomCode()
    {
        var sb = new StringBuilder(CodeLength);
        for (var i = 0; i < CodeLength; i++) sb.Append(Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]);
        return sb.ToString();
    }

    private void PurgeExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var kv in _challenges)
            if (kv.Value.ExpiresAt <= now) _challenges.TryRemove(kv.Key, out _);
    }

    // Hand-rolled SVG so there's no native image dependency: a light plate, a few noise lines, then each
    // glyph at a jittered position, size and rotation in a random ink colour.
    private static string Render(string code)
    {
        int w = 150, h = 50;
        int R() => RandomNumberGenerator.GetInt32(int.MaxValue);
        var sb = new StringBuilder();
        sb.Append($"<svg xmlns='http://www.w3.org/2000/svg' width='{w}' height='{h}' viewBox='0 0 {w} {h}'>");
        sb.Append($"<rect width='{w}' height='{h}' rx='8' fill='#e9e9f2'/>");
        for (var i = 0; i < 5; i++)
        {
            var (x1, y1, x2, y2) = (R() % w, R() % h, R() % w, R() % h);
            sb.Append($"<line x1='{x1}' y1='{y1}' x2='{x2}' y2='{y2}' stroke='{InkColors[i % InkColors.Length]}' stroke-opacity='0.25' stroke-width='1'/>");
        }
        for (var i = 0; i < CodeLength; i++)
        {
            var x = 18 + i * 26 + R() % 6;
            var y = 33 + R() % 8;
            var rot = R() % 40 - 20;
            var size = 26 + R() % 7;
            var color = InkColors[R() % InkColors.Length];
            var ch = System.Security.SecurityElement.Escape(code[i].ToString());
            sb.Append($"<text x='{x}' y='{y}' font-size='{size}' font-family='monospace' font-weight='bold' fill='{color}' transform='rotate({rot} {x} {y})'>{ch}</text>");
        }
        for (var i = 0; i < 18; i++)
            sb.Append($"<circle cx='{R() % w}' cy='{R() % h}' r='1' fill='{InkColors[R() % InkColors.Length]}' fill-opacity='0.3'/>");
        sb.Append("</svg>");
        return sb.ToString();
    }
}

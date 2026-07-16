using System.Text;

namespace Phonix.Api.Security;

public static class InputValidation
{
    // Normalizes Persian and Arabic-Indic digits to ASCII and drops everything else.
    public static string DigitsOnly(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (ch is >= '0' and <= '9') sb.Append(ch);
            else if (ch is >= '۰' and <= '۹') sb.Append((char)('0' + (ch - '۰'))); // ۰-۹
            else if (ch is >= '٠' and <= '٩') sb.Append((char)('0' + (ch - '٠'))); // ٠-٩
        }
        return sb.ToString();
    }

    // Shape check only (something@something.tld, no spaces) — deliverability is proven by the verification
    // mail, not by a regex, so this deliberately stays permissive rather than chasing the RFC.
    public static bool IsEmail(string? raw)
    {
        var value = (raw ?? "").Trim();
        if (value.Length is 0 or > 254 || value.Any(char.IsWhiteSpace)) return false;
        var at = value.IndexOf('@');
        if (at <= 0 || at != value.LastIndexOf('@') || at == value.Length - 1) return false;
        var domain = value[(at + 1)..];
        var dot = domain.LastIndexOf('.');
        return dot > 0 && dot < domain.Length - 1;
    }

    public static bool IsValidCardNumber(string? raw)
    {
        var digits = DigitsOnly(raw);
        return digits.Length == 16 && PassesLuhn(digits);
    }

    public static bool PassesLuhn(string digits)
    {
        var sum = 0;
        var doubleIt = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var d = digits[i] - '0';
            if (doubleIt)
            {
                d *= 2;
                if (d > 9) d -= 9;
            }
            sum += d;
            doubleIt = !doubleIt;
        }
        return sum % 10 == 0;
    }

    // Iranian national-id (کد ملی) checksum: 10 digits, a weighted mod-11 control digit; all-identical
    // sequences (e.g. 0000000000) are rejected even though they would otherwise satisfy the formula.
    public static bool IsValidNationalId(string? raw)
    {
        var d = DigitsOnly(raw);
        if (d.Length != 10) return false;
        if (new string(d[0], 10) == d) return false;

        var sum = 0;
        for (var i = 0; i < 9; i++) sum += (d[i] - '0') * (10 - i);
        var remainder = sum % 11;
        var control = d[9] - '0';
        return remainder < 2 ? control == remainder : control == 11 - remainder;
    }
}

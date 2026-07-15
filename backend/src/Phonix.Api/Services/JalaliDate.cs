using System.Globalization;

namespace Phonix.Api.Services;

// Parsing/validation for the Persian (Jalali) payment dates users enter as "yyyy/MM/dd". Used to enforce
// server-side that a claimed payment date is a real date and never in the future — the client calendar also
// blocks future days, but that check is cosmetic and trivially bypassable, so the server is the real gate.
public static class JalaliDate
{
    private static readonly PersianCalendar Cal = new();

    // Converts a "yyyy/MM/dd" string (Persian or Latin digits) to its Gregorian date, or null if it is not
    // a valid Jalali date.
    public static DateTime? TryParse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        // normalize Persian/Arabic-Indic digits to ASCII so int.Parse works.
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            if (ch is >= '۰' and <= '۹') sb.Append((char)('0' + (ch - '۰')));
            else if (ch is >= '٠' and <= '٩') sb.Append((char)('0' + (ch - '٠')));
            else sb.Append(ch);
        }

        var parts = sb.ToString().Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3) return null;
        if (!int.TryParse(parts[0], out var jy) || !int.TryParse(parts[1], out var jm) || !int.TryParse(parts[2], out var jd))
            return null;
        if (jy < 1300 || jy > 1500 || jm < 1 || jm > 12 || jd < 1 || jd > 31) return null;
        if (jd > Cal.GetDaysInMonth(jy, jm)) return null;

        try { return Cal.ToDateTime(jy, jm, jd, 0, 0, 0, 0); }
        catch (ArgumentException) { return null; }
    }

    // The current Tehran moment as "yyyy/MM/dd HH:mm:ss" in the Jalali calendar (e.g. "1405/04/21 19:15:22").
    // Iran is a fixed UTC+03:30 (no DST since 2022), so a constant offset is exact without a tz database.
    public static string NowStamp()
    {
        var t = DateTime.UtcNow.AddMinutes(210);
        return $"{Cal.GetYear(t):0000}/{Cal.GetMonth(t):00}/{Cal.GetDayOfMonth(t):00} {t:HH:mm:ss}";
    }

    // The current Tehran moment for display to a customer: Persian digits, e.g. "۱۴۰۴/۰۵/۱۲ — ساعت ۱۴:۳۰".
    public static string NowFa()
    {
        var t = DateTime.UtcNow.AddMinutes(210);
        var s = $"{Cal.GetYear(t):0000}/{Cal.GetMonth(t):00}/{Cal.GetDayOfMonth(t):00} — ساعت {t:HH:mm}";
        return ToPersianDigits(s);
    }

    public static string ToPersianDigits(string value) =>
        new(value.Select(ch => char.IsAsciiDigit(ch) ? (char)('۰' + (ch - '0')) : ch).ToArray());

    // True when the string is a valid Jalali date no later than today (Tehran). Empty/invalid input is not
    // "valid" — callers should reject those separately with their own message.
    public static bool IsValidAndNotFuture(string? value)
    {
        var d = TryParse(value);
        if (d is null) return false;
        // one day of slack absorbs timezone differences between the server and the user's local "today".
        return d.Value.Date <= DateTime.UtcNow.Date.AddDays(1);
    }
}

namespace Phonix.Api.Services;

// Turns a raw User-Agent header into something a customer can recognise in a login email ("کروم روی ویندوز").
// Deliberately coarse: this is a human-readable hint next to the IP, not a fingerprint or a security control,
// so an unknown agent degrades to a plain "نامشخص" rather than dumping the raw header at the reader.
public static class UserAgentLabel
{
    // Order matters: Edge and Opera both carry "Chrome" in their UA, and Chrome carries "Safari".
    private static readonly (string token, string label)[] Browsers =
    {
        ("Edg", "اج"),
        ("OPR", "اپرا"),
        ("Firefox", "فایرفاکس"),
        ("Chrome", "کروم"),
        ("Safari", "سافاری"),
    };

    // iPhone/iPad must be checked before Mac: iOS agents also say "Mac OS X".
    private static readonly (string token, string label)[] Platforms =
    {
        ("Windows", "ویندوز"),
        ("Android", "اندروید"),
        ("iPhone", "آیفون"),
        ("iPad", "آیپد"),
        ("Mac", "مک"),
        ("Linux", "لینوکس"),
    };

    public static string From(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return "نامشخص";
        var ua = userAgent!;
        var browser = Browsers.FirstOrDefault(b => ua.Contains(b.token, StringComparison.OrdinalIgnoreCase)).label;
        var platform = Platforms.FirstOrDefault(p => ua.Contains(p.token, StringComparison.OrdinalIgnoreCase)).label;
        return (browser, platform) switch
        {
            (null, null) => "نامشخص",
            (null, _) => platform,
            (_, null) => browser,
            _ => $"{browser} روی {platform}",
        };
    }
}

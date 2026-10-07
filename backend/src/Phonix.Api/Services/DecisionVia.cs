using Phonix.Api.Models;

namespace Phonix.Api.Services;

// How staff are told where a decision was made, in the panel and in the bots alike: «از طریق تلگرام (@ali)»,
// «از طریق سایت (reza)», «به‌صورت خودکار». One wording, so the two sides never contradict each other.
public static class DecisionVia
{
    public const string Telegram = "telegram";
    public const string Site = "site";

    public static string Describe(string? via, string? by)
    {
        var who = string.IsNullOrWhiteSpace(by) ? "" : $" ({by.Trim()})";
        return via switch
        {
            Telegram => "از طریق تلگرام" + who,
            Site => "از طریق سایت" + who,
            _ => "",
        };
    }

    // An order account records one field for this: "telegram", an automatic deliverer's name, or the staff
    // member's username on the site.
    public static string DescribeUnit(string? handledBy) => handledBy switch
    {
        null or "" => "",
        Telegram => "از طریق تلگرام",
        StockFulfillmentService.Actor or "سیستم (V2Ray)" or "سیستم (WireGuard)" => "به‌صورت خودکار",
        _ => $"از طریق سایت ({handledBy})",
    };

    public static string Outcome(TxStatus status) => status == TxStatus.Approved ? "تأیید" : "رد";
    public static string Outcome(BankCardStatus status) => status == BankCardStatus.Approved ? "تأیید" : "رد";
    public static string Outcome(KycStatus status) => status == KycStatus.Approved ? "تأیید" : "رد";

    // «این تراکنش قبلاً از طریق تلگرام (@ali) تأیید شده است.»
    public static string AlreadyDecided(string what, string outcome, string via) =>
        $"این {what} قبلاً {(via.Length > 0 ? via + " " : "")}{outcome} شده است.";

    // Telegram's display name for whoever tapped: their @username, or their first name when they have none.
    public static string? TelegramName(System.Text.Json.JsonElement from)
    {
        if (from.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
        if (from.TryGetProperty("username", out var u) && u.GetString() is { Length: > 0 } username) return "@" + username;
        return from.TryGetProperty("first_name", out var f) && f.GetString() is { Length: > 0 } first ? first : null;
    }
}

using System.Text.Json;

namespace Phonix.Api.Services;

// Asks Telegram who a bot token belongs to (getMe), so a panel never saves a token Telegram doesn't recognise.
// Shared by the bots that are set up on their own panel pages.
public static class TelegramTokenCheck
{
    // The bot's @username, or Telegram's own error in words staff can act on.
    public static async Task<(bool Ok, string? Username, string? Error)> CheckAsync(IHttpClientFactory httpFactory, string token,
        ILogger logger, CancellationToken ct = default)
    {
        token = (token ?? "").Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(token, @"^\d{5,15}:[A-Za-z0-9_-]{30,64}$"))
            return (false, null, "قالب توکن درست نیست. توکن را کامل از BotFather کپی کنید (مثل 123456789:ABC...).");
        try
        {
            using var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(15);
            using var resp = await http.GetAsync($"https://api.telegram.org/bot{token}/getMe", ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            if (resp.IsSuccessStatusCode && doc.RootElement.TryGetProperty("result", out var r)
                && r.TryGetProperty("username", out var u) && u.GetString() is { Length: > 0 } name)
                return (true, name, null);
            var description = doc.RootElement.TryGetProperty("description", out var d) ? d.GetString() : null;
            return (false, null, (int)resp.StatusCode == 401
                ? "تلگرام این توکن را نپذیرفت (Unauthorized). توکن را دوباره از BotFather بگیرید."
                : $"تلگرام خطا داد: {description ?? ((int)resp.StatusCode).ToString()}");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Telegram getMe failed");
            return (false, null, "ارتباط با تلگرام برقرار نشد. کمی بعد دوباره امتحان کنید.");
        }
    }
}

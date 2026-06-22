using System.Collections.Concurrent;
using Phonix.Api.Data;

namespace Phonix.Api.Services;

public interface ITelegramAlertSender
{
    // Sends an operational alert to the configured Telegram chat. No-op unless alerts are enabled
    // and the bot token + chat id are set. Identical messages are throttled to avoid error storms.
    // force = true bypasses the enabled flag + throttle (used by the admin "send test alert" button).
    Task<bool> SendAlertAsync(string text, bool force = false, CancellationToken ct = default);
}

public class TelegramAlertSender : ITelegramAlertSender
{
    private readonly StoreData _store;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<TelegramAlertSender> _logger;

    // Per-message cooldown so a burst of identical 500s sends at most one alert per window.
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, DateTime> _lastSent = new();

    public TelegramAlertSender(StoreData store, IHttpClientFactory httpFactory, ILogger<TelegramAlertSender> logger)
    {
        _store = store;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task<bool> SendAlertAsync(string text, bool force = false, CancellationToken ct = default)
    {
        var settings = _store.GetTelegramSettings();
        if (string.IsNullOrWhiteSpace(settings.BotToken) || string.IsNullOrWhiteSpace(settings.ChatId))
            return false;
        if (!force && !settings.AlertsEnabled)
            return false;

        // Throttle repeats of the same alert text (skipped for explicit test sends).
        var now = DateTime.UtcNow;
        if (!force)
        {
            if (_lastSent.TryGetValue(text, out var last) && now - last < Cooldown)
                return false;
            _lastSent[text] = now;
        }

        try
        {
            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(20);
            var url = $"https://api.telegram.org/bot{settings.BotToken}/sendMessage";
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["chat_id"] = settings.ChatId,
                ["text"] = text,
                ["disable_web_page_preview"] = "true",
            });
            using var resp = await http.PostAsync(url, form, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Telegram alert rejected: {Status}", (int)resp.StatusCode);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            // Alerting must never throw into the caller (it runs from the error path itself).
            _logger.LogWarning(ex, "Telegram alert send failed");
            return false;
        }
    }
}

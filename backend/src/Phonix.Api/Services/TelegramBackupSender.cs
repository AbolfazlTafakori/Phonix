using System.Net.Http.Headers;
using System.Text;
using Phonix.Api.Data;

namespace Phonix.Api.Services;

public interface ITelegramBackupSender
{
    // sends the current full store snapshot as a document to the configured chat. Returns (ok, error).
    Task<(bool ok, string error)> SendAsync(string caption, CancellationToken ct = default);

    // sends a single encrypted section backup to the configured chat.
    Task<(bool ok, string error)> SendSectionAsync(BackupSection section, string caption, CancellationToken ct = default);
}

public class TelegramBackupSender : ITelegramBackupSender
{
    private readonly StoreData _store;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<TelegramBackupSender> _logger;

    public TelegramBackupSender(StoreData store, IHttpClientFactory httpFactory, ILogger<TelegramBackupSender> logger)
    {
        _store = store;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public Task<(bool ok, string error)> SendAsync(string caption, CancellationToken ct = default)
    {
        var json = _store.SerializeSnapshot();
        return SendBackupAsync("کامل", json, caption, ct);
    }

    public Task<(bool ok, string error)> SendSectionAsync(BackupSection section, string caption, CancellationToken ct = default)
    {
        var json = _store.SerializeSection(section);
        return SendBackupAsync(section.ToString(), json, caption, ct);
    }

    // Encrypts the payload and ships it to the SINGLE configured numeric chat id — nothing else. The chat id
    // must be numeric (a Telegram user id, or a group/channel id starting with -); anything else is refused,
    // so a backup can never be delivered to an arbitrary or wrong chat even if the bot is added elsewhere.
    private async Task<(bool ok, string error)> SendBackupAsync(string sectionLabel, string json, string caption, CancellationToken ct)
    {
        var settings = _store.GetTelegramSettings();
        var chatId = (settings.ChatId ?? "").Trim();

        if (string.IsNullOrWhiteSpace(settings.BotToken) || string.IsNullOrWhiteSpace(chatId))
            return Fail(sectionLabel, chatId, "توکن بات یا شناسهٔ چت تنظیم نشده است.");

        if (!IsNumericChatId(chatId))
            return Fail(sectionLabel, chatId, "شناسهٔ چت باید عددی باشد (آیدی عددی کاربر یا گروه).");

        try
        {
            var stamp = DateTime.Now.ToString("yyyy-MM-dd-HHmm");
            var encrypted = BackupCrypto.IsEnabled;
            var payload = encrypted ? BackupCrypto.Encrypt(json) : json;
            var bytes = Encoding.UTF8.GetBytes(payload);
            var ext = encrypted ? "phxbak" : "json";
            var fileName = $"phonix-{sectionLabel.ToLowerInvariant()}-{stamp}.{ext}";

            using var form = new MultipartFormDataContent { { new StringContent(chatId), "chat_id" } };
            if (!string.IsNullOrWhiteSpace(caption)) form.Add(new StringContent(caption), "caption");
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue(encrypted ? "application/octet-stream" : "application/json");
            form.Add(file, "document", fileName);

            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(60);
            using var resp = await http.PostAsync($"https://api.telegram.org/bot{settings.BotToken}/sendDocument", form, ct);

            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogError("Telegram backup failed: {Status} {Body}", (int)resp.StatusCode, body);
                return Fail(sectionLabel, chatId, $"تلگرام درخواست را رد کرد (کد {(int)resp.StatusCode}).");
            }

            _store.RecordTelegramBackup(true, "");
            _store.RecordBackup(sectionLabel, $"تلگرام ({chatId})", true, "");
            _logger.LogInformation("Telegram backup '{Section}' sent to chat {ChatId}", sectionLabel, chatId);
            return (true, "");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram backup send failed");
            return Fail(sectionLabel, chatId, "ارسال به تلگرام ناموفق بود (جزئیات در لاگ سرور).");
        }
    }

    private (bool ok, string error) Fail(string sectionLabel, string chatId, string error)
    {
        _store.RecordTelegramBackup(false, error);
        _store.RecordBackup(sectionLabel, string.IsNullOrWhiteSpace(chatId) ? "تلگرام" : $"تلگرام ({chatId})", false, error);
        return (false, error);
    }

    // digits only, optionally a single leading '-' (group/channel ids are negative).
    private static bool IsNumericChatId(string id) =>
        System.Text.RegularExpressions.Regex.IsMatch(id, @"^-?\d{1,32}$");
}

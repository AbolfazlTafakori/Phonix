using System.Net.Http.Headers;
using System.Text;
using Phonix.Api.Data;

namespace Phonix.Api.Services;

public interface ITelegramBackupSender
{
    // sends the current store snapshot as a document to the configured chat. Returns (ok, error).
    Task<(bool ok, string error)> SendAsync(string caption, CancellationToken ct = default);
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

    public async Task<(bool ok, string error)> SendAsync(string caption, CancellationToken ct = default)
    {
        var settings = _store.GetTelegramSettings();
        if (string.IsNullOrWhiteSpace(settings.BotToken) || string.IsNullOrWhiteSpace(settings.ChatId))
        {
            const string err = "توکن بات یا شناسه چت تنظیم نشده است.";
            _store.RecordTelegramBackup(false, err);
            return (false, err);
        }

        try
        {
            var json = _store.SerializeSnapshot();
            var bytes = Encoding.UTF8.GetBytes(json);
            var fileName = $"phonix-backup-{DateTime.Now:yyyy-MM-dd-HHmm}.json";

            using var form = new MultipartFormDataContent
            {
                { new StringContent(settings.ChatId), "chat_id" },
            };
            if (!string.IsNullOrWhiteSpace(caption)) form.Add(new StringContent(caption), "caption");

            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            form.Add(file, "document", fileName);

            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(60);
            var url = $"https://api.telegram.org/bot{settings.BotToken}/sendDocument";
            using var resp = await http.PostAsync(url, form, ct);
            var respBody = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogError("Telegram backup failed: {Status} {Body}", (int)resp.StatusCode, respBody);
                var err = $"تلگرام درخواست را رد کرد (کد {(int)resp.StatusCode}). توکن و شناسه چت را بررسی کنید.";
                _store.RecordTelegramBackup(false, err);
                return (false, err);
            }

            _store.RecordTelegramBackup(true, "");
            _logger.LogInformation("Telegram backup sent to chat {ChatId}", settings.ChatId);
            return (true, "");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram backup send failed");
            _store.RecordTelegramBackup(false, ex.Message);
            return (false, "ارسال به تلگرام ناموفق بود (جزئیات در لاگ سرور).");
        }
    }
}

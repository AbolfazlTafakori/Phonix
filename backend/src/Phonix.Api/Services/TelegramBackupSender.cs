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

    // sends an uploaded-files archive to the configured chat. sensitive=false → public site media (product
    // images, banners) as a plain zip; sensitive=true → users' documents (cards, KYC, receipts) encrypted.
    // Large archives are split into parts under Telegram's per-file limit and sent as several documents.
    Task<(bool ok, string error)> SendMediaAsync(bool sensitive, string caption, CancellationToken ct = default);
}

public class TelegramBackupSender : ITelegramBackupSender
{
    // Telegram's Bot API caps a document at 50 MB; we split a few MB under that to leave room for the
    // multipart envelope, so an archive of any total size still goes through as multiple parts.
    private const int MaxPartBytes = 45 * 1024 * 1024;

    private readonly StoreData _store;
    private readonly IFileStorageService _files;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<TelegramBackupSender> _logger;

    public TelegramBackupSender(StoreData store, IFileStorageService files, IHttpClientFactory httpFactory, ILogger<TelegramBackupSender> logger)
    {
        _store = store;
        _files = files;
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

            var (ok, status) = await SendDocumentAsync(settings.BotToken!, chatId,
                bytes, fileName, encrypted ? "application/octet-stream" : "application/json", caption, ct);
            if (!ok)
            {
                _logger.LogError("Telegram backup failed: {Status}", status);
                return Fail(sectionLabel, chatId, $"تلگرام درخواست را رد کرد (کد {status}).");
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

    public async Task<(bool ok, string error)> SendMediaAsync(bool sensitive, string caption, CancellationToken ct = default)
    {
        var label = sensitive ? "مدارک کاربران" : "رسانهٔ سایت";
        var settings = _store.GetTelegramSettings();
        var chatId = (settings.ChatId ?? "").Trim();

        if (string.IsNullOrWhiteSpace(settings.BotToken) || string.IsNullOrWhiteSpace(chatId))
            return Fail(label, chatId, "توکن بات یا شناسهٔ چت تنظیم نشده است.");
        if (!IsNumericChatId(chatId))
            return Fail(label, chatId, "شناسهٔ چت باید عددی باشد (آیدی عددی کاربر یا گروه).");
        // Never ship users' documents in the clear: require the backup key for the sensitive archive.
        if (sensitive && !BackupCrypto.IsEnabled)
            return Fail(label, chatId, "برای ارسال مدارک حساس، کلید پشتیبان (PHONIX_BACKUP_KEY) باید تنظیم شده باشد.");

        try
        {
            var stamp = DateTime.Now.ToString("yyyy-MM-dd-HHmm");
            var raw = sensitive ? _files.ArchiveSensitiveMedia() : _files.ArchivePublicMedia();
            // The public archive travels as a plain zip (those images are served openly anyway); the
            // sensitive archive is always encrypted to the offline key.
            var bytes = sensitive ? BackupCrypto.EncryptBytes(raw) : raw;
            var ext = sensitive ? "phxbak" : "zip";
            var kind = sensitive ? "documents" : "site";

            // Split into parts so any total size goes through under Telegram's per-file cap.
            var parts = Math.Max(1, (bytes.Length + MaxPartBytes - 1) / MaxPartBytes);
            for (var i = 0; i < parts; i++)
            {
                var offset = i * MaxPartBytes;
                var len = Math.Min(MaxPartBytes, bytes.Length - offset);
                var chunk = new byte[len];
                Array.Copy(bytes, offset, chunk, 0, len);

                var partCaption = parts > 1 ? $"{caption} — بخش {i + 1}/{parts}" : caption;
                var fileName = parts > 1
                    ? $"phonix-media-{kind}-{stamp}.part{i + 1:00}of{parts:00}.{ext}"
                    : $"phonix-media-{kind}-{stamp}.{ext}";

                var (ok, status) = await SendDocumentAsync(settings.BotToken!, chatId,
                    chunk, fileName, "application/octet-stream", partCaption, ct);
                if (!ok)
                {
                    _logger.LogError("Telegram media backup failed at part {Part}/{Parts}: {Status}", i + 1, parts, status);
                    return Fail(label, chatId, $"ارسال بخش {i + 1} از {parts} ناموفق بود (کد {status}).");
                }
            }

            _store.RecordTelegramBackup(true, "");
            _store.RecordBackup(label, $"تلگرام ({chatId})", true, parts > 1 ? $"{parts} بخش" : "");
            _logger.LogInformation("Telegram media backup '{Label}' sent to {ChatId} in {Parts} part(s)", label, chatId, parts);
            return (true, "");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram media backup send failed");
            return Fail(label, chatId, "ارسال رسانه به تلگرام ناموفق بود (جزئیات در لاگ سرور).");
        }
    }

    // Low-level single sendDocument call. Returns (ok, statusCodeOrError) so callers can log/report.
    private async Task<(bool ok, string status)> SendDocumentAsync(
        string token, string chatId, byte[] bytes, string fileName, string contentType, string caption, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent { { new StringContent(chatId), "chat_id" } };
        if (!string.IsNullOrWhiteSpace(caption)) form.Add(new StringContent(caption), "caption");
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "document", fileName);

        using var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromMinutes(5); // a 45 MB part can take a while on a modest uplink
        using var resp = await http.PostAsync($"https://api.telegram.org/bot{token}/sendDocument", form, ct);
        if (resp.IsSuccessStatusCode) return (true, "200");
        var body = await resp.Content.ReadAsStringAsync(ct);
        _logger.LogError("Telegram sendDocument failed: {Status} {Body}", (int)resp.StatusCode, body);
        return (false, ((int)resp.StatusCode).ToString());
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

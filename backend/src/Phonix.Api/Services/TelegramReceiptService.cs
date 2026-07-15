using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Phonix.Api.Data;
using Phonix.Api.Models;

namespace Phonix.Api.Services;

// Deposit-receipt review over Telegram. Two halves:
//   • NotifyDepositAsync — when a customer files a top-up, push its receipt + details to the admin chat with
//     inline «تأیید / رد» buttons.
//   • ProcessUpdatesAsync — long-poll getUpdates for the admin's button taps and apply each decision back to
//     the transaction via the SAME store path the panel uses (SetTransactionStatus with via="telegram"), so
//     the money movement, order advancement and user notification are identical to an in-panel approval.
//
// Security: a decision is only honoured when it comes from the single configured ChatId (the admin's own
// chat / group), and the store transition is idempotent, so a replayed or stale tap can never double-apply.
public interface ITelegramReceiptService
{
    // Fire-and-forget from the request path: never throws. No-op unless the receipt bot is enabled and
    // the bot token + numeric chat id are configured.
    Task NotifyDepositAsync(Transaction tx, CancellationToken ct = default);

    // Long-polls one getUpdates cycle starting at `offset` and applies any admin decisions. Returns the next
    // offset to poll from (the highest handled update_id + 1, or `offset` when nothing advanced).
    Task<long> ProcessUpdatesAsync(long offset, CancellationToken ct = default);
}

public sealed class TelegramReceiptService : ITelegramReceiptService
{
    private const string ApprovePrefix = "rcpt:ok:";
    private const string RejectPrefix = "rcpt:no:";

    private readonly IDataStore _store;
    private readonly IFileStorageService _files;
    private readonly IUserMailer _mailer;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<TelegramReceiptService> _logger;

    public TelegramReceiptService(IDataStore store, IFileStorageService files, IUserMailer mailer,
        IHttpClientFactory httpFactory, ILogger<TelegramReceiptService> logger)
    {
        _store = store;
        _files = files;
        _mailer = mailer;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    // The receipt bot's OWN token + chat, independent of the backup/alerts bot.
    private (string token, string chatId)? ActiveConfig()
    {
        var s = _store.GetTelegramSettings();
        if (!s.ReceiptBotEnabled) return null;
        var token = (s.ReceiptBotToken ?? "").Trim();
        var chatId = (s.ReceiptChatId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(token) || !IsNumericChatId(chatId)) return null;
        return (token, chatId);
    }

    public async Task NotifyDepositAsync(Transaction tx, CancellationToken ct = default)
    {
        try
        {
            if (ActiveConfig() is not { } cfg) return;
            var (token, chatId) = cfg;

            var caption = BuildCaption(tx);
            var markup = JsonSerializer.Serialize(new
            {
                inline_keyboard = new[]
                {
                    new object[]
                    {
                        new { text = "✅ تأیید", callback_data = ApprovePrefix + tx.Id },
                        new { text = "❌ رد", callback_data = RejectPrefix + tx.Id },
                    },
                },
            });

            // Prefer sending the receipt image itself; fall back to a text message when there is no receipt.
            var receipt = OpenReceipt(tx.ReceiptUrl);
            if (receipt is not null)
            {
                await using (receipt.Content)
                    await SendPhotoAsync(token, chatId, receipt, caption, markup, ct);
            }
            else
            {
                await SendMessageAsync(token, chatId, caption, markup, ct);
            }
        }
        catch (Exception ex)
        {
            // The notification is best-effort: filing the deposit must never fail because Telegram is down.
            _logger.LogWarning(ex, "Telegram receipt notification failed for tx #{TxId}", tx.Id);
        }
    }

    public async Task<long> ProcessUpdatesAsync(long offset, CancellationToken ct = default)
    {
        if (ActiveConfig() is not { } cfg) return offset;
        var (token, _) = cfg;

        var url = $"https://api.telegram.org/bot{token}/getUpdates"
                + $"?offset={offset}&timeout=25&allowed_updates=%5B%22callback_query%22%5D";

        using var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(35); // longer than the 25s long-poll so the poll itself never times out

        using var resp = await http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("Telegram getUpdates failed: {Status}", (int)resp.StatusCode);
            return offset;
        }

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("result", out var results) || results.ValueKind != JsonValueKind.Array)
            return offset;

        var next = offset;
        foreach (var update in results.EnumerateArray())
        {
            if (update.TryGetProperty("update_id", out var uid) && uid.TryGetInt64(out var id) && id >= next)
                next = id + 1; // acknowledge every update we see, even ones we don't act on

            if (update.TryGetProperty("callback_query", out var cq))
                await HandleCallbackAsync(token, cq, ct);
        }
        return next;
    }

    private async Task HandleCallbackAsync(string token, JsonElement cq, CancellationToken ct)
    {
        var callbackId = cq.TryGetProperty("id", out var cid) ? cid.GetString() ?? "" : "";
        var data = cq.TryGetProperty("data", out var d) ? d.GetString() ?? "" : "";
        var fromId = cq.TryGetProperty("from", out var from) && from.TryGetProperty("id", out var fid)
            ? fid.GetRawText() : "";
        long? chatId = null;
        int? messageId = null;
        if (cq.TryGetProperty("message", out var msg))
        {
            if (msg.TryGetProperty("chat", out var chat) && chat.TryGetProperty("id", out var chId) && chId.TryGetInt64(out var c))
                chatId = c;
            if (msg.TryGetProperty("message_id", out var mId) && mId.TryGetInt32(out var m))
                messageId = m;
        }

        // Authorization: only the single configured receipt chat may decide. Accept when either the acting
        // user or the hosting chat matches the configured ReceiptChatId (equal for a private admin chat).
        var configuredChat = (_store.GetTelegramSettings().ReceiptChatId ?? "").Trim();
        var allowed = fromId == configuredChat || (chatId?.ToString() ?? "") == configuredChat;
        if (!allowed)
        {
            _logger.LogWarning("Rejected Telegram receipt decision from unauthorized source (from={From}, chat={Chat})", fromId, chatId);
            await AnswerCallbackAsync(token, callbackId, "شما مجاز به این عملیات نیستید.", ct);
            return;
        }

        var (action, txId) = ParseCallback(data);
        if (action is null || txId is null)
        {
            await AnswerCallbackAsync(token, callbackId, "", ct);
            return;
        }

        var tx = _store.GetTransaction(txId.Value);
        if (tx is null)
        {
            await AnswerCallbackAsync(token, callbackId, "تراکنش یافت نشد.", ct);
            return;
        }
        if (tx.Status != TxStatus.Pending)
        {
            await AnswerCallbackAsync(token, callbackId, $"این تراکنش قبلاً {StatusFa(tx.Status)} شده است.", ct);
            if (chatId is not null && messageId is not null)
                await EditDecidedAsync(token, chatId.Value, messageId.Value, tx, ct);
            return;
        }

        var approve = action == "ok";
        // No note either way: the decision channel is already recorded in ApprovedVia, and Note is the
        // rejection REASON — it reaches the customer in their rejection email, so an internal marker like
        // "رد از طریق تلگرام" must never land in it. A one-tap reject simply carries no reason.
        var ok = _store.SetTransactionStatus(txId.Value, approve ? TxStatus.Approved : TxStatus.Rejected, "telegram", null);
        if (!ok)
        {
            await AnswerCallbackAsync(token, callbackId, "اعمال تغییر ناموفق بود.", ct);
            return;
        }

        _logger.LogInformation("Telegram receipt decision: tx #{TxId} → {Status} by chat {Chat}",
            txId.Value, approve ? "Approved" : "Rejected", configuredChat);

        var updated = _store.GetTransaction(txId.Value) ?? tx;
        // Same customer email an in-panel decision sends (the Pending guard above keeps it to one).
        _ = _mailer.TransactionDecidedAsync(updated);
        await AnswerCallbackAsync(token, callbackId, approve ? "✅ تأیید شد." : "❌ رد شد.", ct);
        if (chatId is not null && messageId is not null)
            await EditDecidedAsync(token, chatId.Value, messageId.Value, updated, ct);
    }

    private static (string? action, int? txId) ParseCallback(string data)
    {
        if (data.StartsWith(ApprovePrefix, StringComparison.Ordinal) && int.TryParse(data[ApprovePrefix.Length..], out var a))
            return ("ok", a);
        if (data.StartsWith(RejectPrefix, StringComparison.Ordinal) && int.TryParse(data[RejectPrefix.Length..], out var r))
            return ("no", r);
        return (null, null);
    }

    // Rich receipt caption as Telegram HTML: each section is a <blockquote> (the red-bar quote style) with
    // its money line bold, matching the admin's reference layout. An order purchase (OrderPayment) also
    // carries the service block resolved from the linked order; a wallet top-up omits it. Every dynamic
    // value is HTML-escaped — names, notes and card holders are customer input.
    private string BuildCaption(Transaction tx)
    {
        var isOrder = tx.Type == TxTypes.OrderPayment;
        var user = tx.UserId > 0 ? _store.GetUser(tx.UserId) : null;
        var sb = new StringBuilder();

        sb.AppendLine(isOrder ? "❗️|💳 خرید جدید ( کارت به کارت )" : "❗️|💳 واریز جدید ( کارت به کارت )");
        sb.AppendLine();

        sb.AppendLine("<blockquote>مشخصات کاربر");
        sb.AppendLine($"▫️آیدی کاربر: {Esc(user?.Code is { Length: > 0 } code ? code : tx.UserId.ToString())}");
        sb.AppendLine($"👨‍💼اسم کاربر: {Esc(Dash(user?.Name ?? tx.UserName))}");
        sb.AppendLine($"⚡️ نام کاربری: {Esc(Dash(user?.Username))}");
        sb.AppendLine($"📞 شماره تماس: {Esc(Dash(user?.Phone))}");
        sb.AppendLine();
        sb.AppendLine($"<b>💳 موجودی کاربر: {Money(user?.Wallet ?? 0)} تومان</b></blockquote>");
        sb.AppendLine();

        var serviceLines = new StringBuilder();
        if (isOrder && !string.IsNullOrWhiteSpace(tx.OrderCode)
            && _store.GetUserOrders(tx.UserId).FirstOrDefault(o => o.Code == tx.OrderCode) is { Items.Count: > 0 } order)
        {
            serviceLines.AppendLine("مشخصات سرویس");
            // Every purchased service is listed as its own block, separated by a blank line, so a multi-item
            // order (e.g. two Netflix accounts) shows each line item rather than only the first.
            foreach (var item in order.Items)
            {
                var category = _store.GetProduct(item.ProductId) is { } p ? _store.GetCategory(p.CategoryId)?.Name : null;
                var (planType, planDuration) = SplitPlan(item);
                serviceLines.AppendLine();
                serviceLines.AppendLine($"🚦دسته‌بندی: {Esc(Dash(category))}");
                serviceLines.AppendLine($"✏️ نام سرویس: {Esc(Dash(item.Name))}");
                serviceLines.AppendLine($"🔋نوع سرویس: {Esc(Dash(planType))}");
                serviceLines.AppendLine($"⏰ مدت سرویس: {Esc(Dash(planDuration))}");
            }
            serviceLines.AppendLine();
        }
        // The amount closes the service block when there is one (the reference layout), and stands in its own
        // quote block for a plain wallet top-up.
        sb.AppendLine($"<blockquote>{serviceLines}<b>💰مبلغ پرداختی: {Money(Math.Abs(tx.Amount))} تومان</b></blockquote>");
        sb.AppendLine();

        sb.AppendLine("<blockquote>اطلاعات واریزی");
        if (!string.IsNullOrWhiteSpace(tx.SourceCard)) sb.AppendLine($"شماره کارت مبدأ: {FormatCard(tx.SourceCard)}");
        if (!string.IsNullOrWhiteSpace(tx.SourceHolder)) sb.AppendLine($"👤 نگهدارنده کارت مبدأ: {Esc(tx.SourceHolder!)}");
        sb.AppendLine(); // blank line between the source-card group and the destination-card group
        if (!string.IsNullOrWhiteSpace(tx.DestinationCard)) sb.AppendLine($"شماره کارت مقصد: {FormatCard(tx.DestinationCard)}");
        if (!string.IsNullOrWhiteSpace(tx.DestinationHolder)) sb.AppendLine($"👤 نگهدارنده کارت مقصد: {Esc(tx.DestinationHolder!)}");
        if (!string.IsNullOrWhiteSpace(tx.TrackingNumber)) sb.AppendLine($"🔗 شماره پیگیری: {Esc(tx.TrackingNumber!)}");
        sb.AppendLine("</blockquote>");

        sb.Append(JalaliDate.NowStamp());
        return sb.ToString();
    }

    private static string Dash(string? v) => string.IsNullOrWhiteSpace(v) ? "-" : v.Trim();

    private static string Esc(string v) => System.Net.WebUtility.HtmlEncode(v);

    // 16-digit cards read as "6037-9912-3456-7890"; anything else (crypto address, IBAN) passes through.
    private static string FormatCard(string? card)
    {
        var raw = (card ?? "").Trim();
        var digits = new string(raw.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length != 16) return Esc(raw);
        return $"{digits[..4]}-{digits[4..8]}-{digits[8..12]}-{digits[12..]}";
    }

    // 3-3 grouped from the right with commas, e.g. 490000 → "490,000".
    private static string Money(long toman) => toman.ToString("N0", CultureInfo.InvariantCulture);

    // OrderItem.Plan is built as "{Type} · {Months} ماهه"; fall back to PlanMonths for the duration.
    private static (string? type, string? duration) SplitPlan(OrderItem item)
    {
        string? type = null;
        var duration = item.PlanMonths is int m ? $"{m} ماهه" : null;
        if (!string.IsNullOrWhiteSpace(item.Plan))
        {
            var parts = item.Plan.Split('·');
            type = parts[0].Trim();
            if (parts.Length > 1) duration = parts[1].Trim();
        }
        return (type, duration);
    }

    private StoredFile? OpenReceipt(string? receiptUrl)
    {
        if (string.IsNullOrWhiteSpace(receiptUrl)) return null;
        // Receipt values are opaque storage ids (owner-prefixed); the file service validates the shape and
        // keeps the read inside the receipts folder, so a crafted value can never escape it.
        var id = receiptUrl.Trim();
        var slash = id.LastIndexOf('/');
        if (slash >= 0) id = id[(slash + 1)..];
        try { return _files.Open("receipts", id); }
        catch { return null; }
    }

    // ── Low-level Telegram calls ──────────────────────────────────────────────────────────────────────────

    private async Task SendPhotoAsync(string token, string chatId, StoredFile receipt, string caption, string markup, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await receipt.Content.CopyToAsync(ms, ct);
        using var form = new MultipartFormDataContent
        {
            { new StringContent(chatId), "chat_id" },
            { new StringContent(caption), "caption" },
            { new StringContent("HTML"), "parse_mode" },
            { new StringContent(markup), "reply_markup" },
        };
        var photo = new ByteArrayContent(ms.ToArray());
        photo.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(receipt.ContentType) ? "image/jpeg" : receipt.ContentType);
        form.Add(photo, "photo", "receipt.jpg");

        using var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);
        using var resp = await http.PostAsync($"https://api.telegram.org/bot{token}/sendPhoto", form, ct);
        if (!resp.IsSuccessStatusCode)
            _logger.LogWarning("Telegram sendPhoto failed: {Status}", (int)resp.StatusCode);
    }

    private async Task SendMessageAsync(string token, string chatId, string text, string markup, CancellationToken ct) =>
        await PostFormAsync(token, "sendMessage", new Dictionary<string, string>
        {
            ["chat_id"] = chatId,
            ["text"] = text,
            ["parse_mode"] = "HTML",
            ["reply_markup"] = markup,
            ["disable_web_page_preview"] = "true",
        }, ct);

    private async Task AnswerCallbackAsync(string token, string callbackId, string text, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(callbackId)) return;
        var fields = new Dictionary<string, string> { ["callback_query_id"] = callbackId };
        if (!string.IsNullOrEmpty(text)) fields["text"] = text;
        await PostFormAsync(token, "answerCallbackQuery", fields, ct);
    }

    // Rewrites the reviewed message to show the outcome and drops the inline buttons so it can't be tapped again.
    private async Task EditDecidedAsync(string token, long chatId, int messageId, Transaction tx, CancellationToken ct)
    {
        var outcome = tx.Status == TxStatus.Approved ? "✅ تأیید شد" : "❌ رد شد";
        var caption = $"{BuildCaption(tx)}\n\n<b>وضعیت: {outcome} (از طریق تلگرام)</b>";
        // The message carries a photo, so the caption is what we edit; empty inline_keyboard removes the buttons.
        await PostFormAsync(token, "editMessageCaption", new Dictionary<string, string>
        {
            ["chat_id"] = chatId.ToString(),
            ["message_id"] = messageId.ToString(),
            ["caption"] = caption,
            ["parse_mode"] = "HTML",
            ["reply_markup"] = "{\"inline_keyboard\":[]}",
        }, ct);
    }

    private async Task PostFormAsync(string token, string method, Dictionary<string, string> fields, CancellationToken ct)
    {
        try
        {
            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(20);
            using var form = new FormUrlEncodedContent(fields);
            using var resp = await http.PostAsync($"https://api.telegram.org/bot{token}/{method}", form, ct);
            if (!resp.IsSuccessStatusCode)
                _logger.LogWarning("Telegram {Method} failed: {Status}", method, (int)resp.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram {Method} call failed", method);
        }
    }

    private static string StatusFa(TxStatus s) => s switch
    {
        TxStatus.Approved => "تأیید",
        TxStatus.Rejected => "رد",
        _ => "بررسی",
    };

    // digits only, optionally a single leading '-' (group/channel ids are negative).
    private static bool IsNumericChatId(string id) =>
        System.Text.RegularExpressions.Regex.IsMatch(id, @"^-?\d{1,32}$");
}

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

    // Sends a test message with the saved settings and returns Telegram's own error on failure. Every real
    // send is fire-and-forget and only logs, so without this a misconfiguration is invisible: the bot just
    // silently never posts. This is the one place that reports why.
    Task<(bool ok, string? error)> SendTestAsync(CancellationToken ct = default);
}

public sealed class TelegramReceiptService : ITelegramReceiptService
{
    private const string ApprovePrefix = "rcpt:ok:";
    private const string RejectPrefix = "rcpt:no:";
    // The static post-decision button; not an actionable prefix, so a tap is a harmless no-op.
    private const string DecidedPrefix = "rcpt:done:";

    private readonly IDataStore _store;
    private readonly IFileStorageService _files;
    private readonly IUserMailer _mailer;
    private readonly ITelegramOrderService _orderBot;
    private readonly IStockFulfillmentService _stock;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<TelegramReceiptService> _logger;

    public TelegramReceiptService(IDataStore store, IFileStorageService files, IUserMailer mailer,
        ITelegramOrderService orderBot, IStockFulfillmentService stock, IHttpClientFactory httpFactory,
        ILogger<TelegramReceiptService> logger)
    {
        _store = store;
        _files = files;
        _mailer = mailer;
        _orderBot = orderBot;
        _stock = stock;
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

    public async Task<(bool ok, string? error)> SendTestAsync(CancellationToken ct = default)
    {
        var s = _store.GetTelegramSettings();
        // Report the exact reason ActiveConfig() would have refused, instead of silently doing nothing.
        if (!s.ReceiptBotEnabled) return (false, "ربات تأیید رسید خاموش است.");
        var token = (s.ReceiptBotToken ?? "").Trim();
        var chatId = (s.ReceiptChatId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(token)) return (false, "توکن بات رسید وارد نشده است.");
        if (!IsNumericChatId(chatId))
            return (false, $"شناسهٔ چت «{chatId}» عددی نیست. باید عدد باشد (گروه/کانال با منفی شروع می‌شود، مثل ‎-1001234567890).");

        return await PostAndReportAsync(token, "sendMessage", new Dictionary<string, string>
        {
            ["chat_id"] = chatId,
            ["text"] = "✅ پیام تست ربات رسید فونیکس. اگر این پیام را می‌بینید، تنظیمات درست است.",
        }, ct);
    }

    public async Task<long> ProcessUpdatesAsync(long offset, CancellationToken ct = default)
    {
        if (ActiveConfig() is not { } cfg) return offset;
        var (token, _) = cfg;

        // Both the button taps (callback_query) and the admin's typed rejection reason (message) are needed;
        // the reason arrives as a reply to the bot's own prompt, so it reaches us even under group privacy mode.
        var url = $"https://api.telegram.org/bot{token}/getUpdates"
                + $"?offset={offset}&timeout=25&allowed_updates=%5B%22callback_query%22%2C%22message%22%5D";

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
            else if (update.TryGetProperty("message", out var m))
                await HandleReasonReplyAsync(token, m, ct);
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

        // Reject is a TWO-STEP flow: a tap only asks for the reason. The actual rejection happens in
        // HandleReasonReplyAsync once the admin replies with the reason text, so a rejection always carries
        // one — it becomes tx.Note and reaches the customer in their rejection email.
        if (action == "no")
        {
            if (chatId is not null && messageId is not null)
                await SendReasonPromptAsync(token, chatId.Value, messageId.Value, txId.Value, ct);
            await AnswerCallbackAsync(token, callbackId, "✍️ دلیل رد را در «پاسخ» به پیام ارسال کنید.", ct);
            return;
        }

        // Approve is one tap. No note: the decision channel is recorded in ApprovedVia, and Note is reserved
        // for the rejection reason, so an internal marker must never land in it.
        var ok = _store.SetTransactionStatus(txId.Value, TxStatus.Approved, "telegram", null);
        if (!ok)
        {
            await AnswerCallbackAsync(token, callbackId, "اعمال تغییر ناموفق بود.", ct);
            return;
        }

        _logger.LogInformation("Telegram receipt decision: tx #{TxId} → Approved by chat {Chat}", txId.Value, configuredChat);

        var updated = _store.GetTransaction(txId.Value) ?? tx;
        // Same customer email an in-panel decision sends (the Pending guard above keeps it to one).
        _ = _mailer.TransactionDecidedAsync(updated);
        // Approving here also advanced the order into fulfillment: the pool delivers what it can right away,
        // and only the accounts left over go to the orders group.
        _stock.AutoDeliverForTransaction(updated);
        _ = _orderBot.AnnounceApprovedOrderAsync(updated, ct);
        await AnswerCallbackAsync(token, callbackId, "✅ تأیید شد.", ct);
        if (chatId is not null && messageId is not null)
            await EditDecidedAsync(token, chatId.Value, messageId.Value, updated, ct);
    }

    // Handles the admin's typed rejection reason: it arrives as a reply to the bot's reason prompt, whose text
    // carries a «#REJ:<txId>:<receiptMessageId>» marker. We reject the transaction with the reply as its note,
    // email the customer the reason, and rewrite the original receipt to the resolved state (reason included).
    private async Task HandleReasonReplyAsync(string token, JsonElement msg, CancellationToken ct)
    {
        if (!msg.TryGetProperty("reply_to_message", out var replied)) return;
        var promptText = replied.TryGetProperty("text", out var pt) ? pt.GetString() ?? "" : "";
        if (ParseReasonMarker(promptText) is not { } marker) return; // not a reply to our reason prompt
        var (txId, receiptMsgId) = marker;

        var reason = (msg.TryGetProperty("text", out var mt) ? mt.GetString() ?? "" : "").Trim();

        // Same authorization gate as the buttons: only the configured receipt chat may decide.
        var fromId = msg.TryGetProperty("from", out var from) && from.TryGetProperty("id", out var fid) ? fid.GetRawText() : "";
        long? chatId = msg.TryGetProperty("chat", out var chat) && chat.TryGetProperty("id", out var chId) && chId.TryGetInt64(out var c) ? c : null;
        var configuredChat = (_store.GetTelegramSettings().ReceiptChatId ?? "").Trim();
        if (fromId != configuredChat && (chatId?.ToString() ?? "") != configuredChat)
        {
            _logger.LogWarning("Ignored Telegram rejection reason from unauthorized source (from={From}, chat={Chat})", fromId, chatId);
            return;
        }

        if (chatId is null) return;
        if (string.IsNullOrWhiteSpace(reason))
        {
            await SendMessageAsync(token, chatId.Value.ToString(), "دلیل رد نمی‌تواند خالی باشد. لطفاً دوباره در «پاسخ» به پیام دلیل را بنویسید.", "", ct);
            return;
        }

        var tx = _store.GetTransaction(txId);
        if (tx is null) return;
        if (tx.Status != TxStatus.Pending)
        {
            await SendMessageAsync(token, chatId.Value.ToString(), $"این تراکنش قبلاً {StatusFa(tx.Status)} شده است.", "", ct);
            return;
        }

        if (!_store.SetTransactionStatus(txId, TxStatus.Rejected, "telegram", reason))
        {
            await SendMessageAsync(token, chatId.Value.ToString(), "اعمال رد ناموفق بود.", "", ct);
            return;
        }

        _logger.LogInformation("Telegram receipt rejection: tx #{TxId} → Rejected by chat {Chat}", txId, configuredChat);

        var updated = _store.GetTransaction(txId) ?? tx;
        _ = _mailer.TransactionDecidedAsync(updated); // rejection email carries tx.Note (the reason)
        // Rewrite the original receipt to the resolved state (the reason is shown there and in this reply thread).
        if (receiptMsgId > 0)
            await EditDecidedAsync(token, chatId.Value, receiptMsgId, updated, ct);
        await SendMessageAsync(token, chatId.Value.ToString(), "❌ تراکنش رد شد و دلیل برای کاربر ایمیل شد.", "", ct);
    }

    private static (string? action, int? txId) ParseCallback(string data)
    {
        if (data.StartsWith(ApprovePrefix, StringComparison.Ordinal) && int.TryParse(data[ApprovePrefix.Length..], out var a))
            return ("ok", a);
        if (data.StartsWith(RejectPrefix, StringComparison.Ordinal) && int.TryParse(data[RejectPrefix.Length..], out var r))
            return ("no", r);
        return (null, null);
    }

    // The reason prompt embeds «#REJ:<txId>:<receiptMessageId>» so the admin's reply can be tied back to the
    // exact transaction and its original receipt message, without persisting any correlation state.
    private static string ReasonMarker(int txId, int receiptMsgId) => $"#REJ:{txId}:{receiptMsgId}";

    private static (int txId, int receiptMsgId)? ParseReasonMarker(string text)
    {
        var m = System.Text.RegularExpressions.Regex.Match(text, @"#REJ:(\d+):(\d+)");
        return m.Success ? (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)) : null;
    }

    // Asks the admin to reply with the rejection reason. Sent as a reply to the receipt with ForceReply so the
    // client pre-opens the reply box; because it's a reply to the bot, the answer reaches us even under group
    // privacy mode.
    private async Task SendReasonPromptAsync(string token, long chatId, int receiptMsgId, int txId, CancellationToken ct)
    {
        var forceReply = JsonSerializer.Serialize(new { force_reply = true, input_field_placeholder = "دلیل رد تراکنش..." });
        await PostFormAsync(token, "sendMessage", new Dictionary<string, string>
        {
            ["chat_id"] = chatId.ToString(),
            ["text"] = $"✍️ لطفاً دلیل رد این تراکنش را در «پاسخ» به همین پیام بنویسید.\n{ReasonMarker(txId, receiptMsgId)}",
            ["reply_to_message_id"] = receiptMsgId.ToString(),
            ["reply_markup"] = forceReply,
        }, ct);
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
        sb.AppendLine($"📨 ایمیل: {Esc(Dash(user?.Email))}");
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
                // Services that sell a fixed seat count show it here too, so the approver sees the full spec.
                if (item.UserCount > 0)
                    serviceLines.AppendLine($"👥 تعداد کاربر: {item.UserCount}");
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
        if (isOrder && !string.IsNullOrWhiteSpace(tx.OrderCode))
        {
            sb.AppendLine(); // blank line before the order code
            sb.AppendLine($"🧾 شماره سفارش: {Esc(tx.OrderCode!)}");
        }
        if (!string.IsNullOrWhiteSpace(tx.TrackingNumber))
        {
            sb.AppendLine(); // blank line before the tracking number
            sb.AppendLine($"🔗 شماره پیگیری: {Esc(tx.TrackingNumber!)}");
        }
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

    private async Task SendMessageAsync(string token, string chatId, string text, string markup, CancellationToken ct)
    {
        var fields = new Dictionary<string, string>
        {
            ["chat_id"] = chatId,
            ["text"] = text,
            ["parse_mode"] = "HTML",
            ["disable_web_page_preview"] = "true",
        };
        if (!string.IsNullOrEmpty(markup)) fields["reply_markup"] = markup; // Telegram rejects an empty reply_markup
        await PostFormAsync(token, "sendMessage", fields, ct);
    }

    private async Task AnswerCallbackAsync(string token, string callbackId, string text, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(callbackId)) return;
        var fields = new Dictionary<string, string> { ["callback_query_id"] = callbackId };
        if (!string.IsNullOrEmpty(text)) fields["text"] = text;
        await PostFormAsync(token, "answerCallbackQuery", fields, ct);
    }

    // Rewrites the reviewed message to show the outcome and collapses the two review buttons into a single
    // static status button (e.g. «✅ تأیید شد»), so it reads as done and can't be re-decided.
    private async Task EditDecidedAsync(string token, long chatId, int messageId, Transaction tx, CancellationToken ct)
    {
        var outcome = tx.Status == TxStatus.Approved ? "✅ تأیید شد" : "❌ رد شد";
        // A rejection shows its reason (tx.Note) right in the receipt so the channel keeps a record of why.
        var reasonLine = tx.Status == TxStatus.Rejected && !string.IsNullOrWhiteSpace(tx.Note)
            ? $"\n<b>📝 دلیل رد:</b> {Esc(tx.Note!)}" : "";
        var caption = $"{BuildCaption(tx)}\n\n<b>وضعیت: {outcome} (از طریق تلگرام)</b>{reasonLine}";
        // A one-tap "no-op" callback: DecidedPrefix isn't an approve/reject prefix, so HandleCallbackAsync's
        // ParseCallback returns null and the tap is silently acknowledged — the decision can't be replayed.
        var markup = JsonSerializer.Serialize(new
        {
            inline_keyboard = new[] { new object[] { new { text = outcome, callback_data = DecidedPrefix + tx.Id } } },
        });
        // The message carries a photo, so the caption is what we edit; the single button replaces the two.
        await PostFormAsync(token, "editMessageCaption", new Dictionary<string, string>
        {
            ["chat_id"] = chatId.ToString(),
            ["message_id"] = messageId.ToString(),
            ["caption"] = caption,
            ["parse_mode"] = "HTML",
            ["reply_markup"] = markup,
        }, ct);
    }

    // Like PostFormAsync but hands back Telegram's own description ("chat not found", "bot was blocked",
    // "terminated by other getUpdates request", …) rather than swallowing it into a log line.
    private async Task<(bool ok, string? error)> PostAndReportAsync(string token, string method,
        Dictionary<string, string> fields, CancellationToken ct)
    {
        try
        {
            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(20);
            using var form = new FormUrlEncodedContent(fields);
            using var resp = await http.PostAsync($"https://api.telegram.org/bot{token}/{method}", form, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (resp.IsSuccessStatusCode) return (true, null);

            var description = body;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("description", out var d))
                    description = d.GetString() ?? body;
            }
            catch { /* not JSON — fall back to the raw body */ }
            _logger.LogWarning("Telegram receipt test failed: {Status} {Description}", (int)resp.StatusCode, description);
            return (false, $"تلگرام خطا داد ({(int)resp.StatusCode}): {description}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram receipt test call failed");
            return (false, $"ارتباط با تلگرام برقرار نشد: {ex.Message}");
        }
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

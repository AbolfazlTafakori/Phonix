using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Services;

// Fulfillment over Telegram, on its OWN bot + group, fully separate from the receipt bot:
//   • NotifyOrderAsync — once a payment is approved and the order moves to «در حال آماده‌سازی», every purchased
//     account is posted as its own message (five accounts → five messages) so each can be worked and decided
//     independently, rather than one wall of text.
//   • ProcessUpdatesAsync — long-polls for the staff member's taps and replies and applies them through the
//     SAME store paths the panel uses (DeliverUnit / CancelOrder), so Telegram is just another front-end.
//
// Two shapes of account exist and the approve button branches on them:
//   • the customer handed us their own credentials (the plan collects inputs) → we upgraded their account, so
//     approving simply delivers a confirmation;
//   • a ready-made account → it is served straight from the virtual warehouse when the pool has one; only an
//     empty pool falls back to asking staff to send it, and the reply becomes the delivered content.
//
// Security: only the single configured chat may decide, and every store transition is idempotent, so a stale
// or replayed tap can never double-apply. Messages are NOT encrypted — Telegram group messages cannot be
// (a bot has no secret chats), so whoever is in the orders group can read them.
public interface ITelegramOrderService
{
    // Fire-and-forget from the request path: never throws. No-op unless the order bot is enabled and configured.
    Task NotifyOrderAsync(Order order, CancellationToken ct = default);

    // Convenience for the payment-approval paths (panel or receipt bot): given a just-approved order payment,
    // announce that order's accounts. Claims first, so whichever path gets there is the only one that posts.
    Task AnnounceApprovedOrderAsync(Transaction tx, CancellationToken ct = default);

    // Long-polls one getUpdates cycle from `offset` and applies any staff decisions. Returns the next offset.
    Task<long> ProcessUpdatesAsync(long offset, CancellationToken ct = default);

    // Sends a test message with the saved settings and returns Telegram's own error on failure — the real
    // sends are fire-and-forget and only log, so this is the one place a misconfiguration is visible.
    Task<(bool ok, string? error)> SendTestAsync(CancellationToken ct = default);
}

public sealed class TelegramOrderService : ITelegramOrderService
{
    private const string ApprovePrefix = "ordr:ok:";
    private const string RejectPrefix = "ordr:no:";
    // The static post-decision button; not an actionable prefix, so a tap on it is a harmless no-op.
    private const string DecidedPrefix = "ordr:done:";

    private readonly IDataStore _store;
    private readonly IUserMailer _mailer;
    private readonly IStockFulfillmentService _stock;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<TelegramOrderService> _logger;

    public TelegramOrderService(IDataStore store, IUserMailer mailer, IStockFulfillmentService stock,
        IHttpClientFactory httpFactory, ILogger<TelegramOrderService> logger)
    {
        _store = store;
        _mailer = mailer;
        _stock = stock;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    // The order bot's OWN token + chat, independent of the backup and receipt bots.
    private (string token, string chatId)? ActiveConfig()
    {
        var s = _store.GetTelegramSettings();
        if (!s.OrderBotEnabled) return null;
        var token = (s.OrderBotToken ?? "").Trim();
        var chatId = (s.OrderChatId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(token) || !IsNumericChatId(chatId)) return null;
        return (token, chatId);
    }

    public async Task NotifyOrderAsync(Order order, CancellationToken ct = default)
    {
        try
        {
            if (ActiveConfig() is not { } cfg) return;
            var (token, chatId) = cfg;

            // One message per purchased account. Orders placed before per-unit fulfillment have no units; they
            // are announced as a single message so they are never silently skipped.
            if (order.Units.Count == 0)
            {
                await SendMessageAsync(token, chatId, BuildLegacyCaption(order), "", ct);
                return;
            }

            // Every account except rejected ones. Pending accounts carry approve/reject buttons; ones the pool
            // already delivered (a fully wallet-paid, auto-delivered order) are posted as an FYI — the service
            // specs plus a static «تحویل خودکار» badge — so the group still sees the sale but has nothing to decide.
            foreach (var unit in order.Units.Where(u => !u.Rejected).OrderBy(u => u.UnitIndex))
            {
                string markup;
                string caption;
                if (unit.Delivered)
                {
                    caption = $"{BuildUnitCaption(order, unit)}\n\n<b>وضعیت: ✅ تحویل خودکار سرویس انجام شد</b>";
                    markup = JsonSerializer.Serialize(new
                    {
                        inline_keyboard = new[] { new object[]
                        {
                            new { text = "✅ تحویل خودکار سرویس انجام شد", callback_data = $"{DecidedPrefix}{order.Id}:{unit.Id}" },
                        } },
                    });
                }
                else
                {
                    caption = BuildUnitCaption(order, unit);
                    markup = JsonSerializer.Serialize(new
                    {
                        inline_keyboard = new[] { new object[]
                        {
                            new { text = "✅ تأیید", callback_data = $"{ApprovePrefix}{order.Id}:{unit.Id}" },
                            new { text = "❌ رد", callback_data = $"{RejectPrefix}{order.Id}:{unit.Id}" },
                        } },
                    });
                }
                await SendMessageAsync(token, chatId, caption, markup, ct);
            }
        }
        catch (Exception ex)
        {
            // Best-effort: placing/approving an order must never fail because Telegram is down.
            _logger.LogWarning(ex, "Telegram order notification failed for order {Code}", order.Code);
        }
    }

    public async Task AnnounceApprovedOrderAsync(Transaction tx, CancellationToken ct = default)
    {
        try
        {
            if (tx.Type != TxTypes.OrderPayment || tx.Status != TxStatus.Approved) return;
            if (string.IsNullOrWhiteSpace(tx.OrderCode)) return;
            var order = _store.GetUserOrders(tx.UserId).FirstOrDefault(o => o.Code == tx.OrderCode);
            // Only an order the approval actually advanced into fulfillment is worth announcing.
            if (order is null || order.Status != OrderStatus.Preparing) return;
            if (!_store.TryClaimOrderBotNotification(order.Id)) return;
            await NotifyOrderAsync(order, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram order announce failed for tx #{TxId}", tx.Id);
        }
    }

    public async Task<(bool ok, string? error)> SendTestAsync(CancellationToken ct = default)
    {
        var s = _store.GetTelegramSettings();
        // Report the exact reason ActiveConfig() would have refused, instead of silently doing nothing.
        if (!s.OrderBotEnabled) return (false, "ربات سفارشات خاموش است.");
        var token = (s.OrderBotToken ?? "").Trim();
        var chatId = (s.OrderChatId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(token)) return (false, "توکن بات سفارشات وارد نشده است.");
        if (!IsNumericChatId(chatId))
            return (false, $"شناسهٔ گروه «{chatId}» عددی نیست. باید عدد باشد (گروه/کانال با منفی شروع می‌شود، مثل ‎-1001234567890).");

        return await PostAndReportAsync(token, "sendMessage", new Dictionary<string, string>
        {
            ["chat_id"] = chatId,
            ["text"] = "✅ پیام تست ربات سفارشات فونیکس. اگر این پیام را می‌بینید، تنظیمات درست است.",
        }, ct);
    }

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
            _logger.LogWarning("Telegram order test failed: {Status} {Description}", (int)resp.StatusCode, description);
            return (false, $"تلگرام خطا داد ({(int)resp.StatusCode}): {description}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram order test call failed");
            return (false, $"ارتباط با تلگرام برقرار نشد: {ex.Message}");
        }
    }

    public async Task<long> ProcessUpdatesAsync(long offset, CancellationToken ct = default)
    {
        if (ActiveConfig() is not { } cfg) return offset;
        var (token, _) = cfg;

        var url = $"https://api.telegram.org/bot{token}/getUpdates"
                + $"?offset={offset}&timeout=25&allowed_updates=%5B%22callback_query%22%2C%22message%22%5D";

        using var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(35); // longer than the 25s long-poll so the poll itself never times out

        using var resp = await http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("Telegram order getUpdates failed: {Status}", (int)resp.StatusCode);
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
                await HandleAccountReplyAsync(token, m, ct);
        }
        return next;
    }

    // ── Captions ──────────────────────────────────────────────────────────────────────────────────────────

    // One purchased account, as Telegram HTML. Sections are blockquotes to match the receipt bot's layout.
    private string BuildUnitCaption(Order order, OrderUnit unit)
    {
        var sb = new StringBuilder();
        sb.AppendLine("🛒|📦 سفارش جدید ( در انتظار آماده‌سازی )");
        sb.AppendLine();

        sb.Append(UserBlock(order));
        sb.AppendLine();

        var category = _store.GetProduct(unit.ProductId) is { } p ? _store.GetCategory(p.CategoryId)?.Name : null;
        var (planType, planDuration) = SplitPlan(unit.Plan);
        sb.AppendLine("<blockquote>مشخصات سرویس");
        sb.AppendLine($"🚦دسته‌بندی: {Esc(Dash(category))}");
        sb.AppendLine($"✏️ نام سرویس: {Esc(Dash(unit.Name))}");
        sb.AppendLine($"🔋نوع سرویس: {Esc(Dash(planType))}");
        sb.AppendLine($"⏰ مدت سرویس: {Esc(Dash(planDuration))}");
        // Services that sell a fixed seat count show it so staff hand over the right-sized account.
        if (unit.UserCount > 0)
            sb.AppendLine($"👥 تعداد کاربر: {unit.UserCount}");
        // Which of the buyer's accounts this message is, so five identical messages stay tellable apart.
        if (order.Units.Count > 1)
            sb.AppendLine($"🔢 اکانت: {unit.UnitIndex} از {order.Units.Count}");
        sb.AppendLine();
        sb.AppendLine($"🧾 شماره سفارش: {Esc(order.Code)}</blockquote>");
        sb.AppendLine();

        sb.Append(InputsBlock(unit));

        sb.Append(JalaliDate.NowStamp());
        return sb.ToString();
    }

    // What the customer typed for THIS account (decrypted for display), or a note that it needs a ready-made one.
    private static string InputsBlock(OrderUnit unit)
    {
        var sb = new StringBuilder();
        var inputs = unit.CustomerInputs;
        if (inputs.Count == 0 && string.IsNullOrWhiteSpace(unit.CustomerNote))
        {
            sb.AppendLine("<blockquote>اطلاعات کاربر");
            sb.AppendLine("این سرویس اطلاعاتی از کاربر نمی‌گیرد؛ باید یک اکانت آماده تحویل داده شود.</blockquote>");
            sb.AppendLine();
            return sb.ToString();
        }

        sb.AppendLine("<blockquote>اطلاعات واردشده توسط کاربر");
        foreach (var v in inputs)
        {
            // Stored encrypted; the group is where staff actually need to read it.
            var value = v.Sensitive ? SensitiveField.Reveal(v.Value) : v.Value;
            sb.AppendLine($"▫️ {Esc(v.Label)}: {Esc(Dash(value))}");
        }
        if (!string.IsNullOrWhiteSpace(unit.CustomerNote))
            sb.AppendLine($"📝 یادداشت: {Esc(unit.CustomerNote!)}");
        sb.AppendLine("</blockquote>");
        sb.AppendLine();
        return sb.ToString();
    }

    private string UserBlock(Order order)
    {
        var user = order.UserId > 0 ? _store.GetUser(order.UserId) : null;
        var sb = new StringBuilder();
        sb.AppendLine("<blockquote>مشخصات کاربر");
        sb.AppendLine($"▫️آیدی کاربر: {Esc(user?.Code is { Length: > 0 } code ? code : order.UserId.ToString())}");
        sb.AppendLine($"👨‍💼اسم کاربر: {Esc(Dash(user?.Name ?? order.UserName))}");
        sb.AppendLine($"⚡️ نام کاربری: {Esc(Dash(user?.Username))}");
        sb.AppendLine($"📞 شماره تماس: {Esc(Dash(user?.Phone))}");
        sb.AppendLine($"📨 ایمیل: {Esc(Dash(user?.Email))}</blockquote>");
        return sb.ToString();
    }

    // Orders from before per-unit fulfillment: no units to decide on, so this is an FYI message only.
    private string BuildLegacyCaption(Order order)
    {
        var sb = new StringBuilder();
        sb.AppendLine("🛒|📦 سفارش جدید ( بدون تفکیک اکانت )");
        sb.AppendLine();
        sb.Append(UserBlock(order));
        sb.AppendLine();
        sb.AppendLine("<blockquote>اقلام سفارش");
        foreach (var item in order.Items)
            sb.AppendLine($"▫️ {Esc(item.Name)} — {Esc(Dash(item.Plan))} × {item.Quantity}");
        sb.AppendLine();
        sb.AppendLine($"🧾 شماره سفارش: {Esc(order.Code)}</blockquote>");
        sb.Append(JalaliDate.NowStamp());
        return sb.ToString();
    }

    // ── Decisions ─────────────────────────────────────────────────────────────────────────────────────────

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

        var configuredChat = (_store.GetTelegramSettings().OrderChatId ?? "").Trim();
        if (fromId != configuredChat && (chatId?.ToString() ?? "") != configuredChat)
        {
            _logger.LogWarning("Rejected Telegram order decision from unauthorized source (from={From}, chat={Chat})", fromId, chatId);
            await AnswerCallbackAsync(token, callbackId, "شما مجاز به این عملیات نیستید.", ct);
            return;
        }

        var (action, orderId, unitId) = ParseCallback(data);
        if (action is null || orderId is null || unitId is null)
        {
            await AnswerCallbackAsync(token, callbackId, "", ct);
            return;
        }

        var order = _store.GetOrder(orderId.Value);
        var unit = order?.Units.FirstOrDefault(u => u.Id == unitId.Value);
        if (order is null || unit is null)
        {
            await AnswerCallbackAsync(token, callbackId, "سفارش یافت نشد.", ct);
            return;
        }
        if (order.Status is OrderStatus.Cancelled)
        {
            await AnswerCallbackAsync(token, callbackId, "این سفارش قبلاً لغو شده است.", ct);
            return;
        }
        if (unit.Delivered)
        {
            await AnswerCallbackAsync(token, callbackId, "این اکانت قبلاً تحویل شده است.", ct);
            if (chatId is not null && messageId is not null)
                await EditDecidedAsync(token, chatId.Value, messageId.Value, order, unit, "✅ تحویل شد", ct);
            return;
        }

        if (action == "no")
        {
            // Reject only THIS account. The buyer is refunded what they actually paid for it — its price after
            // its share of the order discount — and the rest of the order carries on independently.
            var (updated, refunded, error) = _store.RejectUnit(orderId.Value, unit.Id, "رد سفارش از طریق تلگرام", "telegram");
            if (error is not null)
            {
                await AnswerCallbackAsync(token, callbackId, error, ct);
                return;
            }
            _store.AddNotification(order.UserId, "سفارش شما رد شد",
                $"«{unit.Name}» از سفارش {order.Code} رد شد. برای پیگیری به بخش تیکت‌ها مراجعه کنید.", "/account/tickets");
            _logger.LogInformation("Telegram order decision: order {Code} unit {UnitId} → Rejected, refunded {Refund}",
                order.Code, unit.Id, refunded);
            await AnswerCallbackAsync(token, callbackId, $"❌ رد شد — {refunded:N0} تومان بازگشت.", ct);
            if (chatId is not null && messageId is not null)
            {
                var rejectedUnit = updated?.Units.FirstOrDefault(u => u.Id == unit.Id) ?? unit;
                await EditDecidedAsync(token, chatId.Value, messageId.Value, updated ?? order, rejectedUnit, "❌ رد شد", ct);
            }
            return;
        }

        // Approve. The customer supplied their own account → the work was done on it, so confirm and deliver.
        if (unit.CustomerInputs.Count > 0)
        {
            await DeliverAsync(token, chatId, messageId, order, unit,
                "اشتراک روی اکانت خودتان فعال شد.", callbackId, ct);
            return;
        }

        // A ready-made account: the warehouse serves it without asking anyone anything.
        if (_stock.ServeUnit(order, unit, StockFulfillmentService.Actor) is { } served)
        {
            await AfterDeliverAsync(token, chatId, messageId, served.order, unit, served.justCompleted, callbackId, ct);
            return;
        }

        // Pool empty: ask staff to send it, and the reply delivers it.
        if (chatId is not null && messageId is not null)
            await SendAccountPromptAsync(token, chatId.Value, messageId.Value, order.Id, unit.Id, ct);
        await AnswerCallbackAsync(token, callbackId, "✍️ اکانت آماده را در «پاسخ» ارسال کنید.", ct);
    }

    // Applies the delivery through the panel's own path, so completion, invoice and referral all behave the same.
    private async Task DeliverAsync(string token, long? chatId, int? messageId, Order order, OrderUnit unit,
        string content, string callbackId, CancellationToken ct)
    {
        var (updated, justCompleted) = _store.DeliverUnit(order.Id, unit.Id, content, "telegram");
        if (updated is null)
        {
            await AnswerCallbackAsync(token, callbackId, "تحویل ناموفق بود.", ct);
            return;
        }
        await AfterDeliverAsync(token, chatId, messageId, updated, unit, justCompleted, callbackId, ct);
    }

    // Everything a delivered account owes the outside world, shared by the two ways a tap can deliver one:
    // the customer's own account being confirmed, and the warehouse serving a ready-made one.
    private async Task AfterDeliverAsync(string token, long? chatId, int? messageId, Order updated, OrderUnit unit,
        bool justCompleted, string callbackId, CancellationToken ct)
    {
        _logger.LogInformation("Telegram order decision: order {Code} unit {UnitId} → Delivered (completed={Done})",
            updated.Code, unit.Id, justCompleted);

        _ = _mailer.OrderUnitDeliveredAsync(updated, unit.Id);
        if (justCompleted) _ = _mailer.OrderCompletedAsync(updated);

        await AnswerCallbackAsync(token, callbackId, justCompleted ? "✅ تحویل شد — سفارش تکمیل شد." : "✅ تحویل شد.", ct);
        if (chatId is not null && messageId is not null)
        {
            var deliveredUnit = updated.Units.FirstOrDefault(u => u.Id == unit.Id) ?? unit;
            await EditDecidedAsync(token, chatId.Value, messageId.Value, updated, deliveredUnit, "✅ تحویل شد", ct);
        }
    }

    // The staff member's reply carrying a ready-made account, tied back by the marker in the bot's prompt.
    private async Task HandleAccountReplyAsync(string token, JsonElement msg, CancellationToken ct)
    {
        if (!msg.TryGetProperty("reply_to_message", out var replied)) return;
        var promptText = replied.TryGetProperty("text", out var pt) ? pt.GetString() ?? "" : "";
        if (ParseAccountMarker(promptText) is not { } marker) return; // not a reply to our account prompt
        var (orderId, unitId, sourceMsgId) = marker;

        var content = (msg.TryGetProperty("text", out var mt) ? mt.GetString() ?? "" : "").Trim();

        var fromId = msg.TryGetProperty("from", out var from) && from.TryGetProperty("id", out var fid) ? fid.GetRawText() : "";
        long? chatId = msg.TryGetProperty("chat", out var chat) && chat.TryGetProperty("id", out var chId) && chId.TryGetInt64(out var c) ? c : null;
        var configuredChat = (_store.GetTelegramSettings().OrderChatId ?? "").Trim();
        if (fromId != configuredChat && (chatId?.ToString() ?? "") != configuredChat)
        {
            _logger.LogWarning("Ignored Telegram account reply from unauthorized source (from={From}, chat={Chat})", fromId, chatId);
            return;
        }
        if (chatId is null) return;

        if (string.IsNullOrWhiteSpace(content))
        {
            await SendMessageAsync(token, chatId.Value.ToString(), "اطلاعات اکانت نمی‌تواند خالی باشد. لطفاً دوباره در «پاسخ» ارسال کنید.", "", ct);
            return;
        }

        var order = _store.GetOrder(orderId);
        var unit = order?.Units.FirstOrDefault(u => u.Id == unitId);
        if (order is null || unit is null) return;
        if (unit.Delivered)
        {
            await SendMessageAsync(token, chatId.Value.ToString(), "این اکانت قبلاً تحویل شده است.", "", ct);
            return;
        }

        var (updated, justCompleted) = _store.DeliverUnit(orderId, unitId, content, "telegram");
        if (updated is null)
        {
            await SendMessageAsync(token, chatId.Value.ToString(), "تحویل ناموفق بود.", "", ct);
            return;
        }

        _logger.LogInformation("Telegram order delivery by reply: order {Code} unit {UnitId} (completed={Done})",
            order.Code, unitId, justCompleted);

        _ = _mailer.OrderUnitDeliveredAsync(updated, unitId);
        if (justCompleted) _ = _mailer.OrderCompletedAsync(updated);

        var deliveredUnit = updated.Units.FirstOrDefault(u => u.Id == unitId) ?? unit;
        if (sourceMsgId > 0)
            await EditDecidedAsync(token, chatId.Value, sourceMsgId, updated, deliveredUnit, "✅ تحویل شد", ct);
        await SendMessageAsync(token, chatId.Value.ToString(),
            justCompleted ? "✅ اکانت تحویل شد و سفارش تکمیل شد." : "✅ اکانت تحویل شد.", "", ct);
    }

    private static (string? action, int? orderId, int? unitId) ParseCallback(string data)
    {
        foreach (var (prefix, action) in new[] { (ApprovePrefix, "ok"), (RejectPrefix, "no") })
        {
            if (!data.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var parts = data[prefix.Length..].Split(':');
            if (parts.Length == 2 && int.TryParse(parts[0], out var o) && int.TryParse(parts[1], out var u))
                return (action, o, u);
        }
        return (null, null, null);
    }

    // The prompt embeds «#ACC:<orderId>:<unitId>:<messageId>» so the reply can be tied back to the exact
    // account and its original message, without persisting any correlation state.
    private static string AccountMarker(int orderId, int unitId, int messageId) => $"#ACC:{orderId}:{unitId}:{messageId}";

    private static (int orderId, int unitId, int messageId)? ParseAccountMarker(string text)
    {
        var m = System.Text.RegularExpressions.Regex.Match(text, @"#ACC:(\d+):(\d+):(\d+)");
        return m.Success
            ? (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value))
            : null;
    }

    // Asks staff to reply with a ready-made account, which only happens once the product's pool has run dry.
    // ForceReply pre-opens the reply box, and because the answer replies to the bot it reaches us even under
    // group privacy mode.
    private async Task SendAccountPromptAsync(string token, long chatId, int sourceMsgId, int orderId, int unitId, CancellationToken ct)
    {
        var forceReply = JsonSerializer.Serialize(new { force_reply = true, input_field_placeholder = "اطلاعات اکانت آماده..." });
        await PostFormAsync(token, "sendMessage", new Dictionary<string, string>
        {
            ["chat_id"] = chatId.ToString(),
            ["text"] = "✍️ انبار این محصول خالی است. لطفاً اطلاعات اکانت آماده را در «پاسخ» به همین پیام ارسال کنید تا برای کاربر ثبت شود.\n"
                     + AccountMarker(orderId, unitId, sourceMsgId),
            ["reply_to_message_id"] = sourceMsgId.ToString(),
            ["reply_markup"] = forceReply,
        }, ct);
    }

    // ── Low-level Telegram calls ──────────────────────────────────────────────────────────────────────────

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

    // Rewrites the decided message and collapses the two buttons into one static status button.
    private async Task EditDecidedAsync(string token, long chatId, int messageId, Order order, OrderUnit unit,
        string outcome, CancellationToken ct)
    {
        var text = $"{BuildUnitCaption(order, unit)}\n\n<b>وضعیت: {outcome} (از طریق تلگرام)</b>";
        if (unit.Delivered && !string.IsNullOrWhiteSpace(unit.DeliveryContent))
            text += $"\n<b>📦 تحویل‌شده:</b> {Esc(unit.DeliveryContent)}";
        var markup = JsonSerializer.Serialize(new
        {
            inline_keyboard = new[] { new object[] { new { text = outcome, callback_data = $"{DecidedPrefix}{order.Id}:{unit.Id}" } } },
        });
        await PostFormAsync(token, "editMessageText", new Dictionary<string, string>
        {
            ["chat_id"] = chatId.ToString(),
            ["message_id"] = messageId.ToString(),
            ["text"] = text,
            ["parse_mode"] = "HTML",
            ["reply_markup"] = markup,
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
                _logger.LogWarning("Telegram order {Method} failed: {Status}", method, (int)resp.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram order {Method} call failed", method);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────────────────

    private static string Dash(string? v) => string.IsNullOrWhiteSpace(v) ? "-" : v.Trim();

    private static string Esc(string v) => System.Net.WebUtility.HtmlEncode(v);

    // OrderUnit.Plan is built as "{Type} · {Months} ماهه".
    private static (string? type, string? duration) SplitPlan(string? plan)
    {
        if (string.IsNullOrWhiteSpace(plan)) return (null, null);
        var parts = plan.Split('·');
        return (parts[0].Trim(), parts.Length > 1 ? parts[1].Trim() : null);
    }

    // digits only, optionally a single leading '-' (group/channel ids are negative).
    private static bool IsNumericChatId(string id) =>
        System.Text.RegularExpressions.Regex.IsMatch(id, @"^-?\d{1,32}$");
}

using System.Text.Json;
using System.Text.RegularExpressions;
using Phonix.Api.Data;
using Phonix.Api.Models;

namespace Phonix.Api.Services;

// The support bot: tickets and live chat in a staff Telegram group. Every new ticket, every customer reply on a
// ticket and every live-chat message is posted to the group; staff answer by REPLYING to the bot's message, and
// the answer reaches the customer exactly as an answer from the panel does — as «پشتیبانی فونیکس», with the usual
// email for a ticket. Answers given on the site are posted to the group as well, so the team sees what is handled.
//
// A thread's later messages are posted as replies to its first one, and every message carries a #ticket_12 /
// #chat_7 tag: tapping the tag lists the whole conversation, and the tag in the replied-to message is how an
// answer finds its ticket — no state kept on our side.
//
// Its own bot and token: Telegram hands a bot's updates to exactly one poller, so it can't share another bot.
// Only the one configured group is listened to, and anyone in it can answer — it has to be a staff-only group.
public interface ITelegramSupportBot
{
    // Configured and switched on (it polls). Posting also needs the group's id.
    bool IsActive { get; }

    // A ticket was opened — by the customer, or by staff on their behalf. Never throws.
    Task NotifyTicketOpenedAsync(Ticket ticket, CancellationToken ct = default);

    // A message on an existing ticket: the customer's reply (asks for an answer), or an answer staff gave on the
    // site (staffName set; posted so the group sees it is handled). Never throws.
    Task NotifyTicketMessageAsync(Ticket ticket, TicketMessage message, string? staffName = null, CancellationToken ct = default);

    // The same for live chat.
    Task NotifyChatMessageAsync(ChatConversation conversation, ChatMessage message, string? staffName = null, CancellationToken ct = default);

    Task<long> ProcessUpdatesAsync(long offset, CancellationToken ct = default);
    Task<(bool Ok, string? Username, string? Error)> CheckTokenAsync(string token, CancellationToken ct = default);
    // A message to the configured group, reporting Telegram's own error when it fails.
    Task<(bool Ok, string? Error)> SendTestAsync(CancellationToken ct = default);
}

public sealed class TelegramSupportBot : ITelegramSupportBot
{
    private const string ClosePrefix = "sup:close:";   // sup:close:t:12 · sup:close:c:7
    private const string ClosedPrefix = "sup:done:";
    private const string SupportAuthor = "پشتیبانی فونیکس";
    // Telegram allows 4096 characters; the rest of the message (headers, tag) needs room too.
    private const int MaxBody = 3500;
    // What an answer may be — the same ceiling the panel's chat box has.
    private const int MaxAnswer = 2000;
    private static readonly Regex Tag = new(@"#(ticket|chat)_(\d+)", RegexOptions.Compiled);

    private readonly IDataStore _store;
    private readonly IUserMailer _mailer;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<TelegramSupportBot> _logger;

    public TelegramSupportBot(IDataStore store, IUserMailer mailer, IHttpClientFactory httpFactory, ILogger<TelegramSupportBot> logger)
    {
        _store = store;
        _mailer = mailer;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    private static string FrontendUrl => (Environment.GetEnvironmentVariable("PHONIX_FRONTEND_URL") ?? "http://localhost:3000").TrimEnd('/');

    private string? ActiveToken()
    {
        var s = _store.GetTelegramSettings();
        var token = (s.SupportBotToken ?? "").Trim();
        return s.SupportBotEnabled && token.Length > 0 ? token : null;
    }

    private (string Token, string ChatId)? PostingConfig()
    {
        if (ActiveToken() is not { } token) return null;
        var chat = (_store.GetTelegramSettings().SupportChatId ?? "").Trim();
        return Regex.IsMatch(chat, @"^-?\d{1,32}$") ? (token, chat) : null;
    }

    public bool IsActive => ActiveToken() is not null;

    // ── Posting ─────────────────────────────────────────────────────────────────────────────────────────────

    public async Task NotifyTicketOpenedAsync(Ticket ticket, CancellationToken ct = default)
    {
        try
        {
            if (PostingConfig() is not { } cfg) return;
            var first = ticket.Messages.FirstOrDefault();
            var byStaff = first?.IsAdmin == true;
            var lines = new List<string>
            {
                byStaff ? "🎫 <b>تیکت جدید — باز شده توسط پشتیبانی</b>" : "🎫 <b>تیکت جدید</b>",
                $"🧾 {Esc(ticket.Code)} · {Esc(ticket.Subject)}",
                $"👤 {Customer(ticket.UserId, ticket.UserName)}",
                $"📂 {Esc(Dash(ticket.Department))} · اولویت: {PriorityFa(ticket.Priority)}",
                "",
                Body(first?.Body),
            };
            if ((Attachment(first?.Attachment) ?? Attachment(ticket.Attachment)) is { } file) lines.Add($"📎 پیوست: {file}");
            if (!byStaff) lines.Add("\n↩️ برای پاسخ، روی همین پیام «Reply» بزنید.");
            lines.Add($"#ticket_{ticket.Id}");
            var sent = await SendAsync(cfg.Token, cfg.ChatId, string.Join("\n", lines), CloseKeyboard('t', ticket.Id), null, ct);
            if (sent is int id && long.TryParse(cfg.ChatId, out var chat)) _store.SetTicketTelegramMessage(ticket.Id, chat, id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Support bot: posting ticket {Code} failed", ticket.Code);
        }
    }

    public async Task NotifyTicketMessageAsync(Ticket ticket, TicketMessage message, string? staffName = null, CancellationToken ct = default)
    {
        try
        {
            if (PostingConfig() is not { } cfg) return;
            var lines = new List<string>();
            if (message.IsAdmin)
            {
                lines.Add($"↩️ <b>پاسخ پشتیبانی</b> — از طریق سایت{(string.IsNullOrWhiteSpace(staffName) ? "" : $" ({Esc(staffName)})")}");
                lines.Add($"🧾 {Esc(ticket.Code)} · {Esc(ticket.Subject)}");
                lines.Add("");
                lines.Add(Body(message.Body));
            }
            else
            {
                lines.Add("💬 <b>پیام جدید کاربر در تیکت</b>");
                lines.Add($"🧾 {Esc(ticket.Code)} · {Esc(ticket.Subject)}");
                lines.Add($"👤 {Customer(ticket.UserId, ticket.UserName)}");
                lines.Add("");
                lines.Add(Body(message.Body));
                if (Attachment(message.Attachment) is { } file) lines.Add($"📎 پیوست: {file}");
                lines.Add("\n↩️ برای پاسخ، روی همین پیام «Reply» بزنید.");
            }
            lines.Add($"#ticket_{ticket.Id}");
            var thread = InThread(cfg.ChatId, ticket.TelegramChatId, ticket.TelegramMessageId);
            var sent = await SendAsync(cfg.Token, cfg.ChatId, string.Join("\n", lines),
                message.IsAdmin ? null : CloseKeyboard('t', ticket.Id), thread, ct);
            // A ticket opened before the bot was set up gets its thread here.
            if (thread is null && !message.IsAdmin && sent is int id && long.TryParse(cfg.ChatId, out var chat))
                _store.SetTicketTelegramMessage(ticket.Id, chat, id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Support bot: posting a message on ticket {Code} failed", ticket.Code);
        }
    }

    public async Task NotifyChatMessageAsync(ChatConversation conversation, ChatMessage message, string? staffName = null, CancellationToken ct = default)
    {
        try
        {
            if (PostingConfig() is not { } cfg) return;
            var lines = new List<string>();
            if (message.FromAdmin)
            {
                lines.Add($"↩️ <b>پاسخ پشتیبانی</b> — از طریق سایت{(string.IsNullOrWhiteSpace(staffName) ? "" : $" ({Esc(staffName)})")}");
                lines.Add($"👤 {Customer(conversation.UserId, conversation.UserName)}");
                lines.Add("");
                lines.Add(Body(message.Body));
            }
            else
            {
                lines.Add("💬 <b>گفت‌وگوی آنلاین</b>");
                lines.Add($"👤 {Customer(conversation.UserId, conversation.UserName)}");
                lines.Add("");
                lines.Add(Body(message.Body));
                lines.Add("\n↩️ برای پاسخ، روی همین پیام «Reply» بزنید.");
            }
            lines.Add($"#chat_{conversation.Id}");
            var thread = InThread(cfg.ChatId, conversation.TelegramChatId, conversation.TelegramMessageId);
            var sent = await SendAsync(cfg.Token, cfg.ChatId, string.Join("\n", lines),
                message.FromAdmin ? null : CloseKeyboard('c', conversation.Id), thread, ct);
            if (thread is null && !message.FromAdmin && sent is int id && long.TryParse(cfg.ChatId, out var chat))
                _store.SetConversationTelegramMessage(conversation.Id, chat, id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Support bot: posting chat #{Id} failed", conversation.Id);
        }
    }

    // The thread's first message, when it was posted to the group the bot posts to now.
    private static int? InThread(string chatId, long? threadChat, int? threadMessage) =>
        threadChat?.ToString() == chatId ? threadMessage : null;

    private string Customer(int userId, string displayName)
    {
        var username = _store.GetUser(userId)?.Username;
        return string.IsNullOrWhiteSpace(username) ? Esc(displayName) : $"{Esc(displayName)} ({Esc(username)})";
    }

    private static string Body(string? text)
    {
        var t = (text ?? "").Trim();
        if (t.Length > MaxBody) t = t[..MaxBody] + "…";
        return t.Length == 0 ? "—" : Esc(t);
    }

    // Attachments are stored as site paths; the group needs a link it can open.
    private static string? Attachment(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var url = path.StartsWith('/') ? FrontendUrl + path : path;
        return Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)
            ? $"<a href=\"{Esc(u.ToString())}\">مشاهده</a>"
            : null;
    }

    private static string PriorityFa(TicketPriority p) => p switch
    {
        TicketPriority.High => "زیاد",
        TicketPriority.Low => "کم",
        _ => "متوسط",
    };

    private static string Dash(string? v) => string.IsNullOrWhiteSpace(v) ? "-" : v.Trim();
    private static string Esc(string v) => System.Net.WebUtility.HtmlEncode(v);

    private static string CloseKeyboard(char kind, int id) => JsonSerializer.Serialize(new
    {
        inline_keyboard = new[] { new object[] { new { text = kind == 't' ? "🔒 بستن تیکت" : "🔒 بستن گفت‌وگو", callback_data = $"{ClosePrefix}{kind}:{id}" } } },
    });

    // ── Updates: answers and the close button ───────────────────────────────────────────────────────────────

    public async Task<long> ProcessUpdatesAsync(long offset, CancellationToken ct = default)
    {
        if (ActiveToken() is not { } token) return offset;
        var url = $"https://api.telegram.org/bot{token}/getUpdates?offset={offset}&timeout=25"
                  + "&allowed_updates=%5B%22message%22%2C%22callback_query%22%5D";
        using var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(35);
        using var resp = await http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("Support bot getUpdates failed: {Status}", (int)resp.StatusCode);
            return offset;
        }
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("result", out var results) || results.ValueKind != JsonValueKind.Array)
            return offset;

        var next = offset;
        foreach (var update in results.EnumerateArray())
        {
            if (update.TryGetProperty("update_id", out var uid) && uid.TryGetInt64(out var id) && id >= next)
                next = id + 1;
            try
            {
                if (update.TryGetProperty("callback_query", out var cq)) await HandleCallbackAsync(token, cq, ct);
                else if (update.TryGetProperty("message", out var msg)) await HandleMessageAsync(token, msg, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Support bot update handling failed");
            }
        }
        return next;
    }

    private string ConfiguredChat => (_store.GetTelegramSettings().SupportChatId ?? "").Trim();

    private async Task HandleMessageAsync(string token, JsonElement msg, CancellationToken ct)
    {
        if (!msg.TryGetProperty("chat", out var chat) || !chat.TryGetProperty("id", out var cid) || !cid.TryGetInt64(out var chatId))
            return;
        var messageId = msg.TryGetProperty("message_id", out var mid) && mid.TryGetInt32(out var m) ? m : 0;
        var text = (msg.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "").Trim();

        // «/id» tells whoever is setting the bot up which id this group has. Harmless anywhere.
        var head = text.Split(' ', 2)[0].Split('@')[0].ToLowerInvariant();
        if (head == "/id")
        {
            await SendAsync(token, chatId.ToString(), $"شناسه‌ی این گفت‌وگو: <code>{chatId}</code>\nاین عدد را در پنل، بخش «ربات تلگرام پشتیبانی» وارد کنید.", null, messageId, ct);
            return;
        }

        // Answers count only from the configured staff group, and only as replies to one of our thread messages.
        if (chatId.ToString() != ConfiguredChat) return;
        if (!msg.TryGetProperty("reply_to_message", out var replied)) return;
        var repliedText = replied.TryGetProperty("text", out var rt) ? rt.GetString() ?? "" : "";
        var tags = Tag.Matches(repliedText);
        if (tags.Count == 0) return;
        var target = tags[^1];
        var kind = target.Groups[1].Value;
        var id = int.Parse(target.Groups[2].Value);

        if (text.Length == 0)
        {
            await SendAsync(token, chatId.ToString(), "فعلاً فقط پاسخ متنی پشتیبانی می‌شود؛ متن پاسخ را بنویسید.", null, messageId, ct);
            return;
        }
        if (text.StartsWith('/')) return;
        if (text.Length > MaxAnswer) text = text[..MaxAnswer];
        var who = msg.TryGetProperty("from", out var from) ? DecisionVia.TelegramName(from) : null;

        if (kind == "ticket")
        {
            if (_store.GetTicket(id) is null)
            {
                await SendAsync(token, chatId.ToString(), "این تیکت یافت نشد.", null, messageId, ct);
                return;
            }
            var ticket = _store.ReplyTicket(id, SupportAuthor, text, isAdmin: true);
            if (ticket is null) return;
            _ = _mailer.TicketRepliedAsync(ticket);
            _logger.LogInformation("Support bot: ticket {Code} answered from Telegram by {Who}", ticket.Code, who);
            await SendAsync(token, chatId.ToString(), "✅ پاسخ برای کاربر ارسال شد.", null, messageId, ct);
            return;
        }

        var conversation = _store.GetConversation(id);
        if (conversation is null)
        {
            await SendAsync(token, chatId.ToString(), "این گفت‌وگو یافت نشد.", null, messageId, ct);
            return;
        }
        // A closed thread is gone from the customer's widget: an answer there would never be read.
        if (conversation.Status == ConversationStatus.Closed)
        {
            await SendAsync(token, chatId.ToString(), "این گفت‌وگو بسته شده است؛ پاسخ ارسال نشد.", null, messageId, ct);
            return;
        }
        _store.AddAdminMessage(id, SupportAuthor, text);
        // Someone who wrote from the customer bot is waiting there, not on the site.
        if (conversation.ViaTelegram) _ = _mailer.SupportReplyAsync(conversation.UserId, text);
        _logger.LogInformation("Support bot: chat #{Id} answered from Telegram by {Who}", id, who);
        await SendAsync(token, chatId.ToString(), "✅ پاسخ برای کاربر ارسال شد.", null, messageId, ct);
    }

    private async Task HandleCallbackAsync(string token, JsonElement cq, CancellationToken ct)
    {
        var callbackId = cq.TryGetProperty("id", out var cbid) ? cbid.GetString() ?? "" : "";
        var data = cq.TryGetProperty("data", out var d) ? d.GetString() ?? "" : "";
        long? chatId = null;
        int? messageId = null;
        if (cq.TryGetProperty("message", out var msg))
        {
            if (msg.TryGetProperty("chat", out var chat) && chat.TryGetProperty("id", out var c) && c.TryGetInt64(out var cv)) chatId = cv;
            if (msg.TryGetProperty("message_id", out var mid) && mid.TryGetInt32(out var mv)) messageId = mv;
        }
        if (chatId?.ToString() != ConfiguredChat)
        {
            await AnswerAsync(token, callbackId, "شما مجاز به این عملیات نیستید.", ct);
            return;
        }
        if (data.StartsWith(ClosedPrefix, StringComparison.Ordinal))
        {
            await AnswerAsync(token, callbackId, "", ct);
            return;
        }
        var match = Regex.Match(data, @"^sup:close:([tc]):(\d+)$");
        if (!match.Success)
        {
            await AnswerAsync(token, callbackId, "", ct);
            return;
        }
        var id = int.Parse(match.Groups[2].Value);
        string answer;
        if (match.Groups[1].Value == "t")
        {
            var ticket = _store.GetTicket(id);
            answer = ticket is null ? "این تیکت یافت نشد."
                : ticket.Status == TicketStatus.Closed ? "این تیکت قبلاً بسته شده است."
                : _store.SetTicketStatus(id, TicketStatus.Closed) ? "🔒 تیکت بسته شد." : "بستن تیکت انجام نشد.";
        }
        else
        {
            var conversation = _store.GetConversation(id);
            answer = conversation is null ? "این گفت‌وگو یافت نشد."
                : conversation.Status == ConversationStatus.Closed ? "این گفت‌وگو قبلاً بسته شده است."
                : _store.CloseConversation(id) ? "🔒 گفت‌وگو بسته شد." : "بستن گفت‌وگو انجام نشد.";
        }
        await AnswerAsync(token, callbackId, answer, ct);
        if (chatId is long chatValue && messageId is int message)
            await PostAsync(token, "editMessageReplyMarkup", new Dictionary<string, string>
            {
                ["chat_id"] = chatValue.ToString(),
                ["message_id"] = message.ToString(),
                ["reply_markup"] = JsonSerializer.Serialize(new
                {
                    inline_keyboard = new[] { new object[] { new { text = "🔒 بسته شد", callback_data = $"{ClosedPrefix}{id}" } } },
                }),
            }, ct);
    }

    // ── Setup ───────────────────────────────────────────────────────────────────────────────────────────────

    public Task<(bool Ok, string? Username, string? Error)> CheckTokenAsync(string token, CancellationToken ct = default) =>
        TelegramTokenCheck.CheckAsync(_httpFactory, token, _logger, ct);

    public async Task<(bool Ok, string? Error)> SendTestAsync(CancellationToken ct = default)
    {
        var s = _store.GetTelegramSettings();
        var token = (s.SupportBotToken ?? "").Trim();
        var chat = (s.SupportChatId ?? "").Trim();
        if (token.Length == 0) return (false, "توکنی ذخیره نشده است.");
        if (!Regex.IsMatch(chat, @"^-?\d{1,32}$")) return (false, "شناسه‌ی گروه را وارد کنید (با دستور /id داخل گروه).");
        try
        {
            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(15);
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["chat_id"] = chat,
                ["text"] = "✅ ربات پشتیبانی فونیکس به این گروه وصل است. تیکت‌ها و گفت‌وگوهای آنلاین اینجا ارسال می‌شوند؛ برای پاسخ، روی پیام «Reply» بزنید.",
            });
            using var resp = await http.PostAsync($"https://api.telegram.org/bot{token}/sendMessage", form, ct);
            if (resp.IsSuccessStatusCode) return (true, null);
            var body = await resp.Content.ReadAsStringAsync(ct);
            string? description = null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                description = doc.RootElement.GetProperty("description").GetString();
            }
            catch { /* not JSON */ }
            return (false, $"تلگرام خطا داد ({(int)resp.StatusCode}): {description ?? body}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Support bot test message failed");
            return (false, "ارتباط با تلگرام برقرار نشد. کمی بعد دوباره امتحان کنید.");
        }
    }

    // ── Telegram calls ──────────────────────────────────────────────────────────────────────────────────────

    private async Task<int?> SendAsync(string token, string chatId, string html, string? markup, int? replyTo, CancellationToken ct)
    {
        var fields = new Dictionary<string, string>
        {
            ["chat_id"] = chatId,
            ["text"] = html,
            ["parse_mode"] = "HTML",
            ["disable_web_page_preview"] = "true",
        };
        if (markup is not null) fields["reply_markup"] = markup;
        if (replyTo is int r && r > 0)
        {
            fields["reply_to_message_id"] = r.ToString();
            fields["allow_sending_without_reply"] = "true";
        }
        var body = await PostAsync(token, "sendMessage", fields, ct);
        if (body is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("result", out var res) && res.TryGetProperty("message_id", out var m)
                   && m.TryGetInt32(out var id) ? id : null;
        }
        catch (JsonException) { return null; }
    }

    private async Task AnswerAsync(string token, string callbackId, string text, CancellationToken ct)
    {
        if (callbackId.Length == 0) return;
        var fields = new Dictionary<string, string> { ["callback_query_id"] = callbackId };
        if (text.Length > 0) fields["text"] = text;
        await PostAsync(token, "answerCallbackQuery", fields, ct);
    }

    // The response body on success, null on failure (logged).
    private async Task<string?> PostAsync(string token, string method, Dictionary<string, string> fields, CancellationToken ct)
    {
        try
        {
            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(20);
            using var form = new FormUrlEncodedContent(fields);
            using var resp = await http.PostAsync($"https://api.telegram.org/bot{token}/{method}", form, ct);
            if (resp.IsSuccessStatusCode) return await resp.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("Support bot {Method} failed: {Status}", method, (int)resp.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Support bot {Method} call failed", method);
            return null;
        }
    }
}

// Polls the support bot while it is switched on — only on the node serving traffic. A Standby has the same
// settings by sync; if it polled too, Telegram would hand each answer to whichever node asked first, and an
// answer read on the read-only Standby could not even be saved.
public sealed class TelegramSupportBotWorker : BackgroundService
{
    private readonly ITelegramSupportBot _bot;
    private readonly IClusterSyncService? _cluster;
    private readonly ILogger<TelegramSupportBotWorker> _logger;
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(10);
    private long _offset;

    public TelegramSupportBotWorker(ITelegramSupportBot bot, ILogger<TelegramSupportBotWorker> logger, IClusterSyncService? cluster = null)
    {
        _bot = bot;
        _cluster = cluster;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_cluster?.Role is ClusterRole.Standby or ClusterRole.Recovering || !_bot.IsActive)
                {
                    await Task.Delay(IdleDelay, stoppingToken);
                    continue;
                }
                var next = await _bot.ProcessUpdatesAsync(_offset, stoppingToken);
                if (next == _offset) await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                else _offset = next;
            }
            catch (TaskCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Support bot poll cycle failed");
                try { await Task.Delay(ErrorDelay, stoppingToken); }
                catch (TaskCanceledException) { break; }
            }
        }
    }
}

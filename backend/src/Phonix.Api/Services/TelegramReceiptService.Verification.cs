using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Phonix.Api.Models;

namespace Phonix.Api.Services;

// Identity verification over the receipt bot: a customer's bank card (level 1) and national-ID documents
// (level 2) are posted to the same admin chat as deposit receipts, with the same «تأیید / رد» buttons and the
// same two-step rejection — a tap on «رد» asks for the reason, the admin answers in a reply, and that text
// reaches the customer in full (in-site and by email).
//
// It lives on the receipt bot rather than a bot of its own because Telegram hands a bot's updates to exactly
// one poller: a second service reading the same token would steal the receipt bot's button taps.
//
// Decisions go through the same store calls and emails as the panel (SetCardStatus / SetKycStatus, then
// CardDecidedAsync / KycDecidedAsync), so approving here raises the customer's verification level exactly as
// approving in the panel does.
public sealed partial class TelegramReceiptService
{
    private const string CardApprovePrefix = "card:ok:";
    private const string CardRejectPrefix = "card:no:";
    private const string KycApprovePrefix = "kyc:ok:";
    private const string KycRejectPrefix = "kyc:no:";
    // The static post-decision button, like DecidedPrefix for receipts: a tap is acknowledged and nothing else.
    private const string VerificationDonePrefix = "vrf:done:";

    private enum VerificationKind { Card, Kyc }

    public async Task NotifyCardAsync(BankCard card, CancellationToken ct = default)
    {
        try
        {
            if (ActiveConfig() is not { } cfg) return;
            var (token, chatId) = cfg;
            var markup = DecisionMarkup(CardApprovePrefix, CardRejectPrefix, card.Id);
            var photo = OpenProtected("cards", card.CardImage);
            int? messageId;
            if (photo is not null)
            {
                await using (photo.Content)
                    messageId = await SendPhotoAsync(token, chatId, photo, BuildCardCaption(card), markup, ct);
            }
            else
            {
                messageId = await SendMessageAsync(token, chatId, BuildCardCaption(card), markup, ct);
            }
            if (messageId is int sent && long.TryParse(chatId, out var chat))
                _store.SetCardTelegramMessage(card.Id, chat, sent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram card notification failed for card #{CardId}", card.Id);
        }
    }

    // Two pictures can't carry buttons together, so the national card and the selfie go up as an album and the
    // details + buttons follow as a reply to it.
    public async Task NotifyKycAsync(KycRequest kyc, CancellationToken ct = default)
    {
        try
        {
            if (ActiveConfig() is not { } cfg) return;
            var (token, chatId) = cfg;
            var photos = new List<(StoredFile File, string Label)>();
            if (OpenProtected("kyc", kyc.CardImage) is { } card) photos.Add((card, "🪪 کارت ملی"));
            if (OpenProtected("kyc", kyc.SelfieImage) is { } selfie && kyc.SelfieImage != kyc.CardImage) photos.Add((selfie, "🤳 سلفی با کارت ملی"));
            int? albumId = null;
            try
            {
                if (photos.Count > 0) albumId = await SendAlbumAsync(token, chatId, photos, ct);
            }
            finally
            {
                foreach (var p in photos) await p.File.Content.DisposeAsync();
            }
            var messageId = await SendMessageAsync(token, chatId, BuildKycCaption(kyc), DecisionMarkup(KycApprovePrefix, KycRejectPrefix, kyc.Id), ct, albumId);
            if (messageId is int sent && long.TryParse(chatId, out var chat))
                _store.SetKycTelegramMessage(kyc.Id, chat, sent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram KYC notification failed for request #{KycId}", kyc.Id);
        }
    }

    // ── Decisions ──────────────────────────────────────────────────────────────────────────────────────────

    private static (VerificationKind Kind, bool Approve, int Id)? ParseVerificationCallback(string data)
    {
        foreach (var (prefix, kind, approve) in new[]
                 {
                     (CardApprovePrefix, VerificationKind.Card, true), (CardRejectPrefix, VerificationKind.Card, false),
                     (KycApprovePrefix, VerificationKind.Kyc, true), (KycRejectPrefix, VerificationKind.Kyc, false),
                 })
        {
            if (data.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(data[prefix.Length..], out var id))
                return (kind, approve, id);
        }
        return null;
    }

    private KycRequest? FindKyc(int id) => _store.GetAllKyc().FirstOrDefault(k => k.Id == id);

    private static string AlreadyDecided(BankCard card) =>
        DecisionVia.AlreadyDecided("کارت بانکی", DecisionVia.Outcome(card.Status), DecisionVia.Describe(card.DecidedVia, card.DecidedBy));

    private static string AlreadyDecided(KycRequest kyc) =>
        DecisionVia.AlreadyDecided("درخواست احراز هویت", DecisionVia.Outcome(kyc.Status), DecisionVia.Describe(kyc.DecidedVia, kyc.DecidedBy));

    // Why this request can't be decided here any more, or null while it is still waiting for a decision.
    private string? AlreadyDecidedOrMissing(VerificationKind kind, int id)
    {
        if (kind == VerificationKind.Card)
            return _store.GetCard(id) is not { } card ? "این درخواست یافت نشد."
                : card.Status == BankCardStatus.Pending ? null : AlreadyDecided(card);
        return FindKyc(id) is not { } kyc ? "این درخواست یافت نشد."
            : kyc.Status == KycStatus.Pending ? null : AlreadyDecided(kyc);
    }

    public async Task ShowCardDecisionAsync(BankCard card, CancellationToken ct = default)
    {
        try
        {
            if (card.TelegramChatId is not long chat || card.TelegramMessageId is not int message) return;
            if (ActiveConfig() is not { } cfg) return;
            await EditVerificationDecidedAsync(cfg.token, chat, message, VerificationKind.Card, card.Id, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram card message update failed for card #{CardId}", card.Id);
        }
    }

    public async Task ShowKycDecisionAsync(KycRequest kyc, CancellationToken ct = default)
    {
        try
        {
            if (kyc.TelegramChatId is not long chat || kyc.TelegramMessageId is not int message) return;
            if (ActiveConfig() is not { } cfg) return;
            await EditVerificationDecidedAsync(cfg.token, chat, message, VerificationKind.Kyc, kyc.Id, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram KYC message update failed for request #{KycId}", kyc.Id);
        }
    }

    // Handles a tap on a card or KYC message. Returns false when the callback isn't one of ours, so the receipt
    // handling can take it. Called only after the chat has been authorized.
    private async Task<bool> TryHandleVerificationCallbackAsync(string token, string callbackId, string data,
        long? chatId, int? messageId, string? by, CancellationToken ct)
    {
        if (data.StartsWith(VerificationDonePrefix, StringComparison.Ordinal))
        {
            await AnswerCallbackAsync(token, callbackId, "", ct);
            return true;
        }
        if (ParseVerificationCallback(data) is not { } parsed) return false;
        var (kind, approve, id) = parsed;

        // Decided already — here or on the site. That decision stands; say where it was made.
        if (AlreadyDecidedOrMissing(kind, id) is { } done)
        {
            await AnswerCallbackAsync(token, callbackId, done, ct);
            if (chatId is not null && messageId is not null)
                await EditVerificationDecidedAsync(token, chatId.Value, messageId.Value, kind, id, ct);
            return true;
        }

        if (!approve)
        {
            if (chatId is not null && messageId is not null)
                await SendVerificationReasonPromptAsync(token, chatId.Value, messageId.Value, kind, id, ct);
            await AnswerCallbackAsync(token, callbackId, "✍️ دلیل رد را در «پاسخ» به پیام ارسال کنید.", ct);
            return true;
        }

        if (Decide(kind, id, approve: true, reason: null, by) is { } refused)
        {
            await AnswerCallbackAsync(token, callbackId, refused, ct);
            if (chatId is not null && messageId is not null)
                await EditVerificationDecidedAsync(token, chatId.Value, messageId.Value, kind, id, ct);
            return true;
        }
        _logger.LogInformation("Telegram verification decision: {Kind} #{Id} → Approved", kind, id);
        await AnswerCallbackAsync(token, callbackId, "✅ تأیید شد.", ct);
        if (chatId is not null && messageId is not null)
            await EditVerificationDecidedAsync(token, chatId.Value, messageId.Value, kind, id, ct);
        return true;
    }

    // The same transition and email the panel's Decide performs, through the store's Pending-only decision.
    // Null when it was applied; otherwise why not (gone, or decided a moment ago — possibly on the site).
    private string? Decide(VerificationKind kind, int id, bool approve, string? reason, string? by)
    {
        if (kind == VerificationKind.Card)
        {
            var card = _store.DecideCard(id, approve ? BankCardStatus.Approved : BankCardStatus.Rejected, reason, DecisionVia.Telegram, by);
            if (card.Item is null) return "این درخواست یافت نشد.";
            if (!card.Applied) return AlreadyDecided(card.Item);
            _ = _mailer.CardDecidedAsync(card.Item);
            return null;
        }
        var kyc = _store.DecideKyc(id, approve ? KycStatus.Approved : KycStatus.Rejected, reason, DecisionVia.Telegram, by);
        if (kyc.Item is null) return "این درخواست یافت نشد.";
        if (!kyc.Applied) return AlreadyDecided(kyc.Item);
        _ = _mailer.KycDecidedAsync(kyc.Item);
        return null;
    }

    // «#CARD-REJ:<id>:<messageId>» / «#KYC-REJ:<id>:<messageId>» in the reason prompt ties the admin's reply back
    // to the request and to the message to rewrite, without keeping any state. (The receipt marker is «#REJ:»,
    // which neither of these contains.)
    private static string VerificationMarker(VerificationKind kind, int id, int messageId) =>
        $"#{(kind == VerificationKind.Card ? "CARD" : "KYC")}-REJ:{id}:{messageId}";

    private static (VerificationKind Kind, int Id, int MessageId)? ParseVerificationMarker(string text)
    {
        var m = System.Text.RegularExpressions.Regex.Match(text, @"#(CARD|KYC)-REJ:(\d+):(\d+)");
        if (!m.Success) return null;
        return (m.Groups[1].Value == "CARD" ? VerificationKind.Card : VerificationKind.Kyc,
            int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
    }

    private async Task SendVerificationReasonPromptAsync(string token, long chatId, int messageId, VerificationKind kind, int id, CancellationToken ct)
    {
        var what = kind == VerificationKind.Card ? "کارت بانکی" : "مدارک هویتی";
        var forceReply = JsonSerializer.Serialize(new { force_reply = true, input_field_placeholder = $"دلیل رد {what}..." });
        await PostFormAsync(token, "sendMessage", new Dictionary<string, string>
        {
            ["chat_id"] = chatId.ToString(),
            ["text"] = $"✍️ لطفاً دلیل رد این {what} را در «پاسخ» به همین پیام بنویسید. همین متن برای کاربر ارسال می‌شود.\n{VerificationMarker(kind, id, messageId)}",
            ["reply_to_message_id"] = messageId.ToString(),
            ["reply_markup"] = forceReply,
        }, ct);
    }

    // The admin's typed reason, already authorized and non-empty (see HandleReasonReplyAsync).
    private async Task RejectVerificationAsync(string token, long chatId, (VerificationKind Kind, int Id, int MessageId) target,
        string reason, string? by, CancellationToken ct)
    {
        var (kind, id, messageId) = target;
        if (Decide(kind, id, approve: false, reason, by) is { } refused)
        {
            await SendMessageAsync(token, chatId.ToString(), refused, "", ct);
            if (messageId > 0) await EditVerificationDecidedAsync(token, chatId, messageId, kind, id, ct);
            return;
        }
        _logger.LogInformation("Telegram verification decision: {Kind} #{Id} → Rejected", kind, id);
        if (messageId > 0) await EditVerificationDecidedAsync(token, chatId, messageId, kind, id, ct);
        await SendMessageAsync(token, chatId.ToString(), "❌ رد شد و دلیل برای کاربر ارسال شد.", "", ct);
    }

    // Rewrites the reviewed message to its outcome (and the reason, on a rejection) and swaps the two buttons for
    // one static status button. A card usually went out as a photo (its caption is edited); a KYC request, and a
    // card whose picture couldn't be read, went out as text — so the caption edit falls back to a text edit.
    private async Task EditVerificationDecidedAsync(string token, long chatId, int messageId, VerificationKind kind, int id, CancellationToken ct)
    {
        string body; bool approved; string? reason; string via;
        if (kind == VerificationKind.Card)
        {
            if (_store.GetCard(id) is not { } card) return;
            body = BuildCardCaption(card); approved = card.Status == BankCardStatus.Approved; reason = card.RejectionReason;
            via = DecisionVia.Describe(card.DecidedVia, card.DecidedBy);
        }
        else
        {
            if (FindKyc(id) is not { } kyc) return;
            body = BuildKycCaption(kyc); approved = kyc.Status == KycStatus.Approved; reason = kyc.RejectionReason;
            via = DecisionVia.Describe(kyc.DecidedVia, kyc.DecidedBy);
        }
        var outcome = approved ? "✅ تأیید شد" : "❌ رد شد";
        var reasonLine = !approved && !string.IsNullOrWhiteSpace(reason) ? $"\n<b>📝 دلیل رد:</b> {Esc(reason!)}" : "";
        var text = $"{body}\n\n<b>وضعیت: {outcome}{(via.Length > 0 ? " — " + Esc(via) : "")}</b>{reasonLine}";
        var markup = JsonSerializer.Serialize(new
        {
            inline_keyboard = new[] { new object[] { new { text = outcome, callback_data = VerificationDonePrefix + id } } },
        });
        var fields = new Dictionary<string, string>
        {
            ["chat_id"] = chatId.ToString(),
            ["message_id"] = messageId.ToString(),
            ["parse_mode"] = "HTML",
            ["reply_markup"] = markup,
        };
        if (await PostFormOkAsync(token, "editMessageCaption", new Dictionary<string, string>(fields) { ["caption"] = text }, ct)) return;
        await PostFormAsync(token, "editMessageText", new Dictionary<string, string>(fields) { ["text"] = text, ["disable_web_page_preview"] = "true" }, ct);
    }

    // ── Captions ───────────────────────────────────────────────────────────────────────────────────────────

    private static string DecisionMarkup(string approvePrefix, string rejectPrefix, int id) =>
        JsonSerializer.Serialize(new
        {
            inline_keyboard = new[]
            {
                new object[]
                {
                    new { text = "✅ تأیید", callback_data = approvePrefix + id },
                    new { text = "❌ رد", callback_data = rejectPrefix + id },
                },
            },
        });

    private string UserBlock(int userId)
    {
        var user = _store.GetUser(userId);
        var sb = new StringBuilder();
        sb.AppendLine("<blockquote>مشخصات کاربر");
        sb.AppendLine($"▫️آیدی کاربر: {Esc(user?.Code is { Length: > 0 } code ? code : userId.ToString())}");
        sb.AppendLine($"👨‍💼اسم کاربر: {Esc(Dash(user?.Name))}");
        sb.AppendLine($"⚡️ نام کاربری: {Esc(Dash(user?.Username))}");
        sb.AppendLine($"📞 شماره تماس: {Esc(Dash(user?.Phone))}");
        sb.AppendLine($"📨 ایمیل: {Esc(Dash(user?.Email))}</blockquote>");
        return sb.ToString();
    }

    private string BuildCardCaption(BankCard card)
    {
        var sb = new StringBuilder();
        sb.AppendLine("❗️|🪪 احراز هویت سطح ۱ ( کارت بانکی )");
        sb.AppendLine();
        sb.Append(UserBlock(card.UserId));
        sb.AppendLine();
        sb.AppendLine("<blockquote>اطلاعات کارت");
        sb.AppendLine($"💳 شماره کارت: {FormatCard(card.CardNumber)}");
        sb.AppendLine($"👤 نام روی کارت: {Esc(Dash(card.HolderName))}");
        if (!string.IsNullOrWhiteSpace(card.Bank)) sb.AppendLine($"🏦 بانک: {Esc(card.Bank)}");
        sb.AppendLine("</blockquote>");
        sb.AppendLine("⚠️ عکس باید از نسخه‌ی فیزیکی کارت باشد و نام روی کارت با صاحب حساب یکی باشد.");
        sb.Append(JalaliDate.NowStamp());
        return sb.ToString();
    }

    private string BuildKycCaption(KycRequest kyc)
    {
        var sb = new StringBuilder();
        sb.AppendLine("❗️|🛂 احراز هویت سطح ۲ ( مدارک هویتی )");
        sb.AppendLine();
        sb.Append(UserBlock(kyc.UserId));
        sb.AppendLine();
        sb.AppendLine("<blockquote>اطلاعات هویتی");
        sb.AppendLine($"👤 نام کامل: {Esc(Dash(kyc.FullName))}");
        sb.AppendLine($"🆔 کد ملی: {Esc(Dash(kyc.NationalId))}");
        sb.AppendLine($"🎂 تاریخ تولد: {Esc(Dash(kyc.BirthDate))}");
        sb.AppendLine("</blockquote>");
        sb.AppendLine("🖼 تصاویر مدارک در پیام بالا هستند.");
        sb.Append(JalaliDate.NowStamp());
        return sb.ToString();
    }

    // ── Telegram calls specific to verification ───────────────────────────────────────────────────────────

    private StoredFile? OpenProtected(string category, string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        try { return _files.Open(category, id.Trim()); }
        catch { return null; }
    }

    // One picture goes up with sendPhoto, two as an album; returns the first message's id so the details can
    // be sent as a reply to it, or null when Telegram refused.
    private async Task<int?> SendAlbumAsync(string token, string chatId, List<(StoredFile File, string Label)> photos, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent { { new StringContent(chatId), "chat_id" } };
        string method;
        if (photos.Count == 1)
        {
            method = "sendPhoto";
            form.Add(new StringContent(photos[0].Label), "caption");
            form.Add(await PhotoContentAsync(photos[0].File, ct), "photo", "photo.jpg");
        }
        else
        {
            method = "sendMediaGroup";
            var media = photos.Select((p, i) => new { type = "photo", media = $"attach://p{i}", caption = p.Label }).ToArray();
            form.Add(new StringContent(JsonSerializer.Serialize(media)), "media");
            for (var i = 0; i < photos.Count; i++)
                form.Add(await PhotoContentAsync(photos[i].File, ct), $"p{i}", $"p{i}.jpg");
        }

        using var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);
        using var resp = await http.PostAsync($"https://api.telegram.org/bot{token}/{method}", form, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("Telegram {Method} failed: {Status}", method, (int)resp.StatusCode);
            return null;
        }
        using var doc = JsonDocument.Parse(body);
        var result = doc.RootElement.GetProperty("result");
        var first = result.ValueKind == JsonValueKind.Array ? result[0] : result;
        return first.TryGetProperty("message_id", out var mid) && mid.TryGetInt32(out var id) ? id : null;
    }

    private static async Task<ByteArrayContent> PhotoContentAsync(StoredFile file, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await file.Content.CopyToAsync(ms, ct);
        var content = new ByteArrayContent(ms.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(file.ContentType) ? "image/jpeg" : file.ContentType);
        return content;
    }

    // Like PostFormAsync, but says whether Telegram accepted the call.
    private async Task<bool> PostFormOkAsync(string token, string method, Dictionary<string, string> fields, CancellationToken ct)
    {
        try
        {
            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(20);
            using var form = new FormUrlEncodedContent(fields);
            using var resp = await http.PostAsync($"https://api.telegram.org/bot{token}/{method}", form, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram {Method} call failed", method);
            return false;
        }
    }
}

using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using System.Text;
using MimeKit;
using MimeKit.Text;
using Phonix.Api.Data;
using Phonix.Api.Models;

namespace Phonix.Api.Services;

// Reads and answers the shop's inbound mailbox over IMAP/SMTP.
//
// Connection policy: one short-lived connection per request, not a pooled long-lived one. The panel is used
// by a handful of staff a few times an hour, so pooling would buy nothing measurable, while a socket held
// open across requests is a socket that goes stale on a Dovecot restart or a NAT timeout and then fails the
// NEXT request for reasons the operator cannot see. Connecting per call is boring and always correct.
//
// Every public method returns MailResult rather than throwing: see IMailboxService for why.
public sealed class MailboxService : IMailboxService
{
    private const int MaxPageSize = 100;
    // A body far past this is a mail bomb or a mailing-list digest with the whole month inline; either way it
    // is not something to push into a browser. The cap is on the rendered body only, never on attachments.
    private const int MaxBodyChars = 400_000;

    private readonly IDataStore _store;
    private readonly ILogger<MailboxService> _logger;

    public MailboxService(IDataStore store, ILogger<MailboxService> logger)
    {
        _store = store;
        _logger = logger;
    }

    // ── Connection plumbing ─────────────────────────────────────────────────────────────────────────

    private MailboxSettings? Configured()
    {
        var s = _store.GetMailboxSettings();
        if (!s.Enabled) return null;
        if (string.IsNullOrWhiteSpace(s.ImapHost) || string.IsNullOrWhiteSpace(s.Username)) return null;
        return s;
    }

    private async Task<MailResult<T>> WithImapAsync<T>(Func<ImapClient, CancellationToken, Task<MailResult<T>>> work, CancellationToken ct)
    {
        var settings = Configured();
        if (settings is null)
            return MailResult<T>.Fail("صندوق ورودی پیکربندی یا فعال نشده است. از تنظیمات صندوق، اطلاعات IMAP را وارد کنید.");

        using var client = new ImapClient();
        try
        {
            var security = settings.ImapUseSsl
                ? MailKit.Security.SecureSocketOptions.SslOnConnect
                : MailKit.Security.SecureSocketOptions.StartTlsWhenAvailable;
            await client.ConnectAsync(settings.ImapHost, settings.ImapPort, security, ct);
            await client.AuthenticateAsync(settings.Username, settings.Password, ct);

            var result = await work(client, ct);

            await client.DisconnectAsync(true, ct);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (MailKit.Security.AuthenticationException)
        {
            // Called out separately because it is the single most common setup mistake, and "authentication
            // failed" is the one error where the operator knows exactly what to do next.
            return MailResult<T>.Fail("نام کاربری یا گذرواژه صندوق پذیرفته نشد.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IMAP operation failed against {Host}:{Port}", settings.ImapHost, settings.ImapPort);
            return MailResult<T>.Fail($"اتصال به سرور ایمیل ممکن نشد: {ex.Message}");
        }
    }

    // ── Folders ─────────────────────────────────────────────────────────────────────────────────────

    // Special-use flags are how a server declares "this is the Trash", and they are what Dovecot sets. Name
    // matching is only the fallback, because folder names are localized and vary between servers.
    private static (string Kind, string Title) Classify(IMailFolder folder)
    {
        if (folder.Attributes.HasFlag(FolderAttributes.Inbox)) return ("inbox", "صندوق ورودی");
        if (folder.Attributes.HasFlag(FolderAttributes.Sent)) return ("sent", "ارسال‌شده");
        if (folder.Attributes.HasFlag(FolderAttributes.Drafts)) return ("drafts", "پیش‌نویس");
        if (folder.Attributes.HasFlag(FolderAttributes.Trash)) return ("trash", "زباله‌دان");
        if (folder.Attributes.HasFlag(FolderAttributes.Junk)) return ("spam", "هرزنامه");
        if (folder.Attributes.HasFlag(FolderAttributes.Archive)) return ("archive", "بایگانی");

        return folder.Name.ToLowerInvariant() switch
        {
            "inbox" => ("inbox", "صندوق ورودی"),
            "sent" or "sent items" or "sent messages" => ("sent", "ارسال‌شده"),
            "drafts" => ("drafts", "پیش‌نویس"),
            "trash" or "deleted items" => ("trash", "زباله‌دان"),
            "junk" or "spam" => ("spam", "هرزنامه"),
            "archive" => ("archive", "بایگانی"),
            _ => ("other", folder.Name),
        };
    }

    public Task<MailResult<IReadOnlyList<MailFolderInfo>>> GetFoldersAsync(CancellationToken ct = default) =>
        WithImapAsync<IReadOnlyList<MailFolderInfo>>(async (client, token) =>
        {
            var list = new List<MailFolderInfo>();

            foreach (var folder in await EnumerateFoldersAsync(client, token))
            {
                // \Noselect folders are containers in the hierarchy, not mailboxes — opening one throws.
                if (folder.Attributes.HasFlag(FolderAttributes.NonExistent) ||
                    folder.Attributes.HasFlag(FolderAttributes.NoSelect)) continue;

                try
                {
                    await folder.StatusAsync(StatusItems.Count | StatusItems.Unread, token);
                    var (kind, title) = Classify(folder);
                    list.Add(new MailFolderInfo(folder.FullName, title, kind, folder.Count, folder.Unread));
                }
                catch (Exception ex)
                {
                    // One unreadable folder must not cost the operator the whole rail.
                    _logger.LogWarning(ex, "Skipping unreadable folder {Folder}", folder.FullName);
                }
            }

            // Inbox first, then the other well-known folders in a familiar order, then everything else.
            var order = new[] { "inbox", "sent", "drafts", "archive", "spam", "trash", "other" };
            return MailResult<IReadOnlyList<MailFolderInfo>>.Success(
                list.OrderBy(f => Array.IndexOf(order, f.Kind)).ThenBy(f => f.Title).ToList());
        }, ct);

    private static async Task<IReadOnlyList<IMailFolder>> EnumerateFoldersAsync(ImapClient client, CancellationToken ct)
    {
        var folders = new List<IMailFolder> { client.Inbox };
        foreach (var ns in client.PersonalNamespaces)
            folders.AddRange((await client.GetFoldersAsync(ns, false, ct)).Where(f => f.FullName != client.Inbox.FullName));
        return folders;
    }

    private static async Task<IMailFolder?> OpenAsync(ImapClient client, string name, FolderAccess access, CancellationToken ct)
    {
        var folder = string.IsNullOrWhiteSpace(name) || name.Equals("INBOX", StringComparison.OrdinalIgnoreCase)
            ? client.Inbox
            : (await EnumerateFoldersAsync(client, ct)).FirstOrDefault(f => f.FullName == name);
        if (folder is null) return null;
        await folder.OpenAsync(access, ct);
        return folder;
    }

    // ── Listing ─────────────────────────────────────────────────────────────────────────────────────

    public Task<MailResult<MailPage>> ListAsync(string folder, int page, int pageSize, string? search, bool unreadOnly, CancellationToken ct = default) =>
        WithImapAsync(async (client, token) =>
        {
            var box = await OpenAsync(client, folder, FolderAccess.ReadOnly, token);
            if (box is null) return MailResult<MailPage>.Fail("این پوشه پیدا نشد.");

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

            // Searching is pushed to the SERVER rather than done by fetching everything and filtering here:
            // a mailbox with tens of thousands of messages would otherwise pull every header on every
            // keystroke. Dovecot indexes these queries.
            var query = BuildQuery(search, unreadOnly);
            var uids = query is null
                ? (await box.SearchAsync(SearchQuery.All, token))
                : (await box.SearchAsync(query, token));

            // Newest first, which is the only order an inbox is ever read in.
            var ordered = uids.OrderByDescending(u => u.Id).ToList();
            var total = ordered.Count;
            var slice = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            var items = new List<MailSummary>(slice.Count);
            if (slice.Count > 0)
            {
                var summaries = await box.FetchAsync(
                    slice,
                    MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.Flags |
                    MessageSummaryItems.BodyStructure | MessageSummaryItems.PreviewText,
                    token);

                // FetchAsync does not promise the requested order back, so re-impose it.
                var bySlot = summaries.ToDictionary(s => s.UniqueId);
                foreach (var uid in slice)
                    if (bySlot.TryGetValue(uid, out var s))
                        items.Add(ToSummary(s));
            }

            var validity = box.UidValidity;
            await box.CloseAsync(false, token);
            return MailResult<MailPage>.Success(new MailPage(items, total, page, pageSize, validity));
        }, ct);

    private static SearchQuery? BuildQuery(string? search, bool unreadOnly)
    {
        SearchQuery? query = unreadOnly ? SearchQuery.NotSeen : null;

        var term = (search ?? "").Trim();
        if (term.Length > 0)
        {
            // Matches the way a person looks for a mail: by who sent it, or by a word in the subject or body.
            var text = SearchQuery.SubjectContains(term)
                .Or(SearchQuery.FromContains(term))
                .Or(SearchQuery.ToContains(term))
                .Or(SearchQuery.BodyContains(term));
            query = query is null ? text : query.And(text);
        }

        return query;
    }

    private static MailSummary ToSummary(IMessageSummary s)
    {
        var env = s.Envelope;
        var flags = s.Flags ?? MessageFlags.None;
        return new MailSummary(
            Uid: s.UniqueId.Id,
            Subject: Clean(env?.Subject) is { Length: > 0 } subj ? subj : "(بدون موضوع)",
            From: FirstAddress(env?.From),
            To: Addresses(env?.To),
            Date: env?.Date ?? s.InternalDate ?? DateTimeOffset.MinValue,
            Preview: Truncate(Clean(s.PreviewText), 200),
            Seen: flags.HasFlag(MessageFlags.Seen),
            Flagged: flags.HasFlag(MessageFlags.Flagged),
            Answered: flags.HasFlag(MessageFlags.Answered),
            HasAttachments: s.Attachments?.Any() == true);
    }

    // ── Single message ──────────────────────────────────────────────────────────────────────────────

    public Task<MailResult<MailMessageDetail>> GetAsync(string folder, uint uid, CancellationToken ct = default) =>
        WithImapAsync(async (client, token) =>
        {
            var box = await OpenAsync(client, folder, FolderAccess.ReadOnly, token);
            if (box is null) return MailResult<MailMessageDetail>.Fail("این پوشه پیدا نشد.");

            var message = await box.GetMessageAsync(new UniqueId(uid), token);
            if (message is null) return MailResult<MailMessageDetail>.Fail("این پیام پیدا نشد.");

            var summaries = await box.FetchAsync(new[] { new UniqueId(uid) }, MessageSummaryItems.Flags, token);
            var flags = summaries.FirstOrDefault()?.Flags ?? MessageFlags.None;

            var (html, hadRemote) = MailHtmlSanitizer.Sanitize(message.HtmlBody);
            var attachments = ExtractAttachments(message);

            await box.CloseAsync(false, token);

            return MailResult<MailMessageDetail>.Success(new MailMessageDetail(
                Uid: uid,
                Subject: Clean(message.Subject) is { Length: > 0 } subj ? subj : "(بدون موضوع)",
                From: FirstAddress(message.From),
                To: Addresses(message.To),
                Cc: Addresses(message.Cc),
                Date: message.Date,
                TextBody: Truncate(Clean(message.TextBody), MaxBodyChars),
                HtmlBody: Truncate(html, MaxBodyChars),
                HadRemoteContent: hadRemote,
                Seen: flags.HasFlag(MessageFlags.Seen),
                Flagged: flags.HasFlag(MessageFlags.Flagged),
                MessageId: message.MessageId ?? "",
                References: string.Join(" ", message.References),
                Attachments: attachments));
        }, ct);

    private static List<MailAttachmentInfo> ExtractAttachments(MimeMessage message)
    {
        var attachments = new List<MailAttachmentInfo>();
        var index = 0;
        foreach (var part in message.BodyParts.OfType<MimePart>())
        {
            // Inline images are listed too: they are stripped out of the rendered body by the sanitizer, so
            // listing them is the only way the admin can still get at them.
            if (!part.IsAttachment && part.ContentDisposition?.Disposition != ContentDisposition.Inline) { index++; continue; }
            if (part is TextPart && !part.IsAttachment) { index++; continue; }

            attachments.Add(new MailAttachmentInfo(
                Index: index,
                FileName: SafeFileName(part.FileName, index),
                ContentType: part.ContentType?.MimeType ?? "application/octet-stream",
                Size: part.Content?.Stream?.Length ?? 0));
            index++;
        }
        return attachments;
    }

    // ── Conversations ───────────────────────────────────────────────────────────────────────────────
    // INBOX carries what customers send us; Sent carries our replies. A conversation is every message across
    // BOTH that shares one outside party and one normalized subject. Threading is done here, in the service,
    // rather than via the IMAP THREAD extension because THREAD is per-folder and cannot span INBOX+Sent.

    // Cap per folder so a mailbox that has been running for years cannot turn one page load into a fetch of
    // tens of thousands of envelopes. Newest wins when the cap bites — old threads drop off the bottom.
    private const int MaxScan = 1000;

    public Task<MailResult<MailConversationPage>> ListConversationsAsync(int page, int pageSize, string? search, bool unreadOnly, CancellationToken ct = default) =>
        WithImapAsync(async (client, token) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

            var rows = await ScanForThreadingAsync(client, token);
            var conversations = GroupIntoConversations(rows);

            var term = (search ?? "").Trim();
            IEnumerable<MailConversationSummary> filtered = conversations;
            if (unreadOnly) filtered = filtered.Where(c => c.Unread > 0);
            if (term.Length > 0)
                filtered = filtered.Where(c =>
                    c.Subject.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    c.Party.Address.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    c.Party.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    c.Preview.Contains(term, StringComparison.OrdinalIgnoreCase));

            var ordered = filtered.OrderByDescending(c => c.LastDate).ToList();
            var slice = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return MailResult<MailConversationPage>.Success(new MailConversationPage(slice, ordered.Count, page, pageSize));
        }, ct);

    public Task<MailResult<MailConversationDetail>> GetConversationAsync(string id, CancellationToken ct = default) =>
        WithImapAsync(async (client, token) =>
        {
            var rows = await ScanForThreadingAsync(client, token);
            var group = rows.Where(r => ConversationId(r) == id).OrderBy(r => r.Date).ToList();
            if (group.Count == 0) return MailResult<MailConversationDetail>.Fail("این گفتگو پیدا نشد.");

            var messages = new List<MailThreadMessage>(group.Count);
            IMailFolder? inbox = null;
            var toMarkSeen = new List<UniqueId>();

            foreach (var row in group)
            {
                var box = await OpenAsync(client, row.Folder, FolderAccess.ReadOnly, token);
                if (box is null) continue;
                var message = await box.GetMessageAsync(new UniqueId(row.Uid), token);
                var (html, hadRemote) = MailHtmlSanitizer.Sanitize(message.HtmlBody);

                messages.Add(new MailThreadMessage(
                    Folder: row.Folder,
                    Uid: row.Uid,
                    FromCustomer: row.FromCustomer,
                    From: FirstAddress(message.From),
                    To: Addresses(message.To),
                    Date: message.Date,
                    TextBody: Truncate(Clean(message.TextBody), MaxBodyChars),
                    HtmlBody: Truncate(html, MaxBodyChars),
                    HadRemoteContent: hadRemote,
                    Attachments: ExtractAttachments(message),
                    Seen: row.Seen));

                if (row.FromCustomer && !row.Seen) toMarkSeen.Add(new UniqueId(row.Uid));
                await box.CloseAsync(false, token);
            }

            // Opening a conversation marks the customer's messages read — same as Gmail. Best-effort: a failed
            // flag update must not fail the read.
            if (toMarkSeen.Count > 0)
            {
                inbox = await OpenAsync(client, "INBOX", FolderAccess.ReadWrite, token);
                if (inbox is not null)
                {
                    try { await inbox.AddFlagsAsync(toMarkSeen, MessageFlags.Seen, true, token); } catch { /* best effort */ }
                    await inbox.CloseAsync(false, token);
                }
            }

            var lastInbound = group.LastOrDefault(r => r.FromCustomer);
            var first = group[0];
            return MailResult<MailConversationDetail>.Success(new MailConversationDetail(
                Id: id,
                Subject: first.Subject.Length > 0 ? first.Subject : "(بدون موضوع)",
                Party: first.Party,
                ReplyFolder: lastInbound?.Folder,
                ReplyUid: lastInbound?.Uid,
                Messages: messages));
        }, ct);

    // One scanned message reduced to just what threading and the list row need.
    private sealed record ThreadRow(
        string Folder, uint Uid, bool FromCustomer, MailAddressInfo Party,
        string RawSubject, string Subject, DateTimeOffset Date, string Preview,
        bool Seen, bool Flagged, bool HasAttachments);

    private async Task<List<ThreadRow>> ScanForThreadingAsync(ImapClient client, CancellationToken ct)
    {
        var rows = new List<ThreadRow>();
        foreach (var (name, fromCustomer) in new[] { ("INBOX", true), ("Sent", false) })
        {
            var box = await OpenAsync(client, name, FolderAccess.ReadOnly, ct);
            if (box is null) continue;

            var uids = await box.SearchAsync(SearchQuery.All, ct);
            var recent = uids.OrderByDescending(u => u.Id).Take(MaxScan).ToList();
            if (recent.Count > 0)
            {
                var summaries = await box.FetchAsync(recent,
                    MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.Flags |
                    MessageSummaryItems.BodyStructure | MessageSummaryItems.PreviewText, ct);

                foreach (var s in summaries)
                {
                    var env = s.Envelope;
                    var flags = s.Flags ?? MessageFlags.None;
                    // The outside party is whoever is NOT us: the sender of an inbound message, the recipient
                    // of one we sent. That is what makes both halves of a thread group together.
                    var party = fromCustomer ? FirstAddress(env?.From) : FirstAddress(env?.To);
                    var rawSubject = Clean(env?.Subject);
                    rows.Add(new ThreadRow(
                        Folder: box.FullName,
                        Uid: s.UniqueId.Id,
                        FromCustomer: fromCustomer,
                        Party: party,
                        RawSubject: rawSubject,
                        Subject: NormalizeSubject(rawSubject),
                        Date: env?.Date ?? s.InternalDate ?? DateTimeOffset.MinValue,
                        Preview: Truncate(Clean(s.PreviewText), 200),
                        Seen: flags.HasFlag(MessageFlags.Seen),
                        Flagged: flags.HasFlag(MessageFlags.Flagged),
                        HasAttachments: s.Attachments?.Any() == true));
                }
            }
            await box.CloseAsync(false, ct);
        }
        return rows;
    }

    private static List<MailConversationSummary> GroupIntoConversations(List<ThreadRow> rows)
    {
        var groups = rows
            .Where(r => !string.IsNullOrEmpty(r.Party.Address))
            .GroupBy(ConversationId);

        var result = new List<MailConversationSummary>();
        foreach (var g in groups)
        {
            var ordered = g.OrderBy(r => r.Date).ToList();
            var last = ordered[^1];
            // A display subject/party taken from the newest message, so a renamed thread shows its latest form.
            result.Add(new MailConversationSummary(
                Id: g.Key,
                Subject: last.RawSubject.Length > 0 ? last.RawSubject : "(بدون موضوع)",
                Party: ordered.FirstOrDefault(r => r.FromCustomer)?.Party ?? last.Party,
                LastDate: last.Date,
                Count: ordered.Count,
                Unread: ordered.Count(r => r.FromCustomer && !r.Seen),
                Preview: last.Preview,
                HasAttachments: ordered.Any(r => r.HasAttachments),
                Flagged: ordered.Any(r => r.Flagged),
                LastFromCustomer: last.FromCustomer));
        }
        return result;
    }

    // Group key AND public id in one: the outside party's address plus the normalized subject, encoded so it
    // survives in a URL. Deterministic, so the detail call re-derives the same id from a fresh scan without
    // any stored state.
    private static string ConversationId(ThreadRow r)
    {
        var key = $"{r.Party.Address.ToLowerInvariant()}{r.Subject.ToLowerInvariant()}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(key)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    // Strips any run of reply/forward prefixes so "Re: Fwd: Order" and "Order" thread together. Covers the
    // English and Persian prefixes a customer's client is likely to prepend.
    private static string NormalizeSubject(string subject)
    {
        var t = subject.Trim();
        while (true)
        {
            var m = System.Text.RegularExpressions.Regex.Match(t, @"^(re|fwd|fw|aw|پاسخ|ارجاع)\s*:\s*",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) break;
            t = t[m.Length..].Trim();
        }
        return t;
    }

    public Task<MailResult<MailAttachmentContent>> GetAttachmentAsync(string folder, uint uid, int index, CancellationToken ct = default) =>
        WithImapAsync(async (client, token) =>
        {
            var box = await OpenAsync(client, folder, FolderAccess.ReadOnly, token);
            if (box is null) return MailResult<MailAttachmentContent>.Fail("این پوشه پیدا نشد.");

            var message = await box.GetMessageAsync(new UniqueId(uid), token);
            var parts = message.BodyParts.OfType<MimePart>().ToList();
            if (index < 0 || index >= parts.Count) return MailResult<MailAttachmentContent>.Fail("این پیوست پیدا نشد.");

            var part = parts[index];
            // A MIME part can legitimately carry no content (a malformed or truncated message); treating that
            // as "not found" is honest, whereas dereferencing it would surface as a 500.
            if (part.Content is null) return MailResult<MailAttachmentContent>.Fail("این پیوست محتوایی ندارد.");

            using var buffer = new MemoryStream();
            await part.Content.DecodeToAsync(buffer, token);

            await box.CloseAsync(false, token);
            return MailResult<MailAttachmentContent>.Success(new MailAttachmentContent(
                buffer.ToArray(),
                part.ContentType?.MimeType ?? "application/octet-stream",
                SafeFileName(part.FileName, index)));
        }, ct);

    // ── Flags and moves ─────────────────────────────────────────────────────────────────────────────

    public Task<MailResult> SetSeenAsync(string folder, uint uid, bool seen, CancellationToken ct = default) =>
        Unit(SetFlagAsync(folder, uid, MessageFlags.Seen, seen, ct));

    public Task<MailResult> SetFlaggedAsync(string folder, uint uid, bool flagged, CancellationToken ct = default) =>
        Unit(SetFlagAsync(folder, uid, MessageFlags.Flagged, flagged, ct));

    private Task<MailResult<bool>> SetFlagAsync(string folder, uint uid, MessageFlags flag, bool on, CancellationToken ct) =>
        WithImapAsync(async (client, token) =>
        {
            var box = await OpenAsync(client, folder, FolderAccess.ReadWrite, token);
            if (box is null) return MailResult<bool>.Fail("این پوشه پیدا نشد.");

            var ids = new[] { new UniqueId(uid) };
            if (on) await box.AddFlagsAsync(ids, flag, true, token);
            else await box.RemoveFlagsAsync(ids, flag, true, token);

            await box.CloseAsync(false, token);
            return MailResult<bool>.Success(true);
        }, ct);

    public Task<MailResult> MoveAsync(string folder, uint uid, string targetFolder, CancellationToken ct = default) =>
        Unit(WithImapAsync(async (client, token) =>
        {
            if (string.Equals(folder, targetFolder, StringComparison.Ordinal))
                return MailResult<bool>.Fail("پیام همین حالا در این پوشه است.");

            var source = await OpenAsync(client, folder, FolderAccess.ReadWrite, token);
            if (source is null) return MailResult<bool>.Fail("پوشه مبدأ پیدا نشد.");

            var target = (await EnumerateFoldersAsync(client, token))
                .FirstOrDefault(f => f.FullName == targetFolder && !f.Attributes.HasFlag(FolderAttributes.NoSelect));
            if (target is null)
            {
                await source.CloseAsync(false, token);
                return MailResult<bool>.Fail("پوشه مقصد پیدا نشد.");
            }

            await source.MoveToAsync(new UniqueId(uid), target, token);
            await source.CloseAsync(false, token);
            return MailResult<bool>.Success(true);
        }, ct));

    // ── Sending ─────────────────────────────────────────────────────────────────────────────────────

    public async Task<MailResult> SendAsync(MailSendRequest request, IReadOnlyList<MailOutgoingAttachment> attachments, CancellationToken ct = default)
    {
        var settings = Configured();
        if (settings is null)
            return MailResult.Fail("صندوق ورودی پیکربندی یا فعال نشده است.");
        if (string.IsNullOrWhiteSpace(settings.SmtpHost))
            return MailResult.Fail("سرور SMTP صندوق تنظیم نشده است.");

        var from = string.IsNullOrWhiteSpace(settings.Address) ? settings.Username : settings.Address;
        if (!MailboxAddress.TryParse(from, out var fromAddress))
            return MailResult.Fail("آدرس فرستنده صندوق معتبر نیست.");
        fromAddress.Name = settings.DisplayName;

        var message = new MimeMessage();
        message.From.Add(fromAddress);

        if (!AddRecipients(message.To, request.To, out var badTo)) return MailResult.Fail($"آدرس گیرنده معتبر نیست: {badTo}");
        if (!AddRecipients(message.Cc, request.Cc, out var badCc)) return MailResult.Fail($"آدرس رونوشت معتبر نیست: {badCc}");
        if (message.To.Count == 0 && message.Cc.Count == 0) return MailResult.Fail("حداقل یک گیرنده وارد کنید.");

        message.Subject = (request.Subject ?? "").Trim();

        // Threading: without In-Reply-To/References the customer's client shows the reply as an unrelated
        // new mail, which is exactly the confusion this feature exists to remove.
        if (request.InReplyToUid is uint replyUid && !string.IsNullOrWhiteSpace(request.ReplyToFolder))
        {
            var original = await GetAsync(request.ReplyToFolder!, replyUid, ct);
            if (original is { Ok: true, Value: not null } && !string.IsNullOrWhiteSpace(original.Value.MessageId))
            {
                message.InReplyTo = original.Value.MessageId;
                foreach (var reference in $"{original.Value.References} {original.Value.MessageId}".Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    message.References.Add(reference);
            }
        }

        // Plain text only, deliberately. The panel composes replies to customers; an HTML composer would add
        // a second sanitization surface and buy nothing a support reply needs.
        var body = new BodyBuilder { TextBody = request.Body ?? "" };
        foreach (var attachment in attachments)
            body.Attachments.Add(attachment.FileName, attachment.Content, ContentType.Parse(attachment.ContentType));
        message.Body = body.ToMessageBody();

        try
        {
            using var smtp = new SmtpClient();
            var security = settings.SmtpUseSsl
                ? MailKit.Security.SecureSocketOptions.StartTlsWhenAvailable
                : MailKit.Security.SecureSocketOptions.None;
            await smtp.ConnectAsync(settings.SmtpHost, settings.SmtpPort, security, ct);
            await smtp.AuthenticateAsync(settings.Username, settings.Password, ct);
            await smtp.SendAsync(message, ct);
            await smtp.DisconnectAsync(true, ct);
        }
        catch (MailKit.Security.AuthenticationException)
        {
            return MailResult.Fail("نام کاربری یا گذرواژه صندوق برای ارسال پذیرفته نشد.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMTP send failed via {Host}:{Port}", settings.SmtpHost, settings.SmtpPort);
            return MailResult.Fail($"ارسال پاسخ ممکن نشد: {ex.Message}");
        }

        // Filing the reply in Sent and marking the original answered are both best-effort: the mail is
        // already delivered at this point, and telling the operator it failed would be a lie.
        await TryAppendToSentAsync(message, ct);
        if (request.InReplyToUid is uint answeredUid && !string.IsNullOrWhiteSpace(request.ReplyToFolder))
            await SetFlagAsync(request.ReplyToFolder!, answeredUid, MessageFlags.Answered, true, ct);

        return MailResult.Success;
    }

    private async Task TryAppendToSentAsync(MimeMessage message, CancellationToken ct)
    {
        try
        {
            await WithImapAsync(async (client, token) =>
            {
                var sent = (await EnumerateFoldersAsync(client, token))
                    .FirstOrDefault(f => f.Attributes.HasFlag(FolderAttributes.Sent))
                    ?? (await EnumerateFoldersAsync(client, token))
                        .FirstOrDefault(f => f.Name.Equals("Sent", StringComparison.OrdinalIgnoreCase));
                if (sent is null) return MailResult<bool>.Success(false);

                await sent.OpenAsync(FolderAccess.ReadWrite, token);
                await sent.AppendAsync(message, MessageFlags.Seen, token);
                await sent.CloseAsync(false, token);
                return MailResult<bool>.Success(true);
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reply was delivered but could not be filed in the Sent folder.");
        }
    }

    // ── Badge + connection test ─────────────────────────────────────────────────────────────────────

    public async Task<int> GetUnreadCountAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await WithImapAsync<int>(async (client, token) =>
            {
                await client.Inbox.StatusAsync(StatusItems.Unread, token);
                return MailResult<int>.Success(client.Inbox.Unread);
            }, ct);
            return result is { Ok: true, Value: int unread } ? unread : 0;
        }
        catch
        {
            return 0; // a badge is never worth failing the admin menu over
        }
    }

    public async Task<MailResult> TestConnectionAsync(CancellationToken ct = default)
    {
        var imap = await WithImapAsync<bool>(async (client, token) =>
        {
            await client.Inbox.StatusAsync(StatusItems.Count, token);
            return MailResult<bool>.Success(true);
        }, ct);
        if (!imap.Ok) return MailResult.Fail(imap.Error ?? "اتصال IMAP ناموفق بود.");

        // SMTP is verified separately (and without sending anything) because a working IMAP login says
        // nothing about whether replies will go out.
        var settings = Configured();
        if (settings is null || string.IsNullOrWhiteSpace(settings.SmtpHost)) return MailResult.Success;
        try
        {
            using var smtp = new SmtpClient();
            var security = settings.SmtpUseSsl
                ? MailKit.Security.SecureSocketOptions.StartTlsWhenAvailable
                : MailKit.Security.SecureSocketOptions.None;
            await smtp.ConnectAsync(settings.SmtpHost, settings.SmtpPort, security, ct);
            await smtp.AuthenticateAsync(settings.Username, settings.Password, ct);
            await smtp.DisconnectAsync(true, ct);
            return MailResult.Success;
        }
        catch (Exception ex)
        {
            return MailResult.Fail($"IMAP درست است اما اتصال SMTP ناموفق بود: {ex.Message}");
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static async Task<MailResult> Unit<T>(Task<MailResult<T>> task)
    {
        var result = await task;
        return result.Ok ? MailResult.Success : MailResult.Fail(result.Error ?? "عملیات ناموفق بود.");
    }

    private static bool AddRecipients(InternetAddressList list, IReadOnlyList<string>? input, out string invalid)
    {
        invalid = "";
        foreach (var raw in input ?? Array.Empty<string>())
        {
            var address = (raw ?? "").Trim();
            if (address.Length == 0) continue;
            if (!MailboxAddress.TryParse(address, out var parsed)) { invalid = address; return false; }
            list.Add(parsed);
        }
        return true;
    }

    private static MailAddressInfo FirstAddress(InternetAddressList? list)
    {
        var mailbox = list?.Mailboxes.FirstOrDefault();
        return mailbox is null
            ? new MailAddressInfo("", "")
            : new MailAddressInfo(Clean(mailbox.Name), mailbox.Address ?? "");
    }

    private static IReadOnlyList<MailAddressInfo> Addresses(InternetAddressList? list) =>
        list?.Mailboxes.Select(m => new MailAddressInfo(Clean(m.Name), m.Address ?? "")).ToList()
        ?? (IReadOnlyList<MailAddressInfo>)Array.Empty<MailAddressInfo>();

    // Control characters in a header are how a subject line smuggles a fake second header, or breaks the
    // JSON the panel renders. Strip them everywhere a header string reaches the client.
    private static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return new string(value.Where(c => !char.IsControl(c) || c is '\t').ToArray()).Trim();
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    // An attachment filename arrives from the sender and is used to name a downloaded file. Anything that
    // could traverse a path or hide the real extension is replaced, and the result is never used to build a
    // server-side path — only as the Content-Disposition name.
    private static string SafeFileName(string? name, int index)
    {
        var candidate = Path.GetFileName((name ?? "").Trim());
        if (string.IsNullOrWhiteSpace(candidate)) return $"attachment-{index + 1}";

        var cleaned = new string(candidate.Where(c => !char.IsControl(c) && !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
        cleaned = cleaned.Replace("\"", "").Trim(' ', '.');
        return string.IsNullOrWhiteSpace(cleaned) ? $"attachment-{index + 1}" : Truncate(cleaned, 180);
    }
}

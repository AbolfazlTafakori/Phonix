namespace Phonix.Api.Services;

// ── Wire shapes ─────────────────────────────────────────────────────────────────────────────────────
// UIDs, not sequence numbers: a sequence number shifts as soon as anything else touches the folder, so a
// "delete message 3" built on one would eventually delete the wrong mail. UIDs are stable per folder, and
// the UidValidity travels with every response so the client can detect a folder that was rebuilt underneath
// it and refetch instead of acting on stale ids.

public sealed record MailFolderInfo(
    string Name,       // the IMAP path, e.g. "INBOX" or "Archive" — what the client sends back
    string Title,      // Persian label for the rail
    string Kind,       // inbox | sent | drafts | trash | spam | archive | other — drives the icon
    int Total,
    int Unread);

public sealed record MailAddressInfo(string Name, string Address);

public sealed record MailSummary(
    uint Uid,
    string Subject,
    MailAddressInfo From,
    IReadOnlyList<MailAddressInfo> To,
    DateTimeOffset Date,
    string Preview,          // first ~200 chars of the text body, for the list row
    bool Seen,
    bool Flagged,
    bool Answered,
    bool HasAttachments);

public sealed record MailAttachmentInfo(int Index, string FileName, string ContentType, long Size);

public sealed record MailMessageDetail(
    uint Uid,
    string Subject,
    MailAddressInfo From,
    IReadOnlyList<MailAddressInfo> To,
    IReadOnlyList<MailAddressInfo> Cc,
    DateTimeOffset Date,
    string TextBody,
    string HtmlBody,          // already sanitized; still rendered in a sandboxed iframe client-side
    bool HadRemoteContent,    // true when sanitizing stripped remote images/frames — the UI says so
    bool Seen,
    bool Flagged,
    string MessageId,         // for In-Reply-To when replying
    string References,
    IReadOnlyList<MailAttachmentInfo> Attachments);

public sealed record MailPage(
    IReadOnlyList<MailSummary> Items,
    int Total,
    int Page,
    int PageSize,
    uint UidValidity);

public sealed record MailAttachmentContent(byte[] Content, string ContentType, string FileName);

// An outgoing message composed in the panel. `InReplyToUid` is set when replying, so the service can pull the
// original's Message-Id/References and keep the customer's client threading the conversation.
public sealed record MailSendRequest(
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    string Subject,
    string Body,
    string? ReplyToFolder = null,
    uint? InReplyToUid = null);

public sealed record MailOutgoingAttachment(string FileName, string ContentType, byte[] Content);

// Why a result record instead of throwing: every failure here is an OPERATOR problem (wrong host, wrong
// password, server down, folder gone), not a bug, and each one needs a specific Persian sentence in the
// panel. Exceptions would collapse them all into a 500.
public sealed record MailResult(bool Ok, string? Error = null)
{
    public static readonly MailResult Success = new(true);
    public static MailResult Fail(string error) => new(false, error);
}

public sealed record MailResult<T>(bool Ok, T? Value, string? Error = null)
{
    public static MailResult<T> Success(T value) => new(true, value);
    public static MailResult<T> Fail(string error) => new(false, default, error);
}

public interface IMailboxService
{
    Task<MailResult<IReadOnlyList<MailFolderInfo>>> GetFoldersAsync(CancellationToken ct = default);

    Task<MailResult<MailPage>> ListAsync(string folder, int page, int pageSize, string? search, bool unreadOnly, CancellationToken ct = default);

    Task<MailResult<MailMessageDetail>> GetAsync(string folder, uint uid, CancellationToken ct = default);

    Task<MailResult<MailAttachmentContent>> GetAttachmentAsync(string folder, uint uid, int index, CancellationToken ct = default);

    Task<MailResult> SetSeenAsync(string folder, uint uid, bool seen, CancellationToken ct = default);

    Task<MailResult> SetFlaggedAsync(string folder, uint uid, bool flagged, CancellationToken ct = default);

    // Moves to another folder; the Trash/Archive targets are resolved from the server's special-use flags
    // rather than hardcoded names, because Dovecot's defaults differ from other servers'.
    Task<MailResult> MoveAsync(string folder, uint uid, string targetFolder, CancellationToken ct = default);

    Task<MailResult> SendAsync(MailSendRequest request, IReadOnlyList<MailOutgoingAttachment> attachments, CancellationToken ct = default);

    // Unread count for the sidebar badge. Returns 0 rather than an error when the mailbox is off or
    // unreachable — a badge is not worth failing the whole admin menu request over.
    Task<int> GetUnreadCountAsync(CancellationToken ct = default);

    // "Does this configuration actually work?" — used by the settings page's test button.
    Task<MailResult> TestConnectionAsync(CancellationToken ct = default);
}

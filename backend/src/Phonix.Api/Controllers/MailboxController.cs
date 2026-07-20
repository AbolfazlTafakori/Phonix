using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;
using Phonix.Api.Services;

namespace Phonix.Api.Controllers;

// What the panel is allowed to know about the mailbox configuration. The password is absent BY TYPE rather
// than blanked at the call site, so no future endpoint can leak it by forgetting to strip it.
public sealed record MailboxSettingsDto(
    bool Enabled,
    string ImapHost, int ImapPort, bool ImapUseSsl,
    string SmtpHost, int SmtpPort, bool SmtpUseSsl,
    string Username, string Address, string DisplayName,
    bool HasPassword);

// Incoming settings. Password is optional: empty means "leave the stored one alone" (see UpdateMailboxSettings).
public sealed record MailboxSettingsInput(
    bool Enabled,
    string ImapHost, int ImapPort, bool ImapUseSsl,
    string SmtpHost, int SmtpPort, bool SmtpUseSsl,
    string Username, string Address, string DisplayName,
    string? Password);

public sealed record MailMoveInput(string Target);
public sealed record MailFlagInput(bool Value);

// The shop's inbound mailbox, as an inbox the staff can actually work in.
//
// Everything here is staff-only and gated on the "mailbox" panel permission, because the contents are
// customer correspondence: an unfiltered stream of personal data that no customer-facing session may touch.
// Settings are Admin-only on top of that — an operator who can read the mail should not also be able to
// repoint the mailbox at a server they control.
[ApiController]
[Route("api/mailbox")]
[Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Support)}")]
[AdminPermission("mailbox")]
public class MailboxController : ControllerBase
{
    // Matches the 8 MB cap the existing upload endpoint uses, per file, with a total ceiling so a compose
    // request cannot be used to push an unbounded body through the API.
    private const long MaxAttachmentBytes = 8 * 1024 * 1024;
    private const long MaxTotalAttachmentBytes = 20 * 1024 * 1024;

    private readonly IMailboxService _mail;
    private readonly IDataStore _store;

    public MailboxController(IMailboxService mail, IDataStore store)
    {
        _mail = mail;
        _store = store;
    }

    // Failures here are configuration problems, not server faults, so they come back as 400 with the
    // operator-facing sentence the service produced — the panel shows it verbatim.
    // Plain string, not an object: the panel's fetch wrapper surfaces a 400 body verbatim as the error
    // message, so a JSON envelope would show the customer-facing operator as raw `{"error":"…"}`.
    private IActionResult Problem(string? error) => BadRequest(error ?? "عملیات ناموفق بود.");

    // ── Reading ─────────────────────────────────────────────────────────────────────────────────────

    [HttpGet("folders")]
    public async Task<IActionResult> Folders(CancellationToken ct)
    {
        var result = await _mail.GetFoldersAsync(ct);
        return result.Ok ? Ok(result.Value) : Problem(result.Error);
    }

    [HttpGet("messages")]
    public async Task<IActionResult> List(
        [FromQuery] string folder = "INBOX",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null,
        [FromQuery] bool unreadOnly = false,
        CancellationToken ct = default)
    {
        var result = await _mail.ListAsync(folder, page, pageSize, search, unreadOnly, ct);
        return result.Ok ? Ok(result.Value) : Problem(result.Error);
    }

    [HttpGet("messages/{uid}")]
    public async Task<IActionResult> Get(uint uid, [FromQuery] string folder = "INBOX", CancellationToken ct = default)
    {
        var result = await _mail.GetAsync(folder, uid, ct);
        return result.Ok ? Ok(result.Value) : Problem(result.Error);
    }

    [HttpGet("messages/{uid}/attachments/{index:int}")]
    public async Task<IActionResult> Attachment(uint uid, int index, [FromQuery] string folder = "INBOX", CancellationToken ct = default)
    {
        var result = await _mail.GetAttachmentAsync(folder, uid, index, ct);
        if (!result.Ok || result.Value is null) return Problem(result.Error);

        // An attachment is arbitrary bytes from a stranger. Three things keep it from executing in the
        // panel's origin: it is always served as a DOWNLOAD, never inline; the browser is told not to
        // second-guess the declared type; and the declared type itself is forced to a neutral one, so an
        // HTML or SVG attachment cannot be coaxed into rendering as a document on our domain.
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
        return File(result.Value.Content, "application/octet-stream", result.Value.FileName);
    }

    // ── State changes ───────────────────────────────────────────────────────────────────────────────

    [HttpPost("messages/{uid}/seen")]
    public async Task<IActionResult> SetSeen(uint uid, MailFlagInput input, [FromQuery] string folder = "INBOX", CancellationToken ct = default)
    {
        var result = await _mail.SetSeenAsync(folder, uid, input.Value, ct);
        return result.Ok ? Ok(new { ok = true }) : Problem(result.Error);
    }

    [HttpPost("messages/{uid}/flagged")]
    public async Task<IActionResult> SetFlagged(uint uid, MailFlagInput input, [FromQuery] string folder = "INBOX", CancellationToken ct = default)
    {
        var result = await _mail.SetFlaggedAsync(folder, uid, input.Value, ct);
        return result.Ok ? Ok(new { ok = true }) : Problem(result.Error);
    }

    // Moving is the only "destructive" action exposed, and it is not actually destructive: deleting in the
    // panel means moving to the server's Trash folder, so a misclick is recoverable from any mail client.
    // There is deliberately no expunge endpoint.
    [HttpPost("messages/{uid}/move")]
    public async Task<IActionResult> Move(uint uid, MailMoveInput input, [FromQuery] string folder = "INBOX", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Target)) return Problem("پوشه مقصد را مشخص کنید.");
        var result = await _mail.MoveAsync(folder, uid, input.Target, ct);
        return result.Ok ? Ok(new { ok = true }) : Problem(result.Error);
    }

    // ── Sending ─────────────────────────────────────────────────────────────────────────────────────

    // multipart/form-data rather than JSON, because a reply carries files. Recipients arrive as repeated
    // `to` / `cc` fields.
    [HttpPost("send")]
    [RequestSizeLimit(MaxTotalAttachmentBytes + (2 * 1024 * 1024))]
    public async Task<IActionResult> Send(
        [FromForm] string? subject,
        [FromForm] string? body,
        [FromForm] string[]? to,
        [FromForm] string[]? cc,
        [FromForm] string? replyToFolder,
        [FromForm] uint? inReplyToUid,
        [FromForm] IFormFileCollection? files,
        CancellationToken ct = default)
    {
        var attachments = new List<MailOutgoingAttachment>();
        long total = 0;

        foreach (var file in files ?? (IFormFileCollection)new FormFileCollection())
        {
            if (file.Length <= 0) continue;
            if (file.Length > MaxAttachmentBytes)
                return Problem($"حجم فایل «{file.FileName}» بیش از ۸ مگابایت است.");
            total += file.Length;
            if (total > MaxTotalAttachmentBytes)
                return Problem("مجموع حجم پیوست‌ها بیش از ۲۰ مگابایت است.");

            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            attachments.Add(new MailOutgoingAttachment(
                Path.GetFileName(file.FileName ?? "attachment"),
                string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
                buffer.ToArray()));
        }

        var request = new MailSendRequest(
            To: to ?? Array.Empty<string>(),
            Cc: cc ?? Array.Empty<string>(),
            Subject: subject ?? "",
            Body: body ?? "",
            ReplyToFolder: replyToFolder,
            InReplyToUid: inReplyToUid);

        var result = await _mail.SendAsync(request, attachments, ct);
        return result.Ok ? Ok(new { ok = true }) : Problem(result.Error);
    }

    // ── Settings (Admin only) ───────────────────────────────────────────────────────────────────────

    [HttpGet("settings")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public MailboxSettingsDto GetSettings()
    {
        var s = _store.GetMailboxSettings();
        return new MailboxSettingsDto(
            s.Enabled, s.ImapHost, s.ImapPort, s.ImapUseSsl,
            s.SmtpHost, s.SmtpPort, s.SmtpUseSsl,
            s.Username, s.Address, s.DisplayName,
            HasPassword: !string.IsNullOrEmpty(s.Password));
    }

    [HttpPut("settings")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public MailboxSettingsDto UpdateSettings(MailboxSettingsInput input)
    {
        _store.UpdateMailboxSettings(new MailboxSettings
        {
            Enabled = input.Enabled,
            ImapHost = input.ImapHost,
            ImapPort = input.ImapPort,
            ImapUseSsl = input.ImapUseSsl,
            SmtpHost = input.SmtpHost,
            SmtpPort = input.SmtpPort,
            SmtpUseSsl = input.SmtpUseSsl,
            Username = input.Username,
            Address = input.Address,
            DisplayName = input.DisplayName,
            Password = input.Password ?? "",
        });
        return GetSettings();
    }

    [HttpPost("settings/test")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> TestSettings(CancellationToken ct)
    {
        var result = await _mail.TestConnectionAsync(ct);
        return result.Ok ? Ok(new { ok = true }) : Problem(result.Error);
    }
}

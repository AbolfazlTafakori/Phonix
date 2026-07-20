using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Controllers;

public record SentEmailDto(int Id, string To, string Subject, string SentAt, bool Success, string? Error);

public record SentEmailPageDto(
    IReadOnlyList<SentEmailDto> Items,
    int Total,
    int Page,
    int PageSize,
    int TotalPages,
    int Failed);

// The record of what the shop has sent. info@ is send-only — nothing is delivered back to it — so without
// this there is no way to answer "did this customer actually get their account email?".
//
// Admin-only, like the other logs: this is a list of customers' email addresses, so it is personal data and
// belongs with the audit and system logs rather than in the sections limited staff receive.
[ApiController]
[Route("api/admin/email-log")]
[Authorize(Roles = nameof(UserRole.Admin))]
[AdminPermission("email-log")]
public class EmailLogController : ControllerBase
{
    private readonly EmailLogStore _log;
    public EmailLogController(EmailLogStore log) => _log = log;

    // GET /api/admin/email-log?search=&status=failed&from=2026-07-01&to=2026-07-20&page=1&pageSize=20
    // Newest first, paginated, optionally filtered by recipient/subject text, outcome and a date range.
    [HttpGet]
    public SentEmailPageDto Get(
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        bool? success = status?.Trim().ToLowerInvariant() switch
        {
            "sent" or "success" => true,
            "failed" or "failure" => false,
            _ => null,
        };

        var fromUtc = ParseDate(from);
        // `to` is inclusive of the day the admin picks, so push it to the next day's start against the
        // store's exclusive upper bound.
        DateTime? toUtc = ParseDate(to) is DateTime t ? t.AddDays(1) : null;

        var (items, total) = _log.Get(search, success, fromUtc, toUtc, page, pageSize);

        var size = Math.Clamp(pageSize, 1, 100);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)size));

        return new SentEmailPageDto(
            Items: items.Select(ToDto).ToList(),
            Total: total,
            Page: Math.Clamp(page, 1, totalPages),
            PageSize: size,
            TotalPages: totalPages,
            Failed: _log.FailedCount());
    }

    private static SentEmailDto ToDto(SentEmail e) => new(
        Id: e.Id,
        To: e.To,
        Subject: e.Subject,
        SentAt: e.SentAtUtc.ToString("o", CultureInfo.InvariantCulture),
        Success: e.Success,
        Error: e.Error);

    private static DateTime? ParseDate(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d)
            ? d
            : null;
}

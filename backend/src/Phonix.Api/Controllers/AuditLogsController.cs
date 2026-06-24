using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Models;

namespace Phonix.Api.Controllers;

public record AuditLogDto(
    int Id,
    string ActionType,
    string Entity,
    string? EntityId,
    int? ActorId,
    string ActorName,
    string ActorRole,
    string Method,
    string Path,
    string Ip,
    int StatusCode,
    bool Success,
    string Timestamp);

public record AuditLogPageDto(
    IReadOnlyList<AuditLogDto> Items,
    int Total,
    int Page,
    int PageSize,
    int TotalPages);

// Admin-only system audit trail. Strictly Admin (not Support) — it exposes the activity of all staff,
// so it belongs to the DevOps/system group that limited staff never receive.
[ApiController]
[Route("api/admin/audit-logs")]
[Authorize(Roles = nameof(UserRole.Admin))]
public class AuditLogsController : ControllerBase
{
    private readonly AuditStore _audit;
    public AuditLogsController(AuditStore audit) => _audit = audit;

    // GET /api/admin/audit-logs?search=&action=Create&from=2026-06-01&to=2026-06-24&page=1&pageSize=20
    // Returns newest-first, paginated, optionally filtered by free text, action type, and a date range.
    [HttpGet]
    public AuditLogPageDto Get(
        [FromQuery] string? search,
        [FromQuery] string? action,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        AuditAction? actionFilter =
            Enum.TryParse<AuditAction>(action, ignoreCase: true, out var parsed) ? parsed : null;

        DateTime? fromUtc = ParseDate(from);
        // `to` is end-of-range inclusive for the day the admin picks: push to the next day's start so the
        // store's exclusive upper bound still captures everything that happened on the chosen date.
        DateTime? toUtc = ParseDate(to) is DateTime t ? t.AddDays(1) : null;

        var (items, total) = _audit.GetAuditLogs(search, actionFilter, fromUtc, toUtc, page, pageSize);

        var size = Math.Clamp(pageSize, 1, 100);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)size));
        var current = Math.Clamp(page, 1, totalPages);

        return new AuditLogPageDto(
            Items: items.Select(ToDto).ToList(),
            Total: total,
            Page: current,
            PageSize: size,
            TotalPages: totalPages);
    }

    private static AuditLogDto ToDto(AuditLog l) => new(
        Id: l.Id,
        ActionType: l.ActionType.ToString(),
        Entity: l.Entity,
        EntityId: l.EntityId,
        ActorId: l.ActorId,
        ActorName: l.ActorName,
        ActorRole: l.ActorRole,
        Method: l.Method,
        Path: l.Path,
        Ip: l.Ip,
        StatusCode: l.StatusCode,
        Success: l.Success,
        Timestamp: l.Timestamp.ToString("o", CultureInfo.InvariantCulture));

    private static DateTime? ParseDate(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d)
            ? d.Date
            : null;
}

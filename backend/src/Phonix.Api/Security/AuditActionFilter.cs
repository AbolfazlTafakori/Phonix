using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Phonix.Api.Data;
using Phonix.Api.Models;

namespace Phonix.Api.Security;

// Populates the admin audit trail automatically: every MUTATING request (POST/PUT/PATCH/DELETE) made by an
// authenticated staff member (Admin/Support) is recorded — actor, resource, IP, and outcome — without any
// per-controller wiring. Reads (GET) and anonymous/customer traffic are ignored, so the log stays a focused
// record of administrative changes. Auditing is best-effort: a failure here never breaks the actual request.
public sealed class AuditActionFilter : IAsyncActionFilter
{
    private static readonly HashSet<string> MutatingVerbs =
        new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "PATCH", "DELETE" };

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executed = await next();
        try { Record(context.HttpContext, executed); }
        catch { /* auditing must never surface as a request failure */ }
    }

    private static void Record(HttpContext http, ActionExecutedContext executed)
    {
        if (!MutatingVerbs.Contains(http.Request.Method)) return;

        var user = http.User;
        var isStaff = user.IsInRole(nameof(UserRole.Admin)) || user.IsInRole(nameof(UserRole.Support));
        if (!isStaff) return;

        if (http.RequestServices.GetService(typeof(AuditStore)) is not AuditStore audit) return;

        var path = http.Request.Path.Value ?? "";
        var (entity, entityId) = ParseEntity(path);

        // The audit log reads itself only via GET, so it's already excluded — but guard the entity name
        // anyway so a future mutating endpoint there can't make the log record its own reads.
        if (entity == "audit-logs") return;

        var statusCode = executed.Exception is not null
            ? StatusCodes.Status500InternalServerError
            : (executed.Result as IStatusCodeActionResult)?.StatusCode ?? http.Response.StatusCode;
        if (statusCode == 0) statusCode = StatusCodes.Status200OK;

        var actionType = http.Request.Method.ToUpperInvariant() switch
        {
            "POST" => AuditAction.Create,
            "PUT" or "PATCH" => AuditAction.Update,
            "DELETE" => AuditAction.Delete,
            _ => AuditAction.Other,
        };

        audit.Record(new AuditLog
        {
            ActionType = actionType,
            Entity = entity,
            EntityId = entityId,
            ActorId = int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null,
            ActorName = user.FindFirstValue(ClaimTypes.Name) ?? "",
            ActorRole = user.IsInRole(nameof(UserRole.Admin)) ? nameof(UserRole.Admin) : nameof(UserRole.Support),
            Method = http.Request.Method.ToUpperInvariant(),
            Path = path,
            Ip = http.Connection.RemoteIpAddress?.ToString() ?? "",
            StatusCode = statusCode,
            Success = statusCode is >= 200 and < 400,
        });
    }

    // Extracts the specific resource and (numeric) id from the request path. The resource is the first
    // meaningful segment after "api", but the generic "admin" namespace is skipped so sub-routes keep their
    // identity instead of all collapsing to "admin":
    //   "/api/products/5"            → ("products", "5")
    //   "/api/orders"                → ("orders", null)
    //   "/api/admin/audit-logs"      → ("audit-logs", null)
    //   "/api/admin/server-status"   → ("server-status", null)
    //   "/api/admin/users/7"         → ("users", "7")
    private static (string Entity, string? EntityId) ParseEntity(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var apiIndex = Array.FindIndex(segments, s => string.Equals(s, "api", StringComparison.OrdinalIgnoreCase));
        var start = apiIndex >= 0 ? apiIndex + 1 : 0;

        // Skip the generic "admin" prefix so the real resource segment is used as the entity.
        if (start < segments.Length && string.Equals(segments[start], "admin", StringComparison.OrdinalIgnoreCase))
            start++;

        if (start >= segments.Length) return ("", null);

        var entity = segments[start];
        var last = segments[^1];
        var entityId = last != entity && last.Length > 0 && last.All(char.IsDigit) ? last : null;
        return (entity, entityId);
    }
}

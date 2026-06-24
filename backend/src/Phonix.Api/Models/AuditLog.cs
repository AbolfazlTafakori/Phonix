namespace Phonix.Api.Models;

// Classifies an audited admin action so the UI can colour it (Create=green, Update=amber, Delete=red).
// Derived from the HTTP verb of the staff request that produced it.
public enum AuditAction
{
    Other,
    Create,
    Update,
    Delete,
}

// One immutable entry in the admin audit trail: WHO (staff actor) did WHAT (action + entity) from WHERE
// (IP) and WHEN, plus the outcome (HTTP status). Captured automatically for every mutating staff request
// by AuditActionFilter and stored append-only with the rest of the durable state (store.json).
public class AuditLog
{
    public int Id { get; set; }

    public AuditAction ActionType { get; set; } = AuditAction.Other;

    // The resource touched, taken from the request path (e.g. "products", "orders"). EntityId is the
    // numeric id segment when present (e.g. "/api/products/5" → Entity "products", EntityId "5").
    public string Entity { get; set; } = "";
    public string? EntityId { get; set; }

    // The acting staff member. ActorId/ActorName come from the authenticated session's claims; a record is
    // only written for Admin/Support callers, never anonymous or customer traffic.
    public int? ActorId { get; set; }
    public string ActorName { get; set; } = "";
    public string ActorRole { get; set; } = "";

    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public string Ip { get; set; } = "";

    // The HTTP status the request finished with; Success is the convenience flag (2xx/3xx) the UI badges.
    public int StatusCode { get; set; }
    public bool Success { get; set; }

    public DateTime Timestamp { get; set; }
}

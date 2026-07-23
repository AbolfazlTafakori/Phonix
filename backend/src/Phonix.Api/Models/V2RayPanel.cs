namespace Phonix.Api.Models;

// Which panel software a configured server runs. Only Sanaei (the 3x-ui fork, MHSanaei) is wired up now;
// the others are declared so the UI can offer them as "coming soon" and so adding one later is a connector
// implementation, not a schema change.
public enum V2RayProvider
{
    Sanaei = 0,   // 3x-ui (MHSanaei) — the one we build against first
    Pasargad = 1,
    Marzban = 2,
    Alireza = 3,
    TxUi = 4,
}

// One V2Ray/Xray management panel the shop provisions accounts on. The credential is the panel's OWN admin
// login: the shop signs in with it (cookie session) to create clients on purchase. The password is stored
// encrypted at rest (SensitiveField) and never returned to the browser — the panel only learns whether one
// is set. Kept entirely separate from every other credential in the shop; this is server-side infrastructure
// only the owner configures.
public class V2RayPanel
{
    public int Id { get; set; }
    public V2RayProvider Provider { get; set; } = V2RayProvider.Sanaei;

    // The full panel URL exactly as entered, including scheme, optional :port and optional /webpath, e.g.
    // https://sub.example.com:8080/secretpath — everything up to (but not including) the panel's own routes.
    public string Url { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";   // encrypted via SensitiveField; blank-on-update = keep

    // PREFERRED credential. A panel API token (Settings → Security → API Token) is sent as
    // `Authorization: Bearer …`, which the panel's CSRF middleware skips outright
    // (`if c.GetBool("api_authed") { c.Next() }`). That avoids the whole session + CSRF handshake the
    // username/password path needs, so it is both simpler and far less brittle for server-to-server calls.
    // When this is set the connector uses it and never logs in. Encrypted at rest like the password.
    public string ApiToken { get; set; } = "";

    // ── Operator-facing identity ────────────────────────────────────────────────────────────────────
    public string Name { get; set; } = "";     // e.g. "هلند تانل" — how staff refer to this server
    public string Remark { get; set; } = "";   // e.g. "Netherlands" — the label carried into configs
    public string Flag { get; set; } = "";     // country code, e.g. "NL", purely for the UI
    public int Capacity { get; set; }          // max accounts this server should hold; 0 = unlimited

    // ── Subscription server ─────────────────────────────────────────────────────────────────────────
    // The panel's subscription service usually runs on its OWN domain/port/path, separate from the panel
    // itself, and that is the URL the customer's client app is pointed at. It cannot be derived from the
    // panel URL, so it is captured here; leaving the domain empty means "no subscription link".
    public string SubDomain { get; set; } = "";
    public int SubPort { get; set; }
    public string SubPath { get; set; } = "sub";
    public bool SubHttps { get; set; } = true;

    public bool Enabled { get; set; } = true;
    public string CreatedAtUtc { get; set; } = "";

    // The customer-facing subscription URL for one subId, or empty when the subscription server isn't
    // configured. Built exactly as the panel serves it: scheme://domain:port/path/subId.
    public string SubscriptionUrl(string subId)
    {
        if (string.IsNullOrWhiteSpace(SubDomain) || string.IsNullOrWhiteSpace(subId)) return "";
        var scheme = SubHttps ? "https" : "http";
        var port = SubPort > 0 ? $":{SubPort}" : "";
        var path = (SubPath ?? "").Trim('/');
        return path.Length == 0
            ? $"{scheme}://{SubDomain}{port}/{subId}"
            : $"{scheme}://{SubDomain}{port}/{path}/{subId}";
    }

    // Result of the most recent connection test, so the panel list can show a live status without re-probing
    // every server on every page load.
    public string LastCheckAtUtc { get; set; } = "";
    public bool LastCheckOk { get; set; }
    public string LastCheckError { get; set; } = "";
    public int InboundCount { get; set; }         // inbounds seen at the last successful check
}

public class V2RaySettings
{
    public List<V2RayPanel> Panels { get; set; } = new();
    public int NextId { get; set; } = 1;   // panel id counter (kept as-is for backward compatibility)

    // The separate V2Ray sales catalogue.
    public List<V2RayCategory> Categories { get; set; } = new();
    public List<V2RayPlan> Plans { get; set; } = new();
    public int NextCategoryId { get; set; } = 1;
    public int NextPlanId { get; set; } = 1;
}

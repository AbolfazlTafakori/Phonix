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

    public bool Enabled { get; set; } = true;
    public string CreatedAtUtc { get; set; } = "";

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

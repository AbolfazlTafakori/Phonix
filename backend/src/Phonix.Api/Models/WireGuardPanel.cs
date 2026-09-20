namespace Phonix.Api.Models;

// Which panel software a configured WireGuard server runs. Only W-UI is wired up: it is the panel this shop
// is built alongside, and it is the only one whose API sells WireGuard/AmneziaWG/OpenVPN the way the
// storefront needs (a customer with a quota, an expiry and a device limit across several tunnels).
public enum WireGuardProvider
{
    WUi = 0,
}

// One W-UI panel the shop provisions customers on. Same shape and same rules as V2RayPanel: the credential
// is the panel's OWN admin login or, preferably, an API token; both are encrypted at rest (SensitiveField)
// and never returned to the browser. Owner-only infrastructure.
public class WireGuardPanel
{
    public int Id { get; set; }
    public WireGuardProvider Provider { get; set; } = WireGuardProvider.WUi;

    // The full panel URL exactly as the installer printed it, including scheme, port and the random
    // WebBasePath, e.g. https://203.0.113.5:41873/t7GhP3nLqZ8xWc2vRy — everything up to /api.
    public string Url { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";   // encrypted via SensitiveField; blank-on-update = keep

    // PREFERRED credential. A W-UI access token (Settings → API tokens, or the one the installer prints,
    // `wui_…`) goes on every request as `Authorization: Bearer …`. A username/password session on W-UI is
    // a JWT that is additionally bound to an HttpOnly cookie, so the token path is both simpler and the
    // one the panel means for machines. Encrypted at rest like the password.
    public string ApiToken { get; set; } = "";

    // ── Operator-facing identity ────────────────────────────────────────────────────────────────────
    public string Name { get; set; } = "";     // e.g. "آلمان وایرگارد" — how staff refer to this server
    public string Remark { get; set; } = "";   // e.g. "Germany" — the label carried into customer names
    public string Flag { get; set; } = "";     // country code, e.g. "DE", purely for the UI
    public int Capacity { get; set; }          // max customers this server should hold; 0 = unlimited

    public bool Enabled { get; set; } = true;
    public string CreatedAtUtc { get; set; } = "";

    // Result of the most recent connection test, so the panel list can show a live status without re-probing
    // every server on every page load.
    public string LastCheckAtUtc { get; set; } = "";
    public bool LastCheckOk { get; set; }
    public string LastCheckError { get; set; } = "";
    public int InterfaceCount { get; set; }      // tunnels seen at the last successful check
    public string PanelVersion { get; set; } = ""; // W-UI version reported at the last successful check
}

public class WireGuardSettings
{
    public List<WireGuardPanel> Panels { get; set; } = new();
    public int NextId { get; set; } = 1;

    // The separate WireGuard sales catalogue.
    public List<WireGuardCategory> Categories { get; set; } = new();
    public List<WireGuardPlan> Plans { get; set; } = new();
    public int NextCategoryId { get; set; } = 1;
    public int NextPlanId { get; set; } = 1;
}

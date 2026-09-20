using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Phonix.Api.Models;

namespace Phonix.Api.Services;

public sealed record WireGuardTestResult(bool Ok, string? Error = null, int InterfaceCount = 0, string Version = "")
{
    public static WireGuardTestResult Succeeded(int interfaces, string version) => new(true, null, interfaces, version);
    public static WireGuardTestResult Fail(string error) => new(false, error);
}

// A customer to create, in the shop's own terms. Zero means "unlimited" for traffic and duration, matching
// W-UI: 0 quotaBytes and a null expiresAt are both "no limit". The device limit is what W-UI enforces as
// simultaneous devices; it must be at least 1 because creation issues the first device.
public sealed record WireGuardNewClient(
    string Name,         // the unique label the customer is created under (the name the buyer chose)
    long TotalGb,        // 0 = unlimited
    int DeviceLimit,     // ≥ 1
    int DurationDays,    // 0 = never expires; otherwise a fixed calendar expiry this many days out
    bool StartOnFirstUse = false); // true = the clock starts on the first connection instead of now

public sealed record WireGuardClientResult(
    bool Ok, string? Error = null, int ClientId = 0, string SubId = "", string SubscriptionUrl = "", int InterfacesAdded = 0)
{
    public static WireGuardClientResult Fail(string error) => new(false, error);
}

// One tunnel as W-UI reports it: the id a plan maps onto, what it runs, and how full it is.
public sealed record WireGuardInterface(
    int Id, string Name, string Protocol, string Mode, int ListenPort, string EndpointHost,
    bool Enabled, bool Running, long Clients, long Devices, int Allocated, int Capacity, string NodeName);

public sealed record WireGuardInterfacesResult(bool Ok, string? Error = null, IReadOnlyList<WireGuardInterface>? Interfaces = null)
{
    public static WireGuardInterfacesResult Fail(string error) => new(false, error);
}

// One customer's live counters as W-UI holds them. `QuotaBytes` 0 is unlimited, a null expiry never
// expires. `Missing` separates "the panel answered and has no such customer" from "the panel could not be
// reached" — one means the record is gone for good, the other means try again later.
public sealed record WireGuardClientState(
    bool Ok, string? Error = null,
    long UsedBytes = 0, long QuotaBytes = 0, DateTimeOffset? ExpiresAt = null,
    string Status = "", int DeviceLimit = 0, int OnlineNow = 0, bool Missing = false)
{
    public static WireGuardClientState Fail(string error) => new(false, error);
    public static WireGuardClientState Gone(string error) => new(false, error, Missing: true);
}

public sealed record WireGuardOpResult(bool Ok, string? Error = null)
{
    public static readonly WireGuardOpResult Success = new(true);
    public static WireGuardOpResult Fail(string error) => new(false, error);
}

// How to authenticate to a panel. A `wui_…` access token is preferred: W-UI treats it as a machine
// credential and skips the session cookie binding a browser login carries.
public sealed record WireGuardCredentials(string Url, string Username, string Password, string ApiToken = "")
{
    public bool UsesToken => !string.IsNullOrWhiteSpace(ApiToken);
}

// Talks to a W-UI panel on the shop's behalf.
//
// Written against the W-UI JSON API (`/api/...` under the panel's base path). Two things about that API
// shape this connector:
//
//   1. Auth. `POST /api/auth/login` returns a JWT that is ALSO bound to an HttpOnly cookie (`wui_bind`) —
//      both halves are required on every later request, so a cookie jar is kept per attempt. A `wui_`
//      access token needs neither: it goes straight in the Bearer header.
//   2. Customers, not accounts. `POST /api/clients` takes the customer once plus `interfaceIds`, and the
//      panel issues credentials on every listed tunnel — the same one-call-many-locations shape the V2Ray
//      connector gets from 3x-ui's `/panel/api/clients/add`. The subscription link comes from a second
//      call, `GET /api/clients/{id}/subscription`, which mints the token if the customer has none.
public interface IWireGuardPanelConnector
{
    Task<WireGuardTestResult> TestAsync(WireGuardProvider provider, WireGuardCredentials credentials, CancellationToken ct = default);

    Task<WireGuardInterfacesResult> ListInterfacesAsync(WireGuardProvider provider, WireGuardCredentials credentials, CancellationToken ct = default);

    // Creates the customer on exactly the given tunnels in one call, then reads back the subscription link.
    Task<WireGuardClientResult> AddClientAsync(WireGuardProvider provider, WireGuardCredentials credentials, WireGuardNewClient client, IReadOnlyList<int> interfaceIds, CancellationToken ct = default);

    // Live usage/expiry for one customer, by the id the panel assigned at creation.
    Task<WireGuardClientState> GetClientAsync(WireGuardProvider provider, WireGuardCredentials credentials, int clientId, CancellationToken ct = default);

    // Removes ONE customer and releases their addresses on every tunnel. Other customers are untouched.
    Task<WireGuardOpResult> DeleteClientAsync(WireGuardProvider provider, WireGuardCredentials credentials, int clientId, CancellationToken ct = default);

    static string? NormalizeUrl(string? raw)
    {
        var text = (raw ?? "").Trim();
        if (text.Length == 0) return null;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
        if (string.IsNullOrWhiteSpace(uri.Host)) return null;
        return text.TrimEnd('/');
    }

    // A month is 30 days in this shop and a year 365, so a duration in days maps straight to a fixed expiry.
    // 0 days = no expiry (null, W-UI's "never").
    static DateTimeOffset? ExpiryFromNow(int durationDays) =>
        durationDays <= 0 ? null : DateTimeOffset.UtcNow.AddDays(durationDays);

    // W-UI stores a customer's quota in bytes; the UI shows GB. 0 stays 0 (unlimited).
    static long GbToBytes(long gb) => gb <= 0 ? 0 : gb * 1024L * 1024L * 1024L;
}

public sealed class WireGuardPanelConnector : IWireGuardPanelConnector
{
    private readonly ILogger<WireGuardPanelConnector> _logger;
    public WireGuardPanelConnector(ILogger<WireGuardPanelConnector> logger) => _logger = logger;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // A fresh cookie jar per attempt: a username/password session on W-UI is a JWT plus a bound cookie, and
    // both have to travel together. Certificate validation is relaxed because the installer's default is a
    // certificate for the server's own IP, and this is the owner reaching their OWN server on a URL they
    // typed.
    private static HttpClient NewClient()
    {
        var client = new HttpClient(new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
            AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        })
        { Timeout = TimeSpan.FromSeconds(20) };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("Phonix/1.0 (+wui-connector)");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private sealed record Session(HttpClient Client, string BaseUrl);

    public async Task<WireGuardTestResult> TestAsync(WireGuardProvider provider, WireGuardCredentials creds, CancellationToken ct = default)
    {
        if (provider != WireGuardProvider.WUi)
            return WireGuardTestResult.Fail("این نوع پنل پشتیبانی نمی‌شود.");
        var baseUrl = IWireGuardPanelConnector.NormalizeUrl(creds.Url);
        if (baseUrl is null)
            return WireGuardTestResult.Fail("آدرس پنل معتبر نیست. نمونه: https://1.2.3.4:41873/webBasePath");
        if (!creds.UsesToken && (string.IsNullOrWhiteSpace(creds.Username) || string.IsNullOrWhiteSpace(creds.Password)))
            return WireGuardTestResult.Fail("توکن دسترسی یا نام کاربری و گذرواژه پنل را وارد کنید.");

        using var client = NewClient();
        try
        {
            var (session, error) = await OpenSessionAsync(client, baseUrl, creds, ct);
            if (error is not null) return WireGuardTestResult.Fail(error);

            var (ok, body, status) = await GetAsync(session!, "/api/interfaces", ct);
            if (!ok) return WireGuardTestResult.Fail(AuthOrStatusError(status, "ورود موفق بود اما خواندن تانل‌ها ممکن نشد"));

            // Best-effort: the version is decoration on the panel card, so a build that hides /api/system
            // still passes the test.
            var version = "";
            var (sysOk, sysBody, _) = await GetAsync(session!, "/api/system", ct);
            if (sysOk) version = ReadString(sysBody, "version");

            return WireGuardTestResult.Succeeded(ReadInterfaces(body).Count, version);
        }
        catch (Exception ex)
        {
            return WireGuardTestResult.Fail(FriendlyError(ex, baseUrl, ct));
        }
    }

    public async Task<WireGuardInterfacesResult> ListInterfacesAsync(WireGuardProvider provider, WireGuardCredentials creds, CancellationToken ct = default)
    {
        if (provider != WireGuardProvider.WUi)
            return WireGuardInterfacesResult.Fail("این نوع پنل پشتیبانی نمی‌شود.");
        var baseUrl = IWireGuardPanelConnector.NormalizeUrl(creds.Url);
        if (baseUrl is null) return WireGuardInterfacesResult.Fail("آدرس پنل معتبر نیست.");

        using var client = NewClient();
        try
        {
            var (session, error) = await OpenSessionAsync(client, baseUrl, creds, ct);
            if (error is not null) return WireGuardInterfacesResult.Fail(error);

            var (ok, body, status) = await GetAsync(session!, "/api/interfaces", ct);
            if (!ok) return WireGuardInterfacesResult.Fail(AuthOrStatusError(status, "خواندن تانل‌ها ممکن نشد"));

            return new WireGuardInterfacesResult(true, null, ReadInterfaces(body));
        }
        catch (Exception ex)
        {
            return WireGuardInterfacesResult.Fail(FriendlyError(ex, baseUrl, ct));
        }
    }

    public async Task<WireGuardClientResult> AddClientAsync(WireGuardProvider provider, WireGuardCredentials creds, WireGuardNewClient req, IReadOnlyList<int> interfaceIds, CancellationToken ct = default)
    {
        if (provider != WireGuardProvider.WUi)
            return WireGuardClientResult.Fail("این نوع پنل پشتیبانی نمی‌شود.");
        var baseUrl = IWireGuardPanelConnector.NormalizeUrl(creds.Url);
        if (baseUrl is null) return WireGuardClientResult.Fail("آدرس پنل معتبر نیست.");
        var name = (req.Name ?? "").Trim();
        if (name.Length == 0) return WireGuardClientResult.Fail("نام مشتری را وارد کنید.");
        if (interfaceIds is not { Count: > 0 }) return WireGuardClientResult.Fail("حداقل یک تانل (سرور) برای ساخت مشتری انتخاب کنید.");

        using var client = NewClient();
        try
        {
            var (session, error) = await OpenSessionAsync(client, baseUrl, creds, ct);
            if (error is not null) return WireGuardClientResult.Fail(error);

            // Only the tunnels the plan sells, intersected with what the panel actually has enabled, so a
            // stale mapping (a tunnel deleted on the panel) fails loudly rather than silently placing the
            // customer somewhere they weren't sold.
            var (ok, body, status) = await GetAsync(session!, "/api/interfaces", ct);
            if (!ok) return WireGuardClientResult.Fail(AuthOrStatusError(status, "خواندن تانل‌ها ممکن نشد"));
            var enabled = ReadInterfaces(body).Where(i => i.Enabled).Select(i => i.Id).ToHashSet();
            var targets = interfaceIds.Where(enabled.Contains).Distinct().ToList();
            if (targets.Count == 0)
                return WireGuardClientResult.Fail("تانل‌های انتخاب‌شده روی پنل پیدا نشدند یا غیرفعال‌اند.");

            // W-UI's CreateInput. `expiresAt` is a fixed date; with `startOnFirstUse` the panel wants
            // `durationDays` instead and starts the clock on the first handshake.
            var payload = new Dictionary<string, object?>
            {
                ["name"] = name,
                ["interfaceIds"] = targets,
                ["quotaBytes"] = IWireGuardPanelConnector.GbToBytes(req.TotalGb),
                ["deviceLimit"] = Math.Max(1, req.DeviceLimit),
                ["resetCycle"] = "none",
                ["note"] = "Phonix",
            };
            if (req.StartOnFirstUse && req.DurationDays > 0)
            {
                payload["startOnFirstUse"] = true;
                payload["durationDays"] = req.DurationDays;
            }
            else
            {
                var expiry = IWireGuardPanelConnector.ExpiryFromNow(req.DurationDays);
                if (expiry is not null) payload["expiresAt"] = expiry.Value.UtcDateTime.ToString("O");
            }

            using var resp = await session!.Client.PostAsync($"{baseUrl}/api/clients",
                new StringContent(JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, "application/json"), ct);
            var respBody = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                // The most useful failure is a duplicate name or an exhausted address pool — W-UI says which
                // in `error`, and the caller turns that into a message the operator can act on.
                return WireGuardClientResult.Fail(ReadError(respBody) ?? $"ساخت مشتری ناموفق بود (کد {(int)resp.StatusCode}).");

            var clientId = ReadInt(respBody, "id");
            if (clientId <= 0) return WireGuardClientResult.Fail("پنل مشتری را ساخت اما شناسه‌ای برنگرداند.");

            // The link the customer's app is pointed at. Minted here if the customer has none yet.
            var (subOk, subBody, subStatus) = await GetAsync(session, $"/api/clients/{clientId}/subscription", ct);
            if (!subOk)
            {
                _logger.LogWarning("Created W-UI client {Id} on {Url} but the subscription read answered {Status}.", clientId, baseUrl, subStatus);
                return new WireGuardClientResult(true, null, clientId, "", "", targets.Count);
            }

            return new WireGuardClientResult(true, null, clientId, ReadString(subBody, "token"), ReadString(subBody, "link"), targets.Count);
        }
        catch (Exception ex)
        {
            return WireGuardClientResult.Fail(FriendlyError(ex, baseUrl, ct));
        }
    }

    public async Task<WireGuardClientState> GetClientAsync(WireGuardProvider provider, WireGuardCredentials creds, int clientId, CancellationToken ct = default)
    {
        if (provider != WireGuardProvider.WUi)
            return WireGuardClientState.Fail("این نوع پنل پشتیبانی نمی‌شود.");
        var baseUrl = IWireGuardPanelConnector.NormalizeUrl(creds.Url);
        if (baseUrl is null) return WireGuardClientState.Fail("آدرس پنل معتبر نیست.");
        if (clientId <= 0) return WireGuardClientState.Fail("شناسه مشتری مشخص نیست.");

        using var client = NewClient();
        try
        {
            var (session, error) = await OpenSessionAsync(client, baseUrl, creds, ct);
            if (error is not null) return WireGuardClientState.Fail(error);

            var (ok, body, status) = await GetAsync(session!, $"/api/clients/{clientId}", ct);
            if (status == 404) return WireGuardClientState.Gone("این مشتری روی پنل پیدا نشد.");
            if (!ok) return WireGuardClientState.Fail(AuthOrStatusError(status, "خواندن وضعیت مشتری ممکن نشد"));

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            DateTimeOffset? expires = null;
            if (root.TryGetProperty("expiresAt", out var exp) && exp.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(exp.GetString(), out var parsed))
                expires = parsed;

            return new WireGuardClientState(
                true, null,
                UsedBytes: Num(root, "usedBytes"),
                QuotaBytes: Num(root, "quotaBytes"),
                ExpiresAt: expires,
                Status: ReadString(root, "status"),
                DeviceLimit: (int)Num(root, "deviceLimit"),
                OnlineNow: (int)Num(root, "onlineNow"));
        }
        catch (JsonException)
        {
            return WireGuardClientState.Fail("پاسخ پنل قابل خواندن نبود.");
        }
        catch (Exception ex)
        {
            return WireGuardClientState.Fail(FriendlyError(ex, baseUrl, ct));
        }
    }

    public async Task<WireGuardOpResult> DeleteClientAsync(WireGuardProvider provider, WireGuardCredentials creds, int clientId, CancellationToken ct = default)
    {
        if (provider != WireGuardProvider.WUi)
            return WireGuardOpResult.Fail("این نوع پنل پشتیبانی نمی‌شود.");
        var baseUrl = IWireGuardPanelConnector.NormalizeUrl(creds.Url);
        if (baseUrl is null) return WireGuardOpResult.Fail("آدرس پنل معتبر نیست.");
        if (clientId <= 0) return WireGuardOpResult.Fail("شناسه مشتری مشخص نیست.");

        using var client = NewClient();
        try
        {
            var (session, error) = await OpenSessionAsync(client, baseUrl, creds, ct);
            if (error is not null) return WireGuardOpResult.Fail(error);

            using var resp = await session!.Client.DeleteAsync($"{baseUrl}/api/clients/{clientId}", ct);
            // 404 is "already gone", which for a delete is the state we wanted.
            if (resp.StatusCode == HttpStatusCode.NotFound || resp.IsSuccessStatusCode) return WireGuardOpResult.Success;

            var body = await resp.Content.ReadAsStringAsync(ct);
            return WireGuardOpResult.Fail(ReadError(body) ?? $"حذف مشتری از پنل ناموفق بود (کد {(int)resp.StatusCode}).");
        }
        catch (Exception ex)
        {
            return WireGuardOpResult.Fail(FriendlyError(ex, baseUrl, ct));
        }
    }

    // ── Session ─────────────────────────────────────────────────────────────────────────────────────

    // With a `wui_` token there is nothing to establish. Otherwise POST /api/auth/login: the JWT it returns
    // goes in the Bearer header and the bind cookie it sets stays in the jar, and the panel needs both.
    private static async Task<(Session? session, string? error)> OpenSessionAsync(HttpClient client, string baseUrl, WireGuardCredentials creds, CancellationToken ct)
    {
        if (creds.UsesToken)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", creds.ApiToken.Trim());
            return (new Session(client, baseUrl), null);
        }

        var payload = JsonSerializer.Serialize(new { username = creds.Username.Trim(), password = creds.Password });
        using var resp = await client.PostAsync($"{baseUrl}/api/auth/login",
            new StringContent(payload, Encoding.UTF8, "application/json"), ct);

        if (resp.StatusCode == HttpStatusCode.NotFound)
            return (null, "مسیر ورود پنل پیدا نشد. آدرس و WebBasePath پنل را بررسی کنید.");
        if (resp.StatusCode == HttpStatusCode.TooManyRequests)
            return (null, "پنل به‌خاطر تلاش‌های ناموفق قبلی ورود را موقتاً قفل کرده است. چند دقیقه بعد دوباره تلاش کنید.");

        var body = await resp.Content.ReadAsStringAsync(ct);
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
            return (null, ReadError(body) ?? "نام کاربری یا گذرواژه پنل پذیرفته نشد.");
        if (!resp.IsSuccessStatusCode)
            return (null, ReadError(body) ?? $"پنل به درخواست ورود پاسخ نداد (کد {(int)resp.StatusCode}).");

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            // Two-factor is on for this administrator. The shop cannot answer a TOTP prompt, so the operator
            // has to use an access token instead — which is the right credential for a machine anyway.
            if (root.TryGetProperty("needCode", out var need) && need.ValueKind == JsonValueKind.True)
                return (null, "این کاربر پنل ورود دومرحله‌ای دارد. برای اتصال فروشگاه از توکن دسترسی (API Token) استفاده کنید.");
            var token = root.TryGetProperty("token", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(token))
                return (null, "پاسخ پنل قابل‌شناسایی نبود. آدرس پنل را بررسی کنید.");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        catch (JsonException)
        {
            return (null, "پاسخ پنل قابل‌شناسایی نبود. آدرس پنل را بررسی کنید.");
        }

        return (new Session(client, baseUrl), null);
    }

    private static async Task<(bool ok, string body, int status)> GetAsync(Session session, string path, CancellationToken ct)
    {
        using var resp = await session.Client.GetAsync($"{session.BaseUrl}{path}", ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        return (resp.IsSuccessStatusCode, body, (int)resp.StatusCode);
    }

    private static string AuthOrStatusError(int status, string what) => status switch
    {
        401 => "توکن دسترسی پنل پذیرفته نشد یا نشست منقضی شده است.",
        403 => "پنل این درخواست را رد کرد (۴۰۳). دسترسی توکن را بررسی کنید.",
        _ => $"{what} (کد {status}).",
    };

    private static string FriendlyError(Exception ex, string url, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return "درخواست لغو شد.";
        return ex switch
        {
            TaskCanceledException => "پنل در زمان مقرر پاسخ نداد. آدرس، پورت و دسترسی سرور را بررسی کنید.",
            HttpRequestException hre when hre.InnerException is System.Net.Sockets.SocketException =>
                "اتصال به پنل برقرار نشد. آدرس/پورت را بررسی کنید و مطمئن شوید فایروال سرور فروشگاه را مسدود نکرده است.",
            HttpRequestException hre => $"اتصال به پنل ناموفق بود: {hre.Message}",
            _ => $"خطای غیرمنتظره در اتصال به {url}: {ex.Message}",
        };
    }

    // ── JSON helpers ────────────────────────────────────────────────────────────────────────────────

    private static List<WireGuardInterface> ReadInterfaces(string body)
    {
        var list = new List<WireGuardInterface>();
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var id = (int)Num(el, "id");
                if (id <= 0) continue;
                list.Add(new WireGuardInterface(
                    id,
                    ReadString(el, "name"),
                    ReadString(el, "protocol"),
                    ReadString(el, "mode"),
                    (int)Num(el, "listenPort"),
                    ReadString(el, "endpointHost"),
                    Enabled: !el.TryGetProperty("enabled", out var en) || en.ValueKind != JsonValueKind.False,
                    Running: el.TryGetProperty("running", out var run) && run.ValueKind == JsonValueKind.True,
                    Clients: Num(el, "clients"),
                    Devices: Num(el, "devices"),
                    Allocated: (int)Num(el, "allocated"),
                    Capacity: (int)Num(el, "capacity"),
                    NodeName: ReadString(el, "nodeName")));
            }
        }
        catch (JsonException) { /* return what we have */ }
        return list;
    }

    private static string? ReadError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var msg = ReadString(doc.RootElement, "error");
            return msg.Length > 0 ? msg : null;
        }
        catch (JsonException) { return null; }
    }

    private static string ReadString(string body, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return ReadString(doc.RootElement, name);
        }
        catch (JsonException) { return ""; }
    }

    private static int ReadInt(string body, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return (int)Num(doc.RootElement, name);
        }
        catch (JsonException) { return 0; }
    }

    private static string ReadString(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static long Num(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;
}

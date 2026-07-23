using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Phonix.Api.Models;

namespace Phonix.Api.Services;

public sealed record V2RayTestResult(bool Ok, string? Error = null, int InboundCount = 0)
{
    public static V2RayTestResult Succeeded(int inbounds) => new(true, null, inbounds);
    public static V2RayTestResult Fail(string error) => new(false, error);
}

// A client to create, in the shop's own terms. Zero means "unlimited" everywhere, matching the panel: 0
// traffic, 0 IPs and 0 days (a blank expiry) all mean no limit.
public sealed record V2RayNewClient(
    string Email,        // the unique label the account is created under (the name the customer chose)
    long TotalGb,        // 0 = unlimited
    int LimitIp,         // 0 = unlimited
    int DurationDays,    // 0 = never expires; otherwise a fixed calendar expiry this many days out
    string Flow = "");   // usually empty; set only for inbounds that require an XTLS flow

public sealed record V2RayClientResult(bool Ok, string? Error = null, string Uuid = "", string SubId = "", int InboundsAdded = 0)
{
    public static V2RayClientResult Fail(string error) => new(false, error);
}

public sealed record V2RayInbound(int Id, string Remark, string Protocol, int Port, bool Enable, int ClientCount);

public sealed record V2RayInboundsResult(bool Ok, string? Error = null, IReadOnlyList<V2RayInbound>? Inbounds = null)
{
    public static V2RayInboundsResult Fail(string error) => new(false, error);
}

// Talks to a V2Ray management panel on the shop's behalf.
//
// Written against the Sanaei 3x-ui fork, v3.4.x. Two things about that version drive this design and are
// easy to get wrong:
//
//   1. CSRF. Every non-safe request (POST/PUT/DELETE) must carry an X-CSRF-Token obtained from
//      GET /csrf-token, alongside X-Requested-With: XMLHttpRequest. Without it the panel answers 403
//      BEFORE it ever looks at the credentials — which reads exactly like a wrong password but isn't.
//   2. The client API was reshaped. Older forks added a client per inbound via
//      /panel/api/inbounds/addClient with a stringified `settings` blob. 3.4.x has a dedicated
//      /panel/api/clients/add that takes the client once plus an `inboundIds` ARRAY — so one call places the
//      account on exactly the locations a plan sells.
public interface IV2RayPanelConnector
{
    Task<V2RayTestResult> TestAsync(V2RayProvider provider, string url, string username, string password, CancellationToken ct = default);

    Task<V2RayInboundsResult> ListInboundsAsync(V2RayProvider provider, string url, string username, string password, CancellationToken ct = default);

    // Creates the account on exactly the given inbounds in a single call.
    Task<V2RayClientResult> AddClientAsync(V2RayProvider provider, string url, string username, string password, V2RayNewClient client, IReadOnlyList<int> inboundIds, CancellationToken ct = default);

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
    // 0 days = no expiry (0, the panel's "unlimited").
    static long ExpiryMsFromNow(int durationDays) =>
        durationDays <= 0 ? 0 : DateTimeOffset.UtcNow.AddDays(durationDays).ToUnixTimeMilliseconds();

    // The panel stores a client's traffic cap in bytes; the UI shows GB. 0 stays 0 (unlimited).
    static long GbToBytes(long gb) => gb <= 0 ? 0 : gb * 1024L * 1024L * 1024L;
}

public sealed class V2RayPanelConnector : IV2RayPanelConnector
{
    private readonly ILogger<V2RayPanelConnector> _logger;
    public V2RayPanelConnector(ILogger<V2RayPanelConnector> logger) => _logger = logger;

    // A fresh cookie jar per attempt. Certificate validation is relaxed because panels are commonly served
    // over a self-signed cert or a bare IP, and this is the owner reaching their OWN server on a URL they
    // typed. The headers mirror what the panel's own frontend sends — a real User-Agent as well, since a
    // proxy or WAF in front of the panel will often reject a request that has none.
    private static HttpClient NewClient()
    {
        var client = new HttpClient(new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
            AllowAutoRedirect = true,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        })
        { Timeout = TimeSpan.FromSeconds(20) };

        client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Phonix/1.0 (+panel-connector)");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    // One authenticated session: the logged-in HttpClient plus the CSRF token its POSTs must carry.
    private sealed record Session(HttpClient Client, string BaseUrl, string Csrf);

    public async Task<V2RayTestResult> TestAsync(V2RayProvider provider, string url, string username, string password, CancellationToken ct = default)
    {
        if (provider != V2RayProvider.Sanaei)
            return V2RayTestResult.Fail("این نوع پنل هنوز پشتیبانی نمی‌شود.");
        var baseUrl = IV2RayPanelConnector.NormalizeUrl(url);
        if (baseUrl is null)
            return V2RayTestResult.Fail("آدرس پنل معتبر نیست. نمونه: https://sub.example.com:8080/webpath");
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return V2RayTestResult.Fail("نام کاربری و گذرواژه پنل را وارد کنید.");

        using var client = NewClient();
        try
        {
            var (session, error) = await OpenSessionAsync(client, baseUrl, username, password, ct);
            if (error is not null) return V2RayTestResult.Fail(error);

            var (ok, body, status) = await GetInboundsAsync(session!, ct);
            if (!ok) return V2RayTestResult.Fail($"ورود موفق بود اما خواندن اینباندها ممکن نشد (کد {status}).");

            return V2RayTestResult.Succeeded(ReadInbounds(body).Count);
        }
        catch (Exception ex)
        {
            return V2RayTestResult.Fail(FriendlyError(ex, baseUrl, ct));
        }
    }

    public async Task<V2RayInboundsResult> ListInboundsAsync(V2RayProvider provider, string url, string username, string password, CancellationToken ct = default)
    {
        if (provider != V2RayProvider.Sanaei)
            return V2RayInboundsResult.Fail("این نوع پنل هنوز پشتیبانی نمی‌شود.");
        var baseUrl = IV2RayPanelConnector.NormalizeUrl(url);
        if (baseUrl is null) return V2RayInboundsResult.Fail("آدرس پنل معتبر نیست.");

        using var client = NewClient();
        try
        {
            var (session, error) = await OpenSessionAsync(client, baseUrl, username, password, ct);
            if (error is not null) return V2RayInboundsResult.Fail(error);

            var (ok, body, status) = await GetInboundsAsync(session!, ct);
            if (!ok) return V2RayInboundsResult.Fail($"خواندن اینباندها ممکن نشد (کد {status}).");

            return new V2RayInboundsResult(true, null, ReadInbounds(body));
        }
        catch (Exception ex)
        {
            return V2RayInboundsResult.Fail(FriendlyError(ex, baseUrl, ct));
        }
    }

    public async Task<V2RayClientResult> AddClientAsync(V2RayProvider provider, string url, string username, string password, V2RayNewClient req, IReadOnlyList<int> inboundIds, CancellationToken ct = default)
    {
        if (provider != V2RayProvider.Sanaei)
            return V2RayClientResult.Fail("این نوع پنل هنوز پشتیبانی نمی‌شود.");
        var baseUrl = IV2RayPanelConnector.NormalizeUrl(url);
        if (baseUrl is null) return V2RayClientResult.Fail("آدرس پنل معتبر نیست.");
        var email = (req.Email ?? "").Trim();
        if (email.Length == 0) return V2RayClientResult.Fail("نام (Email) اکانت را وارد کنید.");

        using var client = NewClient();
        try
        {
            var (session, error) = await OpenSessionAsync(client, baseUrl, username, password, ct);
            if (error is not null) return V2RayClientResult.Fail(error);

            var (ok, body, status) = await GetInboundsAsync(session!, ct);
            if (!ok) return V2RayClientResult.Fail($"خواندن اینباندها ممکن نشد (کد {status}).");

            var enabled = ReadInbounds(body).Where(i => i.Enable).Select(i => i.Id).ToList();
            if (enabled.Count == 0) return V2RayClientResult.Fail("هیچ اینباند فعالی روی پنل پیدا نشد.");

            // Only the inbounds the plan sells. Intersected with what the panel actually has enabled, so a
            // stale mapping (an inbound deleted on the panel) fails loudly rather than silently placing the
            // account somewhere it wasn't sold on.
            List<int> targets;
            if (inboundIds is { Count: > 0 })
            {
                targets = inboundIds.Where(enabled.Contains).Distinct().ToList();
                if (targets.Count == 0)
                    return V2RayClientResult.Fail("اینباندهای انتخاب‌شده روی پنل پیدا نشدند یا غیرفعال‌اند.");
            }
            else
            {
                targets = enabled;
            }

            var uuid = Guid.NewGuid().ToString();
            var subId = RandomToken(16);

            // 3.4.x takes the client once plus the inbound ids — one call for every location.
            var payload = JsonSerializer.Serialize(new
            {
                client = new Dictionary<string, object?>
                {
                    // Exact panel field names (note totalGB's capital GB), hence a dictionary rather than a
                    // camelCased anonymous type.
                    ["id"] = uuid,
                    ["email"] = email,
                    ["enable"] = true,
                    ["totalGB"] = IV2RayPanelConnector.GbToBytes(req.TotalGb),
                    ["expiryTime"] = IV2RayPanelConnector.ExpiryMsFromNow(req.DurationDays),
                    ["limitIp"] = Math.Max(0, req.LimitIp),
                    ["flow"] = req.Flow ?? "",
                    ["subId"] = subId,
                    ["tgId"] = "",
                    ["comment"] = "",
                },
                inboundIds = targets,
            });

            using var resp = await PostAsync(session!, "/panel/api/clients/add",
                () => new StringContent(payload, Encoding.UTF8, "application/json"), ct);
            var respBody = await resp.Content.ReadAsStringAsync(ct);

            TryReadSuccess(respBody, out var success, out var msg);
            if (!resp.IsSuccessStatusCode || !success)
                // The most useful failure is a duplicate email — the panel rejects it, and the caller turns
                // that into "ask the customer for a different name".
                return V2RayClientResult.Fail(string.IsNullOrWhiteSpace(msg)
                    ? $"ساخت اکانت ناموفق بود (کد {(int)resp.StatusCode})."
                    : msg);

            return new V2RayClientResult(true, null, uuid, subId, targets.Count);
        }
        catch (Exception ex)
        {
            return V2RayClientResult.Fail(FriendlyError(ex, baseUrl, ct));
        }
    }

    // ── Session: CSRF + login ───────────────────────────────────────────────────────────────────────

    // Fetches a CSRF token, then signs in. Returns the live session, or a ready-to-show Persian error.
    private async Task<(Session? session, string? error)> OpenSessionAsync(HttpClient client, string baseUrl, string username, string password, CancellationToken ct)
    {
        var csrf = await FetchCsrfAsync(client, baseUrl, ct);
        var session = new Session(client, baseUrl, csrf ?? "");

        using var resp = await PostAsync(session, "/login", () => new FormUrlEncodedContent(
            new Dictionary<string, string> { ["username"] = username, ["password"] = password }), ct);

        if (resp.StatusCode == HttpStatusCode.NotFound)
            return (null, "مسیر ورود پنل پیدا نشد. آدرس و وب‌پس پنل را بررسی کنید.");
        if (resp.StatusCode == HttpStatusCode.Forbidden)
            return (null, "پنل درخواست ورود را رد کرد (۴۰۳). اگر پنل پشت کلادفلر/پراکسی است یا محدودیت IP دارد، دسترسی سرور را باز کنید.");

        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            return (null, $"پنل به درخواست ورود پاسخ نداد (کد {(int)resp.StatusCode}).");
        if (!TryReadSuccess(body, out var ok, out var msg))
            return (null, "پاسخ پنل قابل‌شناسایی نبود. آدرس پنل را بررسی کنید.");
        if (!ok)
            return (null, string.IsNullOrWhiteSpace(msg) ? "نام کاربری یا گذرواژه پنل پذیرفته نشد." : msg);

        // The login response rotates the token in some builds; refresh so later POSTs use a current one.
        var after = await FetchCsrfAsync(client, baseUrl, ct);
        return (session with { Csrf = after ?? session.Csrf }, null);
    }

    private static async Task<string?> FetchCsrfAsync(HttpClient client, string baseUrl, CancellationToken ct)
    {
        try
        {
            using var resp = await client.GetAsync($"{baseUrl}/csrf-token", ct);
            if (!resp.IsSuccessStatusCode) return null;
            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("obj", out var obj) && obj.ValueKind == JsonValueKind.String)
                return obj.GetString();
        }
        catch (Exception)
        {
            // An older build without the endpoint simply has no token; the POST then succeeds without one.
        }
        return null;
    }

    // POST with the CSRF header, retrying ONCE with a freshly fetched token on a 403 — the same recovery the
    // panel's own frontend performs when its token has expired.
    private static async Task<HttpResponseMessage> PostAsync(Session session, string path, Func<HttpContent> content, CancellationToken ct)
    {
        var resp = await SendAsync(session, path, content(), session.Csrf, ct);
        if (resp.StatusCode != HttpStatusCode.Forbidden) return resp;

        resp.Dispose();
        var fresh = await FetchCsrfAsync(session.Client, session.BaseUrl, ct);
        return await SendAsync(session, path, content(), fresh ?? session.Csrf, ct);
    }

    private static Task<HttpResponseMessage> SendAsync(Session session, string path, HttpContent content, string csrf, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{session.BaseUrl}{path}") { Content = content };
        if (!string.IsNullOrEmpty(csrf)) request.Headers.TryAddWithoutValidation("X-CSRF-Token", csrf);
        return session.Client.SendAsync(request, ct);
    }

    private static async Task<(bool ok, string body, int status)> GetInboundsAsync(Session session, CancellationToken ct)
    {
        using var resp = await session.Client.GetAsync($"{session.BaseUrl}/panel/api/inbounds/list", ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        return (resp.IsSuccessStatusCode, body, (int)resp.StatusCode);
    }

    private string FriendlyError(Exception ex, string baseUrl, CancellationToken ct)
    {
        if (ex is TaskCanceledException && !ct.IsCancellationRequested)
            return "پنل در زمان مقرر پاسخ نداد (timeout). آدرس و در دسترس بودن سرور را بررسی کنید.";
        if (ex is HttpRequestException)
        {
            _logger.LogWarning(ex, "V2Ray panel request failed to reach {Url}", baseUrl);
            return "اتصال به پنل ممکن نشد. آدرس، پورت و روشن بودن سرور را بررسی کنید.";
        }
        _logger.LogError(ex, "Unexpected error talking to V2Ray panel {Url}", baseUrl);
        return $"خطای غیرمنتظره در اتصال به پنل: {ex.Message}";
    }

    // ── Response parsing ────────────────────────────────────────────────────────────────────────────

    // The panel wraps every reply in {"success":bool,"msg":string,"obj":…}. Parse defensively: an
    // unrecognized shape is reported as failure, never mistaken for success.
    private static bool TryReadSuccess(string body, out bool success, out string msg)
    {
        success = false;
        msg = "";
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("success", out var s))
                return false;
            success = s.ValueKind == JsonValueKind.True || (s.ValueKind == JsonValueKind.String && bool.TryParse(s.GetString(), out var b) && b);
            if (root.TryGetProperty("msg", out var m) && m.ValueKind == JsonValueKind.String)
                msg = m.GetString() ?? "";
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static List<V2RayInbound> ReadInbounds(string body)
    {
        var list = new List<V2RayInbound>();
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("obj", out var obj) || obj.ValueKind != JsonValueKind.Array)
                return list;

            foreach (var el in obj.EnumerateArray())
            {
                if (!el.TryGetProperty("id", out var idEl) || !idEl.TryGetInt32(out var id)) continue;
                var remark = el.TryGetProperty("remark", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() ?? "" : "";
                var protocol = el.TryGetProperty("protocol", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
                var port = el.TryGetProperty("port", out var po) && po.TryGetInt32(out var n) ? n : 0;
                var enable = !el.TryGetProperty("enable", out var en) || en.ValueKind != JsonValueKind.False;
                list.Add(new V2RayInbound(id, remark, protocol, port, enable, CountClients(el)));
            }
        }
        catch (JsonException) { /* return what we have */ }
        return list;
    }

    private static int CountClients(JsonElement inbound)
    {
        if (!inbound.TryGetProperty("settings", out var s) || s.ValueKind != JsonValueKind.String) return 0;
        try
        {
            using var settings = JsonDocument.Parse(s.GetString() ?? "{}");
            foreach (var key in new[] { "clients", "peers" })
                if (settings.RootElement.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array)
                    return arr.GetArrayLength();
        }
        catch (JsonException) { /* ignore */ }
        return 0;
    }

    private static string RandomToken(int length)
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(length);
        var sb = new StringBuilder(length);
        foreach (var b in bytes) sb.Append(alphabet[b % alphabet.Length]);
        return sb.ToString();
    }
}

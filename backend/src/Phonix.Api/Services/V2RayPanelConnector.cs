using System.Net;
using System.Text;
using System.Text.Json;
using Phonix.Api.Models;

namespace Phonix.Api.Services;

public sealed record V2RayTestResult(bool Ok, string? Error = null, int InboundCount = 0)
{
    public static V2RayTestResult Succeeded(int inbounds) => new(true, null, inbounds);
    public static V2RayTestResult Fail(string error) => new(false, error);
}

// A client to create, in the shop's own terms — the connector translates these into the panel's client
// object. Zero means "unlimited" everywhere, matching the panel's own convention: 0 traffic, 0 IPs, and 0
// days (a blank expiry) all mean no limit.
public sealed record V2RayNewClient(
    string Email,        // the unique label the account is created under (the name the customer chose)
    long TotalGb,        // 0 = unlimited
    int LimitIp,         // 0 = unlimited
    int DurationDays,    // 0 = never expires; otherwise a fixed calendar expiry this many days out
    string Flow = "");   // usually left empty; set only for inbounds that require an XTLS flow

public sealed record V2RayClientResult(bool Ok, string? Error = null, string Uuid = "", string SubId = "", int InboundsAdded = 0)
{
    public static V2RayClientResult Fail(string error) => new(false, error);
}

// Talks to a V2Ray management panel on the shop's behalf: test connectivity, and create the account a
// customer bought. Everything is written against the Sanaei 3x-ui fork; other providers return a clear
// "not supported yet" so the store can list them as coming-soon without pretending they work.
public interface IV2RayPanelConnector
{
    Task<V2RayTestResult> TestAsync(V2RayProvider provider, string url, string username, string password, CancellationToken ct = default);

    // Creates the client on EVERY enabled inbound of the panel (the "select all" the operator would do by
    // hand), all sharing one UUID and one subscription id so a single sub link covers every location.
    Task<V2RayClientResult> AddClientAsync(V2RayProvider provider, string url, string username, string password, V2RayNewClient client, CancellationToken ct = default);

    // Parses and normalizes an entered panel URL, or returns null when it is not a usable http(s) URL.
    static string? NormalizeUrl(string? raw)
    {
        var text = (raw ?? "").Trim();
        if (text.Length == 0) return null;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
        if (string.IsNullOrWhiteSpace(uri.Host)) return null;
        return text.TrimEnd('/');
    }

    // A month is 30 days in this shop and a year is 365 (see the plan rules), so a duration in days maps
    // straight to a fixed expiry timestamp. 0 days = no expiry (0, the panel's "unlimited").
    static long ExpiryMsFromNow(int durationDays) =>
        durationDays <= 0 ? 0 : DateTimeOffset.UtcNow.AddDays(durationDays).ToUnixTimeMilliseconds();

    // The panel stores a client's traffic cap in bytes; the UI shows GB. 0 stays 0 (unlimited).
    static long GbToBytes(long gb) => gb <= 0 ? 0 : gb * 1024L * 1024L * 1024L;
}

public sealed class V2RayPanelConnector : IV2RayPanelConnector
{
    private readonly ILogger<V2RayPanelConnector> _logger;
    public V2RayPanelConnector(ILogger<V2RayPanelConnector> logger) => _logger = logger;

    // A fresh cookie jar per attempt; certificate validation relaxed because panels are commonly served over
    // a self-signed cert or a bare IP and this is the owner reaching their OWN server on a URL they typed.
    private static HttpClient NewClient() =>
        new(new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
            AllowAutoRedirect = true,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        })
        { Timeout = TimeSpan.FromSeconds(20) };

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
            var login = await LoginAsync(client, baseUrl, username, password, ct);
            if (login is not null) return V2RayTestResult.Fail(login);

            var (ok, body, status) = await GetInboundsAsync(client, baseUrl, ct);
            if (!ok)
                return V2RayTestResult.Fail($"ورود موفق بود اما خواندن اینباندها ممکن نشد (کد {status}).");

            return V2RayTestResult.Succeeded(CountInbounds(body));
        }
        catch (Exception ex)
        {
            return V2RayTestResult.Fail(FriendlyError(ex, baseUrl, ct));
        }
    }

    public async Task<V2RayClientResult> AddClientAsync(V2RayProvider provider, string url, string username, string password, V2RayNewClient req, CancellationToken ct = default)
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
            var login = await LoginAsync(client, baseUrl, username, password, ct);
            if (login is not null) return V2RayClientResult.Fail(login);

            var (ok, body, status) = await GetInboundsAsync(client, baseUrl, ct);
            if (!ok) return V2RayClientResult.Fail($"خواندن اینباندها ممکن نشد (کد {status}).");

            var inbounds = ReadEnabledInboundIds(body);
            if (inbounds.Count == 0)
                return V2RayClientResult.Fail("هیچ اینباند فعالی روی پنل پیدا نشد.");

            // One identity shared across every inbound: the same UUID and the same subscription id, so a
            // single sub link returned to the customer covers all their locations.
            var uuid = Guid.NewGuid().ToString();
            var subId = RandomToken(16);
            var expiry = IV2RayPanelConnector.ExpiryMsFromNow(req.DurationDays);
            var totalBytes = IV2RayPanelConnector.GbToBytes(req.TotalGb);

            var added = 0;
            string? lastError = null;
            foreach (var inboundId in inbounds)
            {
                var settings = JsonSerializer.Serialize(new
                {
                    clients = new[]
                    {
                        // Exact panel field names (note totalGB's capital GB), so a Dictionary rather than a
                        // camelCased anonymous type.
                        new Dictionary<string, object?>
                        {
                            ["id"] = uuid,
                            ["email"] = email,
                            ["enable"] = true,
                            ["totalGB"] = totalBytes,
                            ["expiryTime"] = expiry,
                            ["limitIp"] = Math.Max(0, req.LimitIp),
                            ["flow"] = req.Flow ?? "",
                            ["subId"] = subId,
                            ["tgId"] = "",
                            ["reset"] = 0,
                        },
                    },
                });
                var payload = JsonSerializer.Serialize(new { id = inboundId, settings });

                using var resp = await client.PostAsync($"{baseUrl}/panel/api/inbounds/addClient",
                    new StringContent(payload, Encoding.UTF8, "application/json"), ct);
                var respBody = await resp.Content.ReadAsStringAsync(ct);

                // Parsed unconditionally so `msg` is always assigned (the panel returns its reason in it).
                TryReadSuccess(respBody, out var s, out var msg);
                if (resp.IsSuccessStatusCode && s)
                    added++;
                else
                    // The most useful failure is a duplicate email — the panel rejects it, and the caller turns
                    // that into "ask the customer for a different name".
                    lastError = string.IsNullOrWhiteSpace(msg) ? $"افزودن روی اینباند {inboundId} ناموفق بود." : msg;
            }

            if (added == 0)
                return V2RayClientResult.Fail(lastError ?? "ساخت اکانت روی هیچ اینباندی موفق نبود.");

            return new V2RayClientResult(true, null, uuid, subId, added);
        }
        catch (Exception ex)
        {
            return V2RayClientResult.Fail(FriendlyError(ex, baseUrl, ct));
        }
    }

    // ── Panel plumbing ──────────────────────────────────────────────────────────────────────────────

    // Returns null on a successful login, or a ready-to-show Persian error otherwise.
    private static async Task<string?> LoginAsync(HttpClient client, string baseUrl, string username, string password, CancellationToken ct)
    {
        using var resp = await client.PostAsync($"{baseUrl}/login",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["username"] = username, ["password"] = password }), ct);

        if (resp.StatusCode == HttpStatusCode.NotFound)
            return "مسیر ورود پنل پیدا نشد. آدرس و وب‌پس پنل را بررسی کنید.";
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            return $"پنل به درخواست ورود پاسخ نداد (کد {(int)resp.StatusCode}).";
        if (!TryReadSuccess(body, out var ok, out var msg))
            return "پاسخ پنل قابل‌شناسایی نبود. آدرس پنل را بررسی کنید.";
        if (!ok)
            return string.IsNullOrWhiteSpace(msg) ? "نام کاربری یا گذرواژه پنل پذیرفته نشد." : msg;
        return null;
    }

    private static async Task<(bool ok, string body, int status)> GetInboundsAsync(HttpClient client, string baseUrl, CancellationToken ct)
    {
        using var resp = await client.GetAsync($"{baseUrl}/panel/api/inbounds/list", ct);
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

    // 3x-ui wraps every reply in {"success":bool,"msg":string,"obj":…}. Parse defensively: an unrecognized
    // shape is reported as failure, never mistaken for success.
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

    private static int CountInbounds(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("obj", out var obj) && obj.ValueKind == JsonValueKind.Array)
                return obj.GetArrayLength();
        }
        catch (JsonException) { /* fall through */ }
        return 0;
    }

    // The ids of every inbound whose "enable" is true — the ones a "select all" would target.
    private static List<int> ReadEnabledInboundIds(string body)
    {
        var ids = new List<int>();
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("obj", out var obj) || obj.ValueKind != JsonValueKind.Array)
                return ids;
            foreach (var el in obj.EnumerateArray())
            {
                var enabled = !el.TryGetProperty("enable", out var en) || en.ValueKind != JsonValueKind.False;
                if (enabled && el.TryGetProperty("id", out var id) && id.TryGetInt32(out var n))
                    ids.Add(n);
            }
        }
        catch (JsonException) { /* return what we have */ }
        return ids;
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

using System.Net;
using System.Text.Json;
using Phonix.Api.Models;

namespace Phonix.Api.Services;

public sealed record V2RayTestResult(bool Ok, string? Error = null, int InboundCount = 0)
{
    public static V2RayTestResult Succeeded(int inbounds) => new(true, null, inbounds);
    public static V2RayTestResult Fail(string error) => new(false, error);
}

// Talks to a V2Ray management panel on the shop's behalf. The only operation for now is "log in and confirm
// the panel answers" — the foundation the account-provisioning calls will build on. Everything is written
// against the Sanaei 3x-ui fork; other providers throw a clear "not supported yet" until their own connector
// is added, so the store can list them as coming-soon without pretending they work.
public interface IV2RayPanelConnector
{
    // Signs in with the panel's admin credentials and verifies the session by reading its inbound list.
    // Returns a typed result (never throws for an operational failure) so the UI can show a precise reason.
    Task<V2RayTestResult> TestAsync(V2RayProvider provider, string url, string username, string password, CancellationToken ct = default);

    // Parses and normalizes an entered panel URL, or returns null when it is not a usable http(s) URL.
    // Exposed so the controller can reject a bad URL before anything is stored.
    static string? NormalizeUrl(string? raw)
    {
        var text = (raw ?? "").Trim();
        if (text.Length == 0) return null;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
        if (string.IsNullOrWhiteSpace(uri.Host)) return null;
        // Drop a trailing slash so route concatenation ("/login", "/panel/api/…") is unambiguous. Keep the
        // rest of the path (the panel's secret webpath) exactly as given.
        return text.TrimEnd('/');
    }
}

public sealed class V2RayPanelConnector : IV2RayPanelConnector
{
    private readonly ILogger<V2RayPanelConnector> _logger;
    public V2RayPanelConnector(ILogger<V2RayPanelConnector> logger) => _logger = logger;

    public async Task<V2RayTestResult> TestAsync(V2RayProvider provider, string url, string username, string password, CancellationToken ct = default)
    {
        if (provider != V2RayProvider.Sanaei)
            return V2RayTestResult.Fail("این نوع پنل هنوز پشتیبانی نمی‌شود.");

        var baseUrl = IV2RayPanelConnector.NormalizeUrl(url);
        if (baseUrl is null)
            return V2RayTestResult.Fail("آدرس پنل معتبر نیست. نمونه: https://sub.example.com:8080/webpath");
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return V2RayTestResult.Fail("نام کاربری و گذرواژه پنل را وارد کنید.");

        // A fresh cookie jar per attempt: the login response sets the panel's session cookie and the inbound
        // read must carry it back. Certificate validation is intentionally relaxed — panels are very commonly
        // served over a self-signed cert or a bare IP, and this is the owner connecting to their OWN server on
        // a URL they typed, not a third party we need to authenticate.
        using var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
            AllowAutoRedirect = true,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };

        try
        {
            // 3x-ui login is a form POST to <base>/login; it answers {"success":bool,"msg":string} and sets
            // the session cookie on success.
            using var loginResp = await client.PostAsync(
                $"{baseUrl}/login",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["username"] = username, ["password"] = password }),
                ct);

            if (loginResp.StatusCode == HttpStatusCode.NotFound)
                return V2RayTestResult.Fail("مسیر ورود پنل پیدا نشد. آدرس و وب‌پس پنل را بررسی کنید.");

            var loginBody = await loginResp.Content.ReadAsStringAsync(ct);
            if (!loginResp.IsSuccessStatusCode)
                return V2RayTestResult.Fail($"پنل به درخواست ورود پاسخ نداد (کد {(int)loginResp.StatusCode}).");

            if (!TryReadSuccess(loginBody, out var ok, out var msg))
                // A non-JSON body here almost always means the URL points at something that isn't a panel
                // (a reverse proxy, a login HTML page, the wrong path).
                return V2RayTestResult.Fail("پاسخ پنل قابل‌شناسایی نبود. آدرس پنل را بررسی کنید.");
            if (!ok)
                return V2RayTestResult.Fail(string.IsNullOrWhiteSpace(msg) ? "نام کاربری یا گذرواژه پنل پذیرفته نشد." : msg);

            // Prove the session actually works by reading the inbound list — the same call provisioning will
            // rely on. A login that "succeeds" but can't read inbounds is not a usable connection.
            using var listResp = await client.GetAsync($"{baseUrl}/panel/api/inbounds/list", ct);
            var listBody = await listResp.Content.ReadAsStringAsync(ct);
            if (!listResp.IsSuccessStatusCode)
                return V2RayTestResult.Fail($"ورود موفق بود اما خواندن اینباندها ممکن نشد (کد {(int)listResp.StatusCode}).");

            var inbounds = CountInbounds(listBody);
            return V2RayTestResult.Succeeded(inbounds);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return V2RayTestResult.Fail("پنل در زمان مقرر پاسخ نداد (timeout). آدرس و در دسترس بودن سرور را بررسی کنید.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "V2Ray panel test failed to reach {Url}", baseUrl);
            return V2RayTestResult.Fail("اتصال به پنل ممکن نشد. آدرس، پورت و روشن بودن سرور را بررسی کنید.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error testing V2Ray panel {Url}", baseUrl);
            return V2RayTestResult.Fail($"خطای غیرمنتظره در اتصال به پنل: {ex.Message}");
        }
    }

    // 3x-ui wraps every API reply in {"success":bool,"msg":string,"obj":…}. Parse defensively: any shape we
    // don't recognize is reported as "unreadable" rather than mistaken for success.
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
}

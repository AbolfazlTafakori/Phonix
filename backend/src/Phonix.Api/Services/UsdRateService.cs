using System.Globalization;
using System.Text;
using System.Text.Json;
using Phonix.Api.Data;

namespace Phonix.Api.Services;

// Fetches the live USDT→Toman price from Nobitex on a fixed cadence and, after each successful refresh, asks
// the store to recompute the Toman price of every USD-priced product. Downstream (cart, checkout, display)
// keeps reading the stored Toman price, so the authoritative charge is always at a current rate without any
// per-request conversion. The last good rate is kept across failures.
public sealed class UsdRateService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    // Nobitex serves the same API from more than one host, and they do not stay up together: api.nobitex.ir
    // stopped resolving at all - NXDOMAIN from Google's and Cloudflare's resolvers alike, so not a block on
    // our address - while apiv2.nobitex.ir answers normally. A single hard-coded host therefore took the rate
    // down silently and left every USD-priced product on the manual fallback. These are tried in order and
    // the first that answers wins, so either host disappearing again costs one failed request, not the rate.
    private static readonly string[] StatsEndpoints =
    {
        "https://apiv2.nobitex.ir/market/stats",
        "https://api.nobitex.ir/market/stats",
    };

    private readonly IHttpClientFactory _http;
    private readonly IDataStore _store;
    private readonly ILogger<UsdRateService> _log;

    private long _nobitexToman;    // last live value from Nobitex, 0 until the first successful fetch
    private long _updatedAtUnixMs;
    private volatile string _lastError = ""; // why the last auto-fetch failed (shown in the admin panel)

    public UsdRateService(IHttpClientFactory http, IDataStore store, ILogger<UsdRateService> log)
    {
        _http = http;
        _store = store;
        _log = log;
    }

    public long NobitexToman => Interlocked.Read(ref _nobitexToman);
    public long UpdatedAtUnixMs => Interlocked.Read(ref _updatedAtUnixMs);
    public string LastError => _lastError;

    // The rate everything actually prices against: the live Nobitex value in auto mode (falling back to the
    // manual rate when Nobitex is unreachable), or the manual rate in manual mode.
    public long TomanPerUsd
    {
        get
        {
            var s = _store.GetSettings();
            var live = NobitexToman;
            if (s.UsdRateAuto && live > 0) return live;
            return s.ManualUsdRate;
        }
    }

    // Re-prices USD products/plans against the current effective rate. Call after the live rate refreshes or
    // the admin changes the manual rate/mode.
    public void ApplyCurrent() => _store.ApplyUsdRate(TomanPerUsd);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RefreshAsync(stoppingToken);
        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await RefreshAsync(stoppingToken);
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    // Pulls the latest USDT price (in Rial) from Nobitex, converts to Toman, publishes it and re-prices USD
    // products. Every known host is tried before giving up. Returns false (keeping the previous value) when
    // none of them answers with a usable price.
    public async Task<bool> RefreshAsync(CancellationToken ct = default)
    {
        string? lastFailure = null;
        foreach (var endpoint in StatsEndpoints)
        {
            try
            {
                if (await TryEndpointAsync(endpoint, ct)) return true;
                lastFailure = "پاسخ نوبیتکس قابل خواندن نبود.";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // shutting down - not a rate failure
            }
            catch (Exception ex)
            {
                lastFailure = ex is HttpRequestException
                    ? "سرور به نوبیتکس دسترسی ندارد (آدرس در دسترس نیست یا IP خارج از ایران مسدود است)."
                    : ex.Message;
                _log.LogWarning(ex, "Failed to refresh USDT-Toman rate from {Endpoint}", endpoint);
            }
        }
        _lastError = lastFailure ?? "";
        return false;
    }

    private async Task<bool> TryEndpointAsync(string endpoint, CancellationToken ct)
    {
        var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        // some endpoints reject requests without a UA; send a plain one.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PhoenixVerify/1.0");

        using var body = new StringContent("{\"srcCurrency\":\"usdt\",\"dstCurrency\":\"rls\"}", Encoding.UTF8, "application/json");
        using var resp = await client.PostAsync(endpoint, body, ct);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("stats", out var stats)) return false;
        foreach (var market in stats.EnumerateObject())
        {
            if (!market.Value.TryGetProperty("latest", out var latest)) continue;
            var raw = latest.ValueKind == JsonValueKind.String ? latest.GetString() : latest.GetRawText();
            if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var rials) && rials > 0)
            {
                // We query the rls (Rial) market, so the value is always in Rial. Dividing by 10 yields the
                // Toman figure shown on nobitex.ir - deterministic, no magnitude guessing.
                var toman = (long)Math.Round(rials / 10m);
                Interlocked.Exchange(ref _nobitexToman, toman);
                Interlocked.Exchange(ref _updatedAtUnixMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                _lastError = "";
                ApplyCurrent();
                return true;
            }
        }
        return false;
    }
}

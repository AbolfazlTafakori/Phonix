using Phonix.Api.Services;

namespace Phonix.Api.Security;

// Traps automated scanners. Real traffic only ever hits /api/*, /health and (in dev) /swagger; the paths
// below are never served, so anything requesting them is hostile. A hit bans the source IP for 24 hours
// and every subsequent request from that IP is rejected outright before it reaches the pipeline.
public sealed class HoneypotMiddleware
{
    private static readonly TimeSpan BanDuration = TimeSpan.FromHours(24);

    // Exact decoy paths plus a few prefixes covering the usual CMS/admin probe families.
    private static readonly HashSet<string> DecoyPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/admin", "/administrator", "/admin.php", "/admin/login", "/dashboard",
        "/wp-admin", "/wp-login.php", "/phpmyadmin", "/.env", "/.git/config", "/config.php",
    };

    private static readonly string[] DecoyPrefixes = { "/wp-", "/.git", "/.env", "/phpmyadmin" };

    private readonly RequestDelegate _next;
    private readonly IpBanService _bans;
    private readonly ITelegramAlertSender _alerts;
    private readonly ILogger<HoneypotMiddleware> _logger;

    public HoneypotMiddleware(RequestDelegate next, IpBanService bans, ITelegramAlertSender alerts, ILogger<HoneypotMiddleware> logger)
    {
        _next = next;
        _bans = bans;
        _alerts = alerts;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (_bans.IsBanned(ip))
        {
            await Reject(context);
            return;
        }

        var path = context.Request.Path.Value ?? "";
        if (IsDecoy(path))
        {
            _bans.Ban(ip, BanDuration);
            _logger.LogWarning("Honeypot hit from {ClientIp} on {Path} — banned for 24h", ip, path);
            _ = _alerts.SendAlertAsync($"🚫 آی‌پی مسدود شد (تله امنیتی): {ip} → {path}");
            await Reject(context);
            return;
        }

        await _next(context);
    }

    private static bool IsDecoy(string path)
    {
        if (DecoyPaths.Contains(path)) return true;
        foreach (var prefix in DecoyPrefixes)
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static async Task Reject(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync("Forbidden");
    }
}

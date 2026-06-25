using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Phonix.Api.Data;
using Phonix.Api.Security;
using Phonix.Api.Services;
using Serilog;
using Serilog.Formatting.Compact;

// Logs live beside the data store so one volume holds state + diagnostics; overridable on the server.
var logDir = Environment.GetEnvironmentVariable("PHONIX_LOG_DIR")
    ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "logs");
Directory.CreateDirectory(logDir);

// Bootstrap logger so even failures during startup land in the console.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Structured logging: console (captured by systemd's journal on the server) + rolling daily JSON files.
    builder.Host.UseSerilog((context, services, config) => config
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File(
            new CompactJsonFormatter(),
            Path.Combine(logDir, "phonix-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 31,
            fileSizeLimitBytes: 50_000_000,
            rollOnFileSizeLimit: true,
            shared: true));

    // Add services to the container.

    builder.Services.AddControllers(options =>
    {
        // Auto-records every mutating staff request into the admin audit trail (see AuditActionFilter).
        options.Filters.Add<AuditActionFilter>();
    }).AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
    builder.Services.AddSingleton<StoreData>();
    builder.Services.AddHostedService<StorePersistenceWorker>();
    // Audit trail lives in its OWN store/file (audit_store.json) so it never bloats the main store.json.
    // Registered as a singleton (shared state) with a dedicated background flusher on its own schedule.
    builder.Services.AddSingleton<AuditStore>();
    builder.Services.AddHostedService<AuditPersistenceWorker>();
    builder.Services.AddSingleton<IEmailSender, EmailSender>();
    builder.Services.AddHttpClient();
    // Live USDT→Toman rate for USD-priced products: one instance serves both the background refresher and
    // the controllers that read/refresh the rate.
    builder.Services.AddSingleton<UsdRateService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<UsdRateService>());
    builder.Services.AddSingleton<ITelegramBackupSender, TelegramBackupSender>();
    builder.Services.AddHostedService<TelegramBackupWorker>();
    builder.Services.AddSingleton<ITelegramAlertSender, TelegramAlertSender>();
    // Honeypot IP bans live in memory (ephemeral, like sessions) so they never bloat store.json.
    builder.Services.AddMemoryCache();
    builder.Services.AddSingleton<IpBanService>();
    builder.Services.AddSingleton<ICaptchaService, CaptchaService>();
    // Identity images (KYC docs, selfies, card photos) are stored outside the web root and only ever
    // streamed back through the authenticated, ownership-checked KYC/Cards download endpoints.
    builder.Services.AddSingleton<IFileStorageService, LocalFileStorageService>();
    // Sends subscription renewal reminders (bell notification + HTML email) before time-based plans expire,
    // on an admin-configured threshold read dynamically each cycle.
    builder.Services.AddHostedService<SubscriptionExpiryWorker>();
    // Read-only access to the Serilog output directory for the admin "system logs" page (list + download).
    builder.Services.AddSingleton(new LogFileService(logDir));
    // Background CPU sampler: one owner produces the rate, the dashboard endpoint reads it lock-free.
    builder.Services.AddSingleton<ServerMetricsCollector>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<ServerMetricsCollector>());
    builder.Services.AddHealthChecks().AddCheck<StoreHealthCheck>("store");

    // Stateless sessions: claims are encrypted into the httpOnly cookie and validated via a PERSISTED Data
    // Protection key ring (App_Data/keys), so logins survive a restart without an in-memory session table.
    var keysDir = Environment.GetEnvironmentVariable("PHONIX_KEYS_DIR")
        ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "keys");
    builder.Services.AddPhonixSessions(keysDir);

    builder.Services.AddAuthentication(TokenAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, TokenAuthenticationHandler>(TokenAuthenticationHandler.SchemeName, null);
    builder.Services.AddAuthorization();

    // throttle auth endpoints per client IP to blunt credential brute-forcing.
    const string authRateLimit = "auth";
    // Per-IP login/register attempts per minute. Configurable so test runs (which share one IP across many
    // cases) and high-traffic deployments can tune it; defaults to a tight 10 for abuse protection.
    var authPermitLimit = int.TryParse(Environment.GetEnvironmentVariable("PHONIX_AUTH_RATE_LIMIT"), out var apl) && apl > 0 ? apl : 10;
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        // baseline per-IP ceiling across the whole API to blunt scraping/abuse.
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(1),
                    PermitLimit = 300,
                    QueueLimit = 0,
                }));
        options.AddPolicy(authRateLimit, context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(1),
                    PermitLimit = authPermitLimit,
                    QueueLimit = 0,
                }));
    });

    const string frontendCors = "frontend";
    // Allow the configured public frontend origin (PHONIX_FRONTEND_URL on the server) alongside the
    // local dev origins, so credentialed cookie auth works from the real domain in production.
    var corsOrigins = new List<string> { "http://localhost:3000", "http://localhost:3001" };
    var frontendOrigin = Environment.GetEnvironmentVariable("PHONIX_FRONTEND_URL")?.TrimEnd('/');
    if (!string.IsNullOrWhiteSpace(frontendOrigin) && !corsOrigins.Contains(frontendOrigin))
        corsOrigins.Add(frontendOrigin);
    builder.Services.AddCors(options =>
    {
        options.AddPolicy(frontendCors, policy => policy
            .WithOrigins(corsOrigins.ToArray())
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
    });

    var app = builder.Build();

    // Behind a reverse proxy (nginx/Caddy terminating TLS), trust its X-Forwarded-* so the real client IP
    // (rate-limiting/logs/audit trail) and HTTPS scheme (Secure cookies) are accurate.
    //
    // SECURITY: only headers from EXPLICITLY trusted proxies are honoured. Trusting every upstream (the old
    // cleared KnownProxies/KnownNetworks) lets anyone able to reach the app port forge X-Forwarded-For and
    // spoof their client IP — poisoning the audit trail, evading the rate limiter, and forging honeypot
    // bans. Set PHONIX_TRUSTED_PROXIES to a comma/space-separated list of the reverse proxy IP(s).
    if (Environment.GetEnvironmentVariable("PHONIX_BEHIND_PROXY") == "true")
    {
        var fwd = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            // Exactly one proxy hop is trusted; the entry it appended is the real client.
            ForwardLimit = 1,
        };
        fwd.KnownNetworks.Clear();
        fwd.KnownProxies.Clear();

        var trustedRaw = Environment.GetEnvironmentVariable("PHONIX_TRUSTED_PROXIES") ?? "";
        var trustedCount = 0;
        foreach (var entry in trustedRaw.Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (IPAddress.TryParse(entry, out var ip))
            {
                fwd.KnownProxies.Add(ip);
                trustedCount++;
            }
            else
            {
                Log.Warning("Ignoring invalid PHONIX_TRUSTED_PROXIES entry: {Entry}", entry);
            }
        }

        if (trustedCount == 0)
            Log.Warning("PHONIX_BEHIND_PROXY=true but no valid PHONIX_TRUSTED_PROXIES configured; " +
                        "X-Forwarded-* headers will be ignored and the direct connection IP used instead.");
        else
            Log.Information("Forwarded headers enabled for {Count} trusted proxy address(es).", trustedCount);

        app.UseForwardedHeaders(fwd);
    }

    // Temporary load-test telemetry: count in-flight requests for the diagnostics endpoint. Only wired when
    // PHONIX_ENABLE_DIAGNOSTICS=true, so production never pays for it. Registered first to wrap everything.
    if (string.Equals(Environment.GetEnvironmentVariable("PHONIX_ENABLE_DIAGNOSTICS"), "true", StringComparison.OrdinalIgnoreCase))
    {
        app.Use(async (context, next) =>
        {
            Phonix.Api.Controllers.InFlightRequestCounter.Increment();
            try { await next(); }
            finally { Phonix.Api.Controllers.InFlightRequestCounter.Decrement(); }
        });
    }

    // Resolved once (singleton): pushes operational alerts to Telegram when enabled.
    var alerts = app.Services.GetRequiredService<ITelegramAlertSender>();

    // Catch-all: log unhandled exceptions with full context and never leak internals to the client.
    app.Use(async (context, next) =>
    {
        try
        {
            await next();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unhandled exception processing {Method} {Path}",
                context.Request.Method, context.Request.Path.Value);
            // Fire-and-forget so alerting never delays the response or throws from the error path; observe
            // any fault so it surfaces in the log instead of becoming an unobserved task exception.
            _ = alerts.SendAlertAsync($"🔴 خطای داخلی سرور در {context.Request.Method} {context.Request.Path.Value}")
                .ContinueWith(t => Log.Warning(t.Exception, "Failed to send error alert"),
                    TaskContinuationOptions.OnlyOnFaulted);
            if (!context.Response.HasStarted)
            {
                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsync("{\"error\":\"خطای داخلی سرور.\"}");
            }
        }
    });

    // Trap scanners hitting decoy admin/CMS paths and reject already-banned IPs. Runs after forwarded
    // headers (real client IP) and before routing so banned traffic never touches the app.
    app.UseMiddleware<HoneypotMiddleware>();

    // One structured summary line per request (method, path, status, duration), enriched with caller identity.
    app.UseSerilogRequestLogging(options =>
    {
        options.EnrichDiagnosticContext = (diag, http) =>
        {
            diag.Set("ClientIp", http.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            var userId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId)) diag.Set("UserId", userId);
        };
    });

    // baseline security headers on every response (defense in depth alongside the frontend headers).
    app.Use(async (context, next) =>
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        await next();
    });

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }
    // HTTPS redirect/HSTS only when TLS is actually terminated for the app (set PHONIX_FORCE_HTTPS=true
    // once a TLS proxy is in front). The default HTTP-only container deploy stays clean without it.
    else if (Environment.GetEnvironmentVariable("PHONIX_FORCE_HTTPS") == "true")
    {
        app.UseHsts();
        app.UseHttpsRedirection();
    }

    app.UseRouting();
    app.UseCors(frontendCors);

    // double-submit CSRF guard: cookie-authenticated unsafe requests must echo the CSRF
    // cookie in a header. Bearer-header clients are CSRF-immune and skip this.
    var csrfExemptPaths = new[] { "/api/auth/login", "/api/auth/register", "/api/auth/forgot" };
    app.Use(async (context, next) =>
    {
        var method = context.Request.Method;
        var unsafeMethod = HttpMethods.IsPost(method) || HttpMethods.IsPut(method)
            || HttpMethods.IsDelete(method) || HttpMethods.IsPatch(method);
        var cookieAuth = context.Request.Cookies.ContainsKey(AuthCookies.Token)
            && string.IsNullOrEmpty(context.Request.Headers.Authorization);
        // unauthenticated entry points don't act on an existing session, so they don't need CSRF
        // (and a stale token cookie shouldn't be able to block re-login).
        var exempt = csrfExemptPaths.Contains(context.Request.Path.Value, StringComparer.OrdinalIgnoreCase);
        if (unsafeMethod && cookieAuth && !exempt)
        {
            var cookieCsrf = context.Request.Cookies[AuthCookies.Csrf];
            var headerCsrf = context.Request.Headers[AuthCookies.CsrfHeader].ToString();
            if (string.IsNullOrEmpty(cookieCsrf) || !CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(cookieCsrf), System.Text.Encoding.UTF8.GetBytes(headerCsrf)))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("درخواست نامعتبر (CSRF).");
                return;
            }
        }
        await next();
    });

    app.UseRateLimiter();

    app.UseAuthentication();
    app.UseAuthorization();

    // Mandatory 2FA enrollment for staff (on by default; set PHONIX_REQUIRE_ADMIN_2FA=false to opt out).
    // Runs after auth so the user's identity/role and id claims are available to the gate.
    if (Environment.GetEnvironmentVariable("PHONIX_REQUIRE_ADMIN_2FA") != "false")
        app.UseMiddleware<TwoFactorSetupGate>();

    app.MapControllers();

    // Liveness/readiness for Docker healthchecks + external uptime monitors (anonymous, JSON).
    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        ResponseWriter = async (ctx, report) =>
        {
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsJsonAsync(new
            {
                status = report.Status.ToString(),
                checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString()),
                totalDurationMs = report.TotalDuration.TotalMilliseconds,
            });
        },
    });

    Log.Information("Phonix API starting up (logs → {LogDir})", logDir);
    // Heads-up that the service (re)started — useful after a deploy or an unexpected restart.
    _ = alerts.SendAlertAsync("✅ سرور فونیکس راه‌اندازی شد.")
        .ContinueWith(t => Log.Warning(t.Exception, "Failed to send startup alert"),
            TaskContinuationOptions.OnlyOnFaulted);
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Phonix API terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Exposed so the test project can boot the app in-memory via WebApplicationFactory<Program>.
public partial class Program { }

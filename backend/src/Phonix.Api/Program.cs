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

    builder.Services.AddControllers().AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
    builder.Services.AddSingleton<StoreData>();
    builder.Services.AddHostedService<StorePersistenceWorker>();
    builder.Services.AddSingleton<IEmailSender, EmailSender>();
    builder.Services.AddHttpClient();
    builder.Services.AddSingleton<ITelegramBackupSender, TelegramBackupSender>();
    builder.Services.AddHostedService<TelegramBackupWorker>();
    builder.Services.AddSingleton<ITelegramAlertSender, TelegramAlertSender>();
    // Identity images (KYC docs, selfies, card photos) are stored outside the web root and only ever
    // streamed back through the authenticated, ownership-checked KYC/Cards download endpoints.
    builder.Services.AddSingleton<IFileStorageService, LocalFileStorageService>();
    // Sends subscription renewal reminders (bell notification + HTML email) before time-based plans expire,
    // on an admin-configured threshold read dynamically each cycle.
    builder.Services.AddHostedService<SubscriptionExpiryWorker>();
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
                    PermitLimit = 10,
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

    // Behind a reverse proxy (nginx/Caddy terminating TLS), trust its X-Forwarded-* so the real
    // client IP (rate-limiting/logs) and HTTPS scheme (Secure cookies) are accurate. Off by default
    // because the proxy hop would otherwise let a directly-exposed port spoof the client IP.
    if (Environment.GetEnvironmentVariable("PHONIX_BEHIND_PROXY") == "true")
    {
        var fwd = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        };
        fwd.KnownNetworks.Clear();
        fwd.KnownProxies.Clear();
        app.UseForwardedHeaders(fwd);
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
            // Fire-and-forget so alerting never delays the response or throws from the error path.
            _ = alerts.SendAlertAsync($"🔴 خطای داخلی سرور در {context.Request.Method} {context.Request.Path.Value}");
            if (!context.Response.HasStarted)
            {
                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsync("{\"error\":\"خطای داخلی سرور.\"}");
            }
        }
    });

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
    _ = alerts.SendAlertAsync("✅ سرور فونیکس راه‌اندازی شد.");
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

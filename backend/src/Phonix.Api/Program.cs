using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using Phonix.Api.Data;
using Phonix.Api.Security;
using Phonix.Api.Services;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddAuthentication(TokenAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, TokenAuthenticationHandler>(TokenAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorization();

// throttle auth endpoints per client IP to blunt credential brute-forcing.
const string authRateLimit = "auth";
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
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
builder.Services.AddCors(options =>
{
    options.AddPolicy(frontendCors, policy => policy
        .WithOrigins("http://localhost:3000", "http://localhost:3001")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
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

app.Run();

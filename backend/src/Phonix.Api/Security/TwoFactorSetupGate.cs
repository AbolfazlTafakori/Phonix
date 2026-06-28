using System.Security.Claims;
using Phonix.Api.Data;
using Phonix.Api.Models;

namespace Phonix.Api.Security;

// Enforces mandatory two-factor for staff: once signed in, an Admin/Support account that has NOT yet enabled
// 2FA is blocked from every admin action and can only reach the handful of endpoints needed to finish the
// setup (read their own profile, and the 2FA status/setup/enable calls). The client redirects them to the
// security page; this gate is the server-side teeth so the rule can't be skipped by calling the API directly.
public sealed class TwoFactorSetupGate
{
    private readonly RequestDelegate _next;
    public TwoFactorSetupGate(RequestDelegate next) => _next = next;

    // The only API surface a not-yet-enrolled staff member may touch, so they can complete enrollment.
    private static bool IsExempt(string path) =>
        !path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ||      // non-API (health, swagger, etc.)
        path.StartsWith("/api/auth/", StringComparison.OrdinalIgnoreCase) ||  // login, logout, 2fa status/setup/enable/verify
        path.StartsWith("/api/account/me", StringComparison.OrdinalIgnoreCase);

    public async Task InvokeAsync(HttpContext context, IDataStore store)
    {
        var user = context.User;
        var isStaff = user.Identity?.IsAuthenticated == true &&
                      (user.IsInRole(nameof(UserRole.Admin)) || user.IsInRole(nameof(UserRole.Support)));

        if (isStaff && !IsExempt(context.Request.Path.Value ?? ""))
        {
            var raw = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(raw, out var id) && store.GetUser(id) is AppUser u && !u.TwoFactorEnabled)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "برای استفاده از پنل، ابتدا باید ورود دو‌مرحله‌ای را فعال کنید.",
                    requiresTwoFactorSetup = true,
                });
                return;
            }
        }

        await _next(context);
    }
}

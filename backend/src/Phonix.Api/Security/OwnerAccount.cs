using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Phonix.Api.Data;
using Phonix.Api.Models;

namespace Phonix.Api.Security;

// The "owner" is not a separate role — it is the single Admin account whose username matches
// PHONIX_OWNER_USERNAME, the account bootstrapped from the environment on every start (see
// EnsureOwnerFromEnvironment). It carries the highest trust in the shop, so the most sensitive settings
// (payment infrastructure, and the V2Ray panel credentials) are gated to it alone, above the ordinary
// Admin level.
public static class OwnerAccount
{
    public static string? ConfiguredUsername =>
        Environment.GetEnvironmentVariable("PHONIX_OWNER_USERNAME")?.Trim();

    // True only when an owner username is configured AND it matches. When the env var is unset (e.g. a dev
    // box that never set one) nobody is the owner — owner-only sections stay closed rather than open to all,
    // which is the safe default for a permission this sensitive.
    public static bool IsOwner(string? username)
    {
        var owner = ConfiguredUsername;
        return !string.IsNullOrWhiteSpace(owner)
            && !string.IsNullOrWhiteSpace(username)
            && string.Equals(owner, username.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}

// Gates an endpoint to the owner account only. Layered ON TOP of [Authorize(Roles = "Admin")]: a normal
// Admin authenticates but is still refused here. The username is read live from the store each request, so
// the check reflects the account as it currently exists rather than a claim baked into the cookie.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class OwnerOnlyAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var principal = context.HttpContext.User;
        if (principal.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var store = context.HttpContext.RequestServices.GetRequiredService<IDataStore>();
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = int.TryParse(raw, out var id) ? store.GetUser(id) : null;

        if (user is not null && OwnerAccount.IsOwner(user.Username)) return;

        context.Result = new ObjectResult("این بخش فقط برای مالک مجموعه در دسترس است.")
        {
            StatusCode = StatusCodes.Status403Forbidden,
        };
    }
}

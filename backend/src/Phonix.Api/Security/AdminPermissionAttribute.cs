using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Phonix.Api.Data;
using Phonix.Api.Models;

namespace Phonix.Api.Security;

// Gates a staff-only endpoint behind a specific panel section. An Admin always passes (full access); a
// Support account passes only when the section is in its granted permissions; anyone else is rejected.
// Permissions are read live from the store each request, so an Admin revoking access takes effect at once.
// Applied alongside the existing [Authorize] role checks — this narrows access, it does not replace it.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class AdminPermissionAttribute : Attribute, IAuthorizationFilter
{
    private readonly string[] _permissions;
    // Accepts one or more section keys; a Support member passes when they hold ANY of them. This lets a
    // shared read endpoint (e.g. listing orders) be reachable from several sections that each own a key.
    public AdminPermissionAttribute(params string[] permissions) => _permissions = permissions;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        // Public reads opt out via [AllowAnonymous]; never gate them.
        if (context.ActionDescriptor.EndpointMetadata.Any(m => m is IAllowAnonymous)) return;

        var principal = context.HttpContext.User;
        if (principal.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedResult();
            return;
        }
        if (principal.IsInRole(nameof(UserRole.Admin))) return;

        var store = context.HttpContext.RequestServices.GetRequiredService<StoreData>();
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = int.TryParse(raw, out var id) ? store.GetUser(id) : null;
        if (user is null)
        {
            context.Result = new UnauthorizedResult();
            return;
        }
        if (user.Role == UserRole.Support && _permissions.Any(p => user.Permissions.Contains(p))) return;

        context.Result = new ObjectResult("شما به این بخش دسترسی ندارید.")
        {
            StatusCode = StatusCodes.Status403Forbidden,
        };
    }
}

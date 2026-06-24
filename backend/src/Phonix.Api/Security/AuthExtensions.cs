using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Models;

namespace Phonix.Api.Security;

public static class AuthExtensions
{
    public const string StaffRoles = nameof(UserRole.Admin) + "," + nameof(UserRole.Support);

    // Set on a session that authenticated through the admin-panel login (password + 2FA).
    public const string AdminScopeClaim = "admin_scope";

    public static int? CurrentUserId(this ControllerBase c)
    {
        var raw = c.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>True when the current session was established through the admin-panel login.</summary>
    public static bool IsAdminScope(this ControllerBase c) =>
        string.Equals(c.User.FindFirstValue(AdminScopeClaim), "true", StringComparison.Ordinal);

    public static bool IsStaff(this ControllerBase c) =>
        c.User.IsInRole(nameof(UserRole.Admin)) || c.User.IsInRole(nameof(UserRole.Support));

    /// <summary>The caller's effective role, read from the authenticated session's role claim.</summary>
    public static UserRole CurrentRole(this ControllerBase c) =>
        c.User.IsInRole(nameof(UserRole.Admin)) ? UserRole.Admin
        : c.User.IsInRole(nameof(UserRole.Support)) ? UserRole.Support
        : UserRole.Customer;

    /// <summary>True when the caller is staff or is acting on their own resource.</summary>
    public static bool OwnsOrStaff(this ControllerBase c, int userId) =>
        c.IsStaff() || c.CurrentUserId() == userId;
}

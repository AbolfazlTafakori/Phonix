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

    /// <summary>
    /// True when the caller holds ANY of the given panel sections — an Admin always does. This is the same
    /// rule <see cref="AdminPermissionAttribute"/> applies, in a form an action can call mid-body when the
    /// decision depends on data (e.g. "this file belongs to someone else, so staff rules apply").
    /// Permissions are read live from the store, so a revoked section takes effect on the next request.
    /// </summary>
    public static bool HasSection(this ControllerBase c, Data.IDataStore store, params string[] sections)
    {
        if (c.User.IsInRole(nameof(UserRole.Admin))) return true;
        if (!c.User.IsInRole(nameof(UserRole.Support))) return false;
        var user = c.CurrentUserId() is int id ? store.GetUser(id) : null;
        return user is not null && sections.Any(s => user.Permissions.Contains(s));
    }

    /// <summary>
    /// Ownership check for the identity documents (KYC photos, card photos, deposit receipts) that a
    /// customer uploads. The owner may always read their own; a staff member may read anyone's ONLY through
    /// the section that is supposed to be looking at them. The plain <see cref="OwnsOrStaff"/> rule was too
    /// broad here: it let a Support account hired to answer chat tickets pull up any customer's national ID
    /// card and selfie, which is the single most sensitive data this shop holds and the one an unrelated
    /// section has no reason to touch.
    /// </summary>
    public static bool OwnsOrSectionStaff(this ControllerBase c, Data.IDataStore store, int ownerId, params string[] sections) =>
        c.CurrentUserId() == ownerId || (c.IsStaff() && c.HasSection(store, sections));
}

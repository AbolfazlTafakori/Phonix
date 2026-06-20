using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Models;

namespace Phonix.Api.Security;

public static class AuthExtensions
{
    public const string StaffRoles = nameof(UserRole.Admin) + "," + nameof(UserRole.Support);

    public static int? CurrentUserId(this ControllerBase c)
    {
        var raw = c.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }

    public static bool IsStaff(this ControllerBase c) =>
        c.User.IsInRole(nameof(UserRole.Admin)) || c.User.IsInRole(nameof(UserRole.Support));

    /// <summary>True when the caller is staff or is acting on their own resource.</summary>
    public static bool OwnsOrStaff(this ControllerBase c, int userId) =>
        c.IsStaff() || c.CurrentUserId() == userId;
}

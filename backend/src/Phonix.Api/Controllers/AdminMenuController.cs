using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Admin;
using Phonix.Api.Data;
using Phonix.Api.Security;

namespace Phonix.Api.Controllers;

public sealed record MenuItemDto(string Key, string Title, string Icon, string Route, bool ComingSoon, int Badge);
public sealed record MenuGroupDto(string Key, string Title, IReadOnlyList<MenuItemDto> Items);

[ApiController]
[Route("api/admin")]
[Authorize(Roles = AuthExtensions.StaffRoles)]
public class AdminMenuController : ControllerBase
{
    private readonly StoreData _store;
    public AdminMenuController(StoreData store) => _store = store;

    // The sidebar for the signed-in staff member: role-filtered SERVER-SIDE (a Support user never receives
    // the Admin-only group — it isn't just hidden in CSS) and pre-populated with live badge counts.
    // NOTE: this is UX. Every Admin-only feature ALSO carries [Authorize(Roles="Admin")] on its own endpoints.
    [HttpGet("menu")]
    public IReadOnlyList<MenuGroupDto> Menu()
    {
        var role = this.CurrentRole();
        var counts = _store.GetAdminBadgeCounts();

        return AdminMenu.Groups
            .Where(g => role.IsAtLeast(g.MinRole))
            .Select(g => new MenuGroupDto(g.Key, g.Title,
                g.Items
                    .Where(i => role.IsAtLeast(i.MinRole))
                    .Select(i => new MenuItemDto(i.Key, i.Title, i.Icon, i.Route, i.ComingSoon, counts.For(i.Badge)))
                    .ToList()))
            .Where(g => g.Items.Count > 0)
            .ToList();
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;

namespace Phonix.Api.Controllers;

// What a CUSTOMER is allowed to see about a WireGuard plan: what they are buying and what it costs. The
// panel, the tunnels, the credentials and the internal ids are absent BY TYPE, so no amount of forgetting
// can leak the infrastructure through this endpoint.
public sealed record WireGuardPublicPlanDto(
    int Id, string Title, string Description, string Protocol,
    long VolumeGb, int DurationDays, int DeviceLimit, long Price, int DiscountPercent, long FinalPrice);

// The storefront's read-only window onto the WireGuard catalogue, mirroring V2RayPublicController.
[ApiController]
[Route("api/wireguard/public")]
[AllowAnonymous]
public class WireGuardPublicController : ControllerBase
{
    private readonly IDataStore _store;
    public WireGuardPublicController(IDataStore store) => _store = store;

    // The active plans of one category, cheapest first. An inactive category returns nothing, so hiding a
    // whole section is one toggle.
    [HttpGet("plans")]
    public IReadOnlyList<WireGuardPublicPlanDto> Plans([FromQuery] int categoryId)
    {
        var category = _store.GetWireGuardCategories().FirstOrDefault(c => c.Id == categoryId);
        if (category is null || !category.Active) return Array.Empty<WireGuardPublicPlanDto>();

        return _store.GetWireGuardPlans()
            .Where(p => p.CategoryId == categoryId && p.Active && !p.SoldOut)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.FinalPrice)
            .Select(p => new WireGuardPublicPlanDto(
                p.Id, p.Title, p.Description, p.Protocol,
                p.VolumeGb, p.DurationDays, p.DeviceLimit, p.Price, p.DiscountPercent, p.FinalPrice))
            .ToList();
    }
}

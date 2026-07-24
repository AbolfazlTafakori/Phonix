using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;

namespace Phonix.Api.Controllers;

// What a CUSTOMER is allowed to see about a V2Ray plan: what they are buying and what it costs. The panel,
// the inbounds, the credentials and the internal ids are absent BY TYPE, so no amount of forgetting can leak
// the infrastructure through this endpoint.
public sealed record V2RayPublicPlanDto(
    int Id, string Title, string Description, string Protocol, string Network,
    long VolumeGb, int DurationDays, int IpLimit, long Price, int DiscountPercent, long FinalPrice);

// The storefront's read-only window onto the V2Ray catalogue. A product links itself to a V2Ray category
// (Product.V2RayCategoryId) and then renders that category's plans as its selectable options, so the whole
// ordinary product presentation — logo, gallery, description, FAQ — is reused and only the plan list differs.
[ApiController]
[Route("api/v2ray/public")]
[AllowAnonymous]
public class V2RayPublicController : ControllerBase
{
    private readonly IDataStore _store;
    public V2RayPublicController(IDataStore store) => _store = store;

    // The active plans of one category, cheapest first. An inactive category returns nothing, so hiding a
    // whole section is one toggle.
    [HttpGet("plans")]
    public IReadOnlyList<V2RayPublicPlanDto> Plans([FromQuery] int categoryId)
    {
        var category = _store.GetV2RayCategories().FirstOrDefault(c => c.Id == categoryId);
        if (category is null || !category.Active) return Array.Empty<V2RayPublicPlanDto>();

        return _store.GetV2RayPlans()
            .Where(p => p.CategoryId == categoryId && p.Active)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.FinalPrice)
            .Select(p => new V2RayPublicPlanDto(
                p.Id, p.Title, p.Description, p.Protocol, p.Network,
                p.VolumeGb, p.DurationDays, p.IpLimit, p.Price, p.DiscountPercent, p.FinalPrice))
            .ToList();
    }
}

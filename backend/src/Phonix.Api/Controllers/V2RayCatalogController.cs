using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Controllers;

public sealed record V2RayCategoryDto(int Id, string Name, string Icon, int SortOrder, bool Active, int PlanCount);
public sealed record V2RayCategoryInput(string Name, string Icon, int SortOrder, bool Active);

public sealed record V2RayPlanDto(
    int Id, int CategoryId, string Title, string Description, int PanelId, int[] InboundIds,
    string Protocol, string Network,
    long VolumeGb, int DurationDays, int IpLimit, int Quantity, long Price, int DiscountPercent, long FinalPrice,
    bool Active, int SortOrder);

public sealed record V2RayPlanInput(
    int CategoryId, string Title, string Description, int PanelId, int[] InboundIds,
    string? Protocol, string? Network,
    long VolumeGb, int DurationDays, int IpLimit, int Quantity, long Price, int DiscountPercent, bool Active, int SortOrder);

// The owner-only management of the separate V2Ray sales catalogue — categories and the plans under them.
// Kept apart from the ordinary product admin on purpose: these plans are many and panel-bound, and mixing
// them in would be confusing. Gated to the owner like the panel credentials it references.
[ApiController]
[Route("api/v2ray/catalog")]
[Authorize(Roles = nameof(UserRole.Admin))]
[OwnerOnly]
public class V2RayCatalogController : ControllerBase
{
    private readonly IDataStore _store;
    public V2RayCatalogController(IDataStore store) => _store = store;

    private IActionResult Problem(string? error) => BadRequest(error ?? "عملیات ناموفق بود.");

    // ── Categories ──────────────────────────────────────────────────────────────────────────────────
    [HttpGet("categories")]
    public IReadOnlyList<V2RayCategoryDto> Categories()
    {
        var plans = _store.GetV2RayPlans();
        return _store.GetV2RayCategories()
            .Select(c => new V2RayCategoryDto(c.Id, c.Name, c.Icon, c.SortOrder, c.Active, plans.Count(p => p.CategoryId == c.Id)))
            .ToList();
    }

    [HttpPost("categories")]
    public IActionResult AddCategory(V2RayCategoryInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) return Problem("نام دسته‌بندی را وارد کنید.");
        var c = _store.AddV2RayCategory(new V2RayCategory
        {
            Name = input.Name.Trim(), Icon = input.Icon?.Trim() ?? "", SortOrder = input.SortOrder, Active = input.Active,
        });
        return Ok(new V2RayCategoryDto(c.Id, c.Name, c.Icon, c.SortOrder, c.Active, 0));
    }

    [HttpPut("categories/{id:int}")]
    public IActionResult UpdateCategory(int id, V2RayCategoryInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) return Problem("نام دسته‌بندی را وارد کنید.");
        var c = _store.UpdateV2RayCategory(new V2RayCategory
        {
            Id = id, Name = input.Name.Trim(), Icon = input.Icon?.Trim() ?? "", SortOrder = input.SortOrder, Active = input.Active,
        });
        return c is null ? NotFound()
            : Ok(new V2RayCategoryDto(c.Id, c.Name, c.Icon, c.SortOrder, c.Active, _store.GetV2RayPlans().Count(p => p.CategoryId == c.Id)));
    }

    // Deleting a category removes its plans too (a plan without a category can't be shown or sold).
    [HttpDelete("categories/{id:int}")]
    public IActionResult DeleteCategory(int id) =>
        _store.DeleteV2RayCategory(id) ? Ok(new { ok = true }) : NotFound();

    // ── Plans ───────────────────────────────────────────────────────────────────────────────────────
    private static V2RayPlanDto ToDto(V2RayPlan p) => new(
        p.Id, p.CategoryId, p.Title, p.Description, p.PanelId, p.InboundIds.ToArray(),
        p.Protocol, p.Network,
        p.VolumeGb, p.DurationDays, p.IpLimit, p.Quantity, p.Price, p.DiscountPercent, p.FinalPrice, p.Active, p.SortOrder);

    [HttpGet("plans")]
    public IReadOnlyList<V2RayPlanDto> Plans() => _store.GetV2RayPlans().Select(ToDto).ToList();

    [HttpPost("plans")]
    public IActionResult AddPlan(V2RayPlanInput input)
    {
        var error = Validate(input);
        if (error is not null) return Problem(error);
        return Ok(ToDto(_store.AddV2RayPlan(FromInput(new V2RayPlan(), input))));
    }

    [HttpPut("plans/{id:int}")]
    public IActionResult UpdatePlan(int id, V2RayPlanInput input)
    {
        var error = Validate(input);
        if (error is not null) return Problem(error);
        var plan = FromInput(new V2RayPlan { Id = id }, input);
        var updated = _store.UpdateV2RayPlan(plan);
        return updated is null ? NotFound() : Ok(ToDto(updated));
    }

    [HttpDelete("plans/{id:int}")]
    public IActionResult DeletePlan(int id) =>
        _store.DeleteV2RayPlan(id) ? Ok(new { ok = true }) : NotFound();

    private string? Validate(V2RayPlanInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Title)) return "عنوان پلن را وارد کنید.";
        if (!_store.GetV2RayCategories().Any(c => c.Id == input.CategoryId)) return "دسته‌بندی معتبر نیست.";
        if (_store.GetV2RayPanel(input.PanelId) is null) return "پنل انتخاب‌شده معتبر نیست.";
        if (input.InboundIds is null || input.InboundIds.Length == 0) return "حداقل یک اینباند (لوکیشن) برای پلن انتخاب کنید.";
        if (input.Price < 0) return "قیمت معتبر نیست.";
        return null;
    }

    private static V2RayPlan FromInput(V2RayPlan plan, V2RayPlanInput input)
    {
        plan.CategoryId = input.CategoryId;
        plan.Title = input.Title.Trim();
        plan.Description = input.Description?.Trim() ?? "";
        plan.PanelId = input.PanelId;
        plan.InboundIds = input.InboundIds.Distinct().ToList();
        plan.Protocol = (input.Protocol ?? "").Trim();
        plan.Network = (input.Network ?? "").Trim();
        plan.Quantity = Math.Max(0, input.Quantity);
        plan.VolumeGb = Math.Max(0, input.VolumeGb);
        plan.DurationDays = Math.Max(0, input.DurationDays);
        plan.IpLimit = Math.Max(0, input.IpLimit);
        plan.Price = Math.Max(0, input.Price);
        plan.DiscountPercent = Math.Clamp(input.DiscountPercent, 0, 100);
        plan.Active = input.Active;
        plan.SortOrder = input.SortOrder;
        return plan;
    }
}

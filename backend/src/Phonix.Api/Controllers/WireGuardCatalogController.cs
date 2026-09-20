using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Controllers;

public sealed record WireGuardCategoryDto(int Id, string Name, string Icon, int SortOrder, bool Active, int PlanCount);
public sealed record WireGuardCategoryInput(string Name, string Icon, int SortOrder, bool Active);

public sealed record WireGuardPlanDto(
    int Id, int CategoryId, string Title, string Description, int PanelId, int[] InterfaceIds, string Protocol,
    long VolumeGb, int DurationDays, int DeviceLimit, int Quantity, long Price, int DiscountPercent, long FinalPrice,
    bool Active, int SortOrder,
    // How much of the cap is used. Sold is meaningless while Quantity is 0 (no cap).
    int Sold, bool SoldOut);

public sealed record WireGuardPlanInput(
    int CategoryId, string Title, string Description, int PanelId, int[] InterfaceIds, string? Protocol,
    long VolumeGb, int DurationDays, int DeviceLimit, int Quantity, long Price, int DiscountPercent, bool Active, int SortOrder);

// The owner-only management of the separate WireGuard sales catalogue — categories and the plans under
// them. Kept apart from the ordinary product admin and from the V2Ray catalogue on purpose: a tunnel plan
// (quota + devices across servers) is its own thing. Gated to the owner like the panel credentials it
// references.
[ApiController]
[Route("api/wireguard/catalog")]
[Authorize(Roles = nameof(UserRole.Admin))]
[OwnerOnly]
public class WireGuardCatalogController : ControllerBase
{
    private readonly IDataStore _store;
    public WireGuardCatalogController(IDataStore store) => _store = store;

    private IActionResult Problem(string? error) => BadRequest(error ?? "عملیات ناموفق بود.");

    // ── Categories ──────────────────────────────────────────────────────────────────────────────────
    [HttpGet("categories")]
    public IReadOnlyList<WireGuardCategoryDto> Categories()
    {
        var plans = _store.GetWireGuardPlans();
        return _store.GetWireGuardCategories()
            .Select(c => new WireGuardCategoryDto(c.Id, c.Name, c.Icon, c.SortOrder, c.Active, plans.Count(p => p.CategoryId == c.Id)))
            .ToList();
    }

    [HttpPost("categories")]
    public IActionResult AddCategory(WireGuardCategoryInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) return Problem("نام دسته‌بندی را وارد کنید.");
        var c = _store.AddWireGuardCategory(new WireGuardCategory
        {
            Name = input.Name.Trim(), Icon = input.Icon?.Trim() ?? "", SortOrder = input.SortOrder, Active = input.Active,
        });
        return Ok(new WireGuardCategoryDto(c.Id, c.Name, c.Icon, c.SortOrder, c.Active, 0));
    }

    [HttpPut("categories/{id:int}")]
    public IActionResult UpdateCategory(int id, WireGuardCategoryInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) return Problem("نام دسته‌بندی را وارد کنید.");
        var c = _store.UpdateWireGuardCategory(new WireGuardCategory
        {
            Id = id, Name = input.Name.Trim(), Icon = input.Icon?.Trim() ?? "", SortOrder = input.SortOrder, Active = input.Active,
        });
        return c is null ? NotFound()
            : Ok(new WireGuardCategoryDto(c.Id, c.Name, c.Icon, c.SortOrder, c.Active, _store.GetWireGuardPlans().Count(p => p.CategoryId == c.Id)));
    }

    // Deleting a category removes its plans too (a plan without a category can't be shown or sold).
    [HttpDelete("categories/{id:int}")]
    public IActionResult DeleteCategory(int id) =>
        _store.DeleteWireGuardCategory(id) ? Ok(new { ok = true }) : NotFound();

    // ── Plans ───────────────────────────────────────────────────────────────────────────────────────
    private static WireGuardPlanDto ToDto(WireGuardPlan p) => new(
        p.Id, p.CategoryId, p.Title, p.Description, p.PanelId, p.InterfaceIds.ToArray(), p.Protocol,
        p.VolumeGb, p.DurationDays, p.DeviceLimit, p.Quantity, p.Price, p.DiscountPercent, p.FinalPrice, p.Active, p.SortOrder,
        p.Sold, p.SoldOut);

    [HttpGet("plans")]
    public IReadOnlyList<WireGuardPlanDto> Plans() => _store.GetWireGuardPlans().Select(ToDto).ToList();

    [HttpPost("plans")]
    public IActionResult AddPlan(WireGuardPlanInput input)
    {
        var error = Validate(input);
        if (error is not null) return Problem(error);
        return Ok(ToDto(_store.AddWireGuardPlan(FromInput(new WireGuardPlan(), input))));
    }

    [HttpPut("plans/{id:int}")]
    public IActionResult UpdatePlan(int id, WireGuardPlanInput input)
    {
        var error = Validate(input);
        if (error is not null) return Problem(error);
        var plan = FromInput(new WireGuardPlan { Id = id }, input);
        var updated = _store.UpdateWireGuardPlan(plan);
        return updated is null ? NotFound() : Ok(ToDto(updated));
    }

    [HttpDelete("plans/{id:int}")]
    public IActionResult DeletePlan(int id) =>
        _store.DeleteWireGuardPlan(id) ? Ok(new { ok = true }) : NotFound();

    private string? Validate(WireGuardPlanInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Title)) return "عنوان پلن را وارد کنید.";
        if (!_store.GetWireGuardCategories().Any(c => c.Id == input.CategoryId)) return "دسته‌بندی معتبر نیست.";
        if (_store.GetWireGuardPanel(input.PanelId) is null) return "پنل انتخاب‌شده معتبر نیست.";
        if (input.InterfaceIds is null || input.InterfaceIds.Length == 0) return "حداقل یک تانل (سرور) برای پلن انتخاب کنید.";
        if (input.DeviceLimit < 1) return "تعداد دستگاه باید حداقل ۱ باشد.";
        if (input.Price < 0) return "قیمت معتبر نیست.";
        return null;
    }

    private static WireGuardPlan FromInput(WireGuardPlan plan, WireGuardPlanInput input)
    {
        plan.CategoryId = input.CategoryId;
        plan.Title = input.Title.Trim();
        plan.Description = input.Description?.Trim() ?? "";
        plan.PanelId = input.PanelId;
        plan.InterfaceIds = input.InterfaceIds.Distinct().ToList();
        plan.Protocol = (input.Protocol ?? "").Trim();
        plan.Quantity = Math.Max(0, input.Quantity);
        plan.VolumeGb = Math.Max(0, input.VolumeGb);
        plan.DurationDays = Math.Max(0, input.DurationDays);
        plan.DeviceLimit = Math.Max(1, input.DeviceLimit);
        plan.Price = Math.Max(0, input.Price);
        plan.DiscountPercent = Math.Clamp(input.DiscountPercent, 0, 100);
        plan.Active = input.Active;
        plan.SortOrder = input.SortOrder;
        return plan;
    }
}

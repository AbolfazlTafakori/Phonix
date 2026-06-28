using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Dtos;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Controllers;

public record DeliveryTemplateInput(string Title, string Content);

[ApiController]
[Route("api/products")]
[Authorize(Roles = AuthExtensions.StaffRoles)]
[AdminPermission("products")]
public class ProductsController : ControllerBase
{
    private readonly StoreData _store;
    private readonly Services.UsdRateService _rate;

    public ProductsController(StoreData store, Services.UsdRateService rate)
    {
        _store = store;
        _rate = rate;
    }

    [AllowAnonymous]
    [HttpGet]
    public IEnumerable<ProductDto> Get([FromQuery] int? categoryId, [FromQuery] string? search)
    {
        var names = CategoryNames();
        return _store.GetProducts(categoryId, search)
            .Select(p => p.ToDto(names.GetValueOrDefault(p.CategoryId, "")));
    }

    [AllowAnonymous]
    [HttpGet("{id:int}")]
    public ActionResult<ProductDto> Get(int id)
    {
        var product = _store.GetProduct(id);
        return product is null ? NotFound() : product.ToDto(CategoryName(product.CategoryId));
    }

    [HttpPost]
    public ActionResult<ProductDto> Create(ProductInput input)
    {
        var product = _store.AddProduct(Map(new Product(), input));
        return CreatedAtAction(nameof(Get), new { id = product.Id }, product.ToDto(CategoryName(product.CategoryId)));
    }

    [HttpPut("{id:int}")]
    public ActionResult<ProductDto> Update(int id, ProductInput input)
    {
        var product = Map(new Product { Id = id }, input);
        if (!_store.UpdateProduct(product)) return NotFound();
        return _store.GetProduct(id)!.ToDto(CategoryName(product.CategoryId));
    }

    [HttpPut("{id:int}/price")]
    public ActionResult<ProductDto> UpdatePrice(int id, PriceInput input)
    {
        var product = _store.GetProduct(id);
        if (product is null) return NotFound();
        product.Price = input.Price;
        product.DiscountPercent = input.DiscountPercent;
        product.PriceUsd = Math.Max(0, input.PriceUsd ?? 0);
        ApplyUsdPrice(product);
        _store.UpdateProduct(product);
        return _store.GetProduct(id)!.ToDto(CategoryName(product.CategoryId));
    }

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id) => _store.DeleteProduct(id) ? NoContent() : NotFound();

    // ── Reusable delivery templates per product (staff only — class-level auth applies) ──

    // Lists the saved templates for a product, to populate the dropdown in the deliver modal.
    [HttpGet("{id:int}/templates")]
    public ActionResult<IEnumerable<ProductDeliveryTemplate>> GetTemplates(int id)
    {
        if (_store.GetProduct(id) is null) return NotFound();
        return Ok(_store.GetDeliveryTemplates(id));
    }

    // Saves a new named template on the product.
    [HttpPost("{id:int}/templates")]
    public ActionResult<ProductDeliveryTemplate> AddTemplate(int id, DeliveryTemplateInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Title)) return BadRequest("عنوان قالب الزامی است.");
        if (string.IsNullOrWhiteSpace(input.Content)) return BadRequest("متن قالب الزامی است.");
        var tpl = _store.AddDeliveryTemplate(id, input.Title, input.Content);
        return tpl is null ? NotFound() : tpl;
    }

    // Removes a template from the product by its (product-scoped) id.
    [HttpDelete("{id:int}/templates/{templateId:int}")]
    public IActionResult DeleteTemplate(int id, int templateId) =>
        _store.DeleteDeliveryTemplate(id, templateId) ? NoContent() : NotFound();

    // For a USD-priced product, snap its Toman Price to the current rate immediately so the saved value is
    // correct right away (the background service keeps it in sync afterwards).
    private void ApplyUsdPrice(Product p)
    {
        var rate = _rate.TomanPerUsd;
        if (rate <= 0) return;
        if (p.PriceUsd > 0) p.Price = (long)Math.Round(p.PriceUsd * rate);
        foreach (var pl in p.Plans)
            if (pl.PriceUsd > 0) pl.Price = (long)Math.Round(pl.PriceUsd * rate);
    }

    private Product Map(Product target, ProductInput input)
    {
        target.Name = (input.Name ?? "").Trim();
        target.CategoryId = input.CategoryId;
        target.Price = Math.Max(0, input.Price);
        target.PriceUsd = Math.Max(0, input.PriceUsd ?? 0);
        target.DiscountPercent = Math.Clamp(input.DiscountPercent, 0, 100);
        target.Stock = Math.Max(0, input.Stock);
        target.IsActive = input.IsActive;
        target.Featured = input.Featured;
        target.Image = input.Image ?? "";
        target.Sku = input.Sku ?? "";
        target.Description = input.Description ?? "";
        target.Warning = input.Warning ?? "";
        target.RequiredLevel = Math.Clamp(input.RequiredLevel ?? 1, 1, 2);
        target.DeliveryTemplate = input.DeliveryTemplate ?? "";
        target.Features = input.Features ?? new();
        target.Plans = (input.Plans ?? new()).Select(NormalizePlan).ToList();
        ApplyUsdPrice(target);
        return target;
    }

    private static readonly HashSet<string> FieldTypes = new(StringComparer.Ordinal)
        { "text", "email", "password", "phone", "textarea" };

    private static ProductPlan NormalizePlan(ProductPlan plan) => new()
    {
        Type = (plan.Type ?? "").Trim(),
        Months = Math.Max(1, plan.Months),
        Price = Math.Max(0, plan.Price),
        PriceUsd = Math.Max(0, plan.PriceUsd),
        DiscountPercent = Math.Clamp(plan.DiscountPercent, 0, 100),
        IsActive = plan.IsActive,
        // Per-plan customer-info settings. Drop blank/invalid fields and clamp the type to a known control;
        // a "password" field is always treated as sensitive regardless of the supplied flag.
        CollectsInfo = plan.CollectsInfo,
        InputFields = (plan.InputFields ?? new())
            .Where(f => !string.IsNullOrWhiteSpace(f.Label))
            .Select(f =>
            {
                var t = (f.Type ?? "").Trim();
                var type = FieldTypes.Contains(t) ? t : "text";
                return new PlanInputField
                {
                    Label = f.Label.Trim(),
                    Type = type,
                    Required = f.Required,
                    Sensitive = f.Sensitive || type == "password",
                };
            })
            .ToList(),
        WarningText = (plan.WarningText ?? "").Trim(),
        TutorialText = (plan.TutorialText ?? "").Trim(),
        TutorialMedia = (plan.TutorialMedia ?? new())
            .Where(m => !string.IsNullOrWhiteSpace(m.Id))
            .Select(m => new PlanTutorialMedia { Kind = m.Kind == "video" ? "video" : "image", Id = m.Id.Trim() })
            .ToList(),
        AllowNotes = plan.AllowNotes,
    };

    private Dictionary<int, string> CategoryNames() =>
        _store.GetCategories().ToDictionary(c => c.Id, c => c.Name);

    private string CategoryName(int categoryId) => _store.GetCategory(categoryId)?.Name ?? "";
}

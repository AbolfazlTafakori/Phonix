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
    private readonly IDataStore _store;
    private readonly Services.UsdRateService _rate;
    private readonly Services.IFileStorageService _files;
    private readonly Services.CatalogCache _cache;

    public ProductsController(IDataStore store, Services.UsdRateService rate, Services.IFileStorageService files,
        Services.CatalogCache cache)
    {
        _store = store;
        _rate = rate;
        _files = files;
        _cache = cache;
    }

    [AllowAnonymous]
    [HttpGet]
    public IEnumerable<ProductDto> Get([FromQuery] int? categoryId, [FromQuery] string? search)
    {
        List<ProductDto> Build(int? cat, string? term)
        {
            var names = CategoryNames();
            return _store.GetProducts(cat, term)
                .Select(p => p.ToDto(names.GetValueOrDefault(p.CategoryId, ""))).ToList();
        }

        // Only the plain listing is cached — it is the one every visitor loads. A filtered or searched request
        // is answered from the store, so caching never has to reason about a key space.
        return categoryId is null && string.IsNullOrWhiteSpace(search)
            ? _cache.Products(() => Build(null, null))
            : Build(categoryId, search);
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
        _cache.Invalidate();
        return CreatedAtAction(nameof(Get), new { id = product.Id }, product.ToDto(CategoryName(product.CategoryId)));
    }

    [HttpPut("{id:int}")]
    public ActionResult<ProductDto> Update(int id, ProductInput input)
    {
        // Capture every image the product referenced BEFORE the swap, so any that a replacement leaves
        // unused can be freed. OrphanCleanup only deletes an id that is no longer referenced anywhere, so a
        // picture still in use (here or on another product) is never removed.
        var existing = _store.GetProduct(id);
        var oldImages = existing is null
            ? Array.Empty<string?>()
            : new[] { existing.Image, existing.Logo, existing.ListImage }.Concat(existing.Gallery).ToArray();
        var product = Map(new Product { Id = id }, input);
        // The product form doesn't carry the stock-pool switches (they live on the stock page) — a
        // full-replace edit must not silently reset them.
        product.AutoDeliverStock = existing?.AutoDeliverStock ?? false;
        product.SlotFulfillment = existing?.SlotFulfillment ?? false;
        product.ServiceName = existing?.ServiceName ?? "";
        if (!_store.UpdateProduct(product)) return NotFound();
        _cache.Invalidate();
        Services.OrphanCleanup.Queue(_files, _store, oldImages);
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
        _cache.Invalidate();
        return _store.GetProduct(id)!.ToDto(CategoryName(product.CategoryId));
    }

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id)
    {
        var existing = _store.GetProduct(id);
        var oldImages = existing is null
            ? Array.Empty<string?>()
            : new[] { existing.Image, existing.Logo, existing.ListImage }.Concat(existing.Gallery).ToArray();
        if (!_store.DeleteProduct(id)) return NotFound();
        _cache.Invalidate();
        Services.OrphanCleanup.Queue(_files, _store, oldImages);
        return NoContent();
    }

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
        target.Logo = input.Logo ?? "";
        target.ListImage = input.ListImage ?? "";
        target.Gallery = input.Gallery ?? new();
        target.Sku = input.Sku ?? "";
        target.Description = input.Description ?? "";
        target.Warning = input.Warning ?? "";
        target.RequiredLevel = Math.Clamp(input.RequiredLevel ?? 1, 1, 2);
        // Links the product to the V2Ray catalogue: when set, its selectable plans come from that category
        // instead of its own Plans list (see Product.V2RayCategoryId). 0 = an ordinary product.
        target.V2RayCategoryId = Math.Max(0, input.V2RayCategoryId ?? 0);
        target.DeliveryTemplate = input.DeliveryTemplate ?? "";
        target.Features = input.Features ?? new();
        target.Faq = (input.Faq ?? new())
            .Select(f => new ProductFaq { Question = (f.Question ?? "").Trim(), Answer = (f.Answer ?? "").Trim() })
            .Where(f => f.Question.Length > 0 && f.Answer.Length > 0)
            .ToList();
        // A V2Ray-linked product owns no plans: its list is projected from the linked category on every read
        // (see ApplyV2RayPlans). Writing the incoming list back would persist that projection as if it were
        // the product's own plans, and the two would then drift apart. Keep it empty instead.
        target.Plans = target.V2RayCategoryId > 0
            ? new()
            : (input.Plans ?? new()).Select(NormalizePlan).ToList();
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
        UserCount = Math.Max(0, plan.UserCount),
        Rules = (plan.Rules ?? "").Trim(),
        // Per-plan customer-info settings. Drop blank/invalid fields and clamp the type to a known control;
        // a "password" field is always treated as sensitive regardless of the supplied flag.
        CollectsInfo = plan.CollectsInfo,
        CollectSeatInfo = plan.CollectSeatInfo,
        SeatInfoHint = (plan.SeatInfoHint ?? "").Trim(),
        // A negative allowance is meaningless; clamp so the panel can trust the number it renders.
        SeatInfoEditLimit = Math.Clamp(plan.SeatInfoEditLimit, 0, 20),
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

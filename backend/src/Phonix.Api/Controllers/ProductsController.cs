using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Dtos;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Controllers;

[ApiController]
[Route("api/products")]
[Authorize(Roles = AuthExtensions.StaffRoles)]
public class ProductsController : ControllerBase
{
    private readonly StoreData _store;

    public ProductsController(StoreData store) => _store = store;

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
        _store.UpdateProduct(product);
        return _store.GetProduct(id)!.ToDto(CategoryName(product.CategoryId));
    }

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id) => _store.DeleteProduct(id) ? NoContent() : NotFound();

    private static Product Map(Product target, ProductInput input)
    {
        target.Name = (input.Name ?? "").Trim();
        target.CategoryId = input.CategoryId;
        target.Price = Math.Max(0, input.Price);
        target.DiscountPercent = Math.Clamp(input.DiscountPercent, 0, 100);
        target.Stock = Math.Max(0, input.Stock);
        target.IsActive = input.IsActive;
        target.Featured = input.Featured;
        target.Image = input.Image ?? "";
        target.Sku = input.Sku ?? "";
        target.Description = input.Description ?? "";
        target.Warning = input.Warning ?? "";
        target.Features = input.Features ?? new();
        target.Plans = (input.Plans ?? new()).Select(NormalizePlan).ToList();
        return target;
    }

    private static ProductPlan NormalizePlan(ProductPlan plan) => new()
    {
        Type = (plan.Type ?? "").Trim(),
        Months = Math.Max(1, plan.Months),
        Price = Math.Max(0, plan.Price),
        DiscountPercent = Math.Clamp(plan.DiscountPercent, 0, 100),
        IsActive = plan.IsActive,
    };

    private Dictionary<int, string> CategoryNames() =>
        _store.GetCategories().ToDictionary(c => c.Id, c => c.Name);

    private string CategoryName(int categoryId) => _store.GetCategory(categoryId)?.Name ?? "";
}

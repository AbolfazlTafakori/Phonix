using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Dtos;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Controllers;

[ApiController]
[Route("api/categories")]
[Authorize(Roles = AuthExtensions.StaffRoles)]
[AdminPermission("categories")]
public class CategoriesController : ControllerBase
{
    private readonly IDataStore _store;
    private readonly Services.CatalogCache _cache;

    public CategoriesController(IDataStore store, Services.CatalogCache cache)
    {
        _store = store;
        _cache = cache;
    }

    [AllowAnonymous]
    [HttpGet]
    // Each category carries a product count, so the uncached version is a scan per category on every visit.
    public IEnumerable<CategoryDto> Get() =>
        _cache.Categories(() => _store.GetCategories().Select(c => c.ToDto(_store.CountProducts(c.Id))).ToList());

    [AllowAnonymous]
    [HttpGet("{id:int}")]
    public ActionResult<CategoryDto> Get(int id)
    {
        var category = _store.GetCategory(id);
        return category is null ? NotFound() : category.ToDto(_store.CountProducts(id));
    }

    [HttpPost]
    public ActionResult<CategoryDto> Create(CategoryInput input)
    {
        var category = _store.AddCategory(new Category
        {
            Name = input.Name,
            Slug = input.Slug,
            Icon = input.Icon,
            Description = input.Description ?? "",
            IsActive = input.IsActive,
            SortOrder = input.SortOrder,
        });
        _cache.Invalidate();
        return CreatedAtAction(nameof(Get), new { id = category.Id }, category.ToDto(0));
    }

    [HttpPut("{id:int}")]
    public ActionResult<CategoryDto> Update(int id, CategoryInput input)
    {
        var ok = _store.UpdateCategory(new Category
        {
            Id = id,
            Name = input.Name,
            Slug = input.Slug,
            Icon = input.Icon,
            Description = input.Description ?? "",
            IsActive = input.IsActive,
            SortOrder = input.SortOrder,
        });
        if (!ok) return NotFound();
        _cache.Invalidate();
        return _store.GetCategory(id)!.ToDto(_store.CountProducts(id));
    }

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id)
    {
        if (!_store.DeleteCategory(id)) return NotFound();
        _cache.Invalidate();
        return NoContent();
    }
}

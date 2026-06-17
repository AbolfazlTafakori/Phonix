using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Dtos;
using Phonix.Api.Models;

namespace Phonix.Api.Controllers;

[ApiController]
[Route("api/categories")]
public class CategoriesController : ControllerBase
{
    private readonly StoreData _store;

    public CategoriesController(StoreData store) => _store = store;

    [HttpGet]
    public IEnumerable<CategoryDto> Get() =>
        _store.GetCategories().Select(c => c.ToDto(_store.CountProducts(c.Id)));

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
            IsActive = input.IsActive,
            SortOrder = input.SortOrder,
        });
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
            IsActive = input.IsActive,
            SortOrder = input.SortOrder,
        });
        if (!ok) return NotFound();
        return _store.GetCategory(id)!.ToDto(_store.CountProducts(id));
    }

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id) => _store.DeleteCategory(id) ? NoContent() : NotFound();
}

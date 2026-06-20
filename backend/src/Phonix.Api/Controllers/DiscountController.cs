using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Controllers;

public record DiscountValidateInput(string Code, long Subtotal);
public record DiscountResultDto(bool Valid, long Amount, long FinalTotal, string? Message);

[ApiController]
[Route("api/discounts")]
[Authorize]
public class DiscountController : ControllerBase
{
    private readonly StoreData _store;
    public DiscountController(StoreData store) => _store = store;

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [HttpGet]
    public IEnumerable<DiscountCode> Get() => _store.GetDiscountCodes();

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [HttpPost]
    public ActionResult<DiscountCode> Create(DiscountCode input)
    {
        if (string.IsNullOrWhiteSpace(input.Code)) return BadRequest("کد تخفیف الزامی است.");
        Normalize(input);
        return _store.AddDiscountCode(input);
    }

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [HttpPut("{id:int}")]
    public ActionResult<DiscountCode> Update(int id, DiscountCode input)
    {
        if (string.IsNullOrWhiteSpace(input.Code)) return BadRequest("کد تخفیف الزامی است.");
        input.Id = id;
        Normalize(input);
        if (!_store.UpdateDiscountCode(input)) return NotFound();
        return _store.GetDiscountCodes().First(d => d.Id == id);
    }

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id) => _store.DeleteDiscountCode(id) ? NoContent() : NotFound();

    // any signed-in customer can preview a code against their cart subtotal (does not consume it).
    [HttpPost("validate")]
    public ActionResult<DiscountResultDto> Validate(DiscountValidateInput input)
    {
        var result = _store.ResolveDiscount(input.Code, Math.Max(0, input.Subtotal));
        if (result.Error is not null) return Ok(new DiscountResultDto(false, 0, Math.Max(0, input.Subtotal), result.Error));
        return Ok(new DiscountResultDto(true, result.Amount, input.Subtotal - result.Amount, null));
    }

    private static void Normalize(DiscountCode input)
    {
        input.Code = input.Code.Trim();
        input.Value = input.Type == DiscountType.Percent
            ? Math.Clamp(input.Value, 0L, 100L)
            : Math.Max(0L, input.Value);
        input.MinOrder = Math.Max(0L, input.MinOrder);
        input.MaxDiscount = Math.Max(0L, input.MaxDiscount);
        input.UsageLimit = Math.Max(0, input.UsageLimit);
    }
}

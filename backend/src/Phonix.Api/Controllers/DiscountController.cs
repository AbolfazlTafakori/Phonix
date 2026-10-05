using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Controllers;

// `Items` is the basket line by line, so a code limited to certain products can be previewed against just
// those. A client that sends only `Subtotal` (a page loaded before this existed) is treated as one line of
// no particular product: an unrestricted code previews as before, a restricted one as not applicable. The
// preview is advisory either way — PlaceOrder re-prices every line from the catalogue and resolves again.
public record DiscountValidateLine(int ProductId, long LineTotal);
public record DiscountValidateInput(string Code, long Subtotal, List<DiscountValidateLine>? Items = null);
public record DiscountResultDto(bool Valid, long Amount, long FinalTotal, string? Message);

[ApiController]
[Route("api/discounts")]
[Authorize]
public class DiscountController : ControllerBase
{
    private readonly IDataStore _store;
    public DiscountController(IDataStore store) => _store = store;

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("discounts")]
    [HttpGet]
    public IEnumerable<DiscountCode> Get() => _store.GetDiscountCodes();

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("discounts")]
    [HttpPost]
    public ActionResult<DiscountCode> Create(DiscountCode input)
    {
        if (string.IsNullOrWhiteSpace(input.Code)) return BadRequest("کد تخفیف الزامی است.");
        Normalize(input);
        return _store.AddDiscountCode(input);
    }

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("discounts")]
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
    [AdminPermission("discounts")]
    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id) => _store.DeleteDiscountCode(id) ? NoContent() : NotFound();

    // any signed-in customer can preview a code against their cart subtotal (does not consume it). Rate
    // limited on its own — see the policy comment in Program.cs — since a preview that names the exact
    // discount amount is otherwise a free oracle for brute-forcing active promo codes.
    [EnableRateLimiting("discount-validate")]
    [HttpPost("validate")]
    public ActionResult<DiscountResultDto> Validate(DiscountValidateInput input)
    {
        var lines = input.Items is { Count: > 0 }
            ? input.Items.Take(200).Select(i => new DiscountLine(i.ProductId, Math.Max(0, i.LineTotal))).ToList()
            : new List<DiscountLine> { new(0, Math.Max(0, input.Subtotal)) };
        var subtotal = lines.Sum(l => l.LineTotal);
        var result = _store.ResolveDiscount(input.Code, lines);
        if (result.Error is not null) return Ok(new DiscountResultDto(false, 0, subtotal, result.Error));
        return Ok(new DiscountResultDto(true, result.Amount, subtotal - result.Amount, null));
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
        input.ProductIds = (input.ProductIds ?? new()).Where(id => id > 0).Distinct().ToList();
    }
}

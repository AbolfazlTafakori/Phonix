using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Security;

namespace Phonix.Api.Controllers;

public record PlanTypeInput(string Name);
public record RenamePlanTypeInput(string OldName, string NewName);

[ApiController]
[Route("api/plan-types")]
[Authorize(Roles = AuthExtensions.StaffRoles)]
public class PlanTypesController : ControllerBase
{
    private readonly StoreData _store;
    public PlanTypesController(StoreData store) => _store = store;

    [AllowAnonymous]
    [HttpGet]
    public IEnumerable<string> Get() => _store.GetPlanTypes();

    [HttpPost]
    public ActionResult<IEnumerable<string>> Add(PlanTypeInput input)
    {
        if (!_store.AddPlanType(input.Name)) return BadRequest("نام نامعتبر یا تکراری است.");
        return Ok(_store.GetPlanTypes());
    }

    [HttpPut("rename")]
    public ActionResult<IEnumerable<string>> Rename(RenamePlanTypeInput input)
    {
        if (!_store.RenamePlanType(input.OldName, input.NewName)) return BadRequest("تغییر نام ممکن نیست (نام تکراری یا یافت نشد).");
        return Ok(_store.GetPlanTypes());
    }

    [HttpDelete("{name}")]
    public ActionResult<IEnumerable<string>> Remove(string name)
    {
        if (!_store.RemovePlanType(name)) return NotFound();
        return Ok(_store.GetPlanTypes());
    }
}

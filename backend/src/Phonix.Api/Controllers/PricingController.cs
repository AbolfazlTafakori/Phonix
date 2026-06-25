using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Dtos;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Controllers;

[ApiController]
[Route("api/pricing")]
[Authorize(Roles = AuthExtensions.StaffRoles)]
[AdminPermission("pricing")]
public class PricingController : ControllerBase
{
    private readonly StoreData _store;
    private readonly Services.UsdRateService _rate;

    public PricingController(StoreData store, Services.UsdRateService rate)
    {
        _store = store;
        _rate = rate;
    }

    // USD→Toman rate used to price products entered in USD. `tomanPerUsd` is the effective rate (what prices
    // actually use); `nobitex` is the live value; `manual`/`auto` reflect the admin override. Anonymous so
    // the storefront can show it; mutations are staff-only.
    [AllowAnonymous]
    [HttpGet("usd-rate")]
    public object GetUsdRate()
    {
        var s = _store.GetSettings();
        return new
        {
            tomanPerUsd = _rate.TomanPerUsd,
            nobitex = _rate.NobitexToman,
            manual = s.ManualUsdRate,
            auto = s.UsdRateAuto,
            updatedAtUnixMs = _rate.UpdatedAtUnixMs,
            lastError = _rate.LastError,
        };
    }

    [HttpPost("usd-rate/refresh")]
    public async Task<object> RefreshUsdRate()
    {
        await _rate.RefreshAsync();
        return GetUsdRate();
    }

    public record ManualRateInput(long Rate, bool Auto);

    // Sets the manual rate and auto/manual mode, then re-prices all USD products/plans against the result.
    [HttpPut("usd-rate/manual")]
    public object SetManualRate(ManualRateInput input)
    {
        _store.SetUsdRate(input.Rate, input.Auto);
        _rate.ApplyCurrent();
        return GetUsdRate();
    }

    [AllowAnonymous]
    [HttpGet("settings")]
    public PricingSettings GetSettings() => _store.GetSettings();

    [HttpPut("settings")]
    public PricingSettings UpdateSettings(PricingSettings settings)
    {
        _store.UpdateSettings(settings);
        return _store.GetSettings();
    }

    [AllowAnonymous]
    [HttpGet("plans")]
    public IEnumerable<PlanDto> GetPlans() => _store.GetPlans().Select(p => p.ToDto());

    [HttpPost("plans")]
    public ActionResult<PlanDto> CreatePlan(PlanInput input)
    {
        var plan = _store.AddPlan(new SubscriptionPlan
        {
            Label = input.Label,
            Months = input.Months,
            Price = PriceFor(input),
            PriceUsd = Math.Max(0, input.PriceUsd ?? 0),
            DiscountPercent = input.DiscountPercent,
        });
        return plan.ToDto();
    }

    [HttpPut("plans/{id:int}")]
    public ActionResult<PlanDto> UpdatePlan(int id, PlanInput input)
    {
        var ok = _store.UpdatePlan(new SubscriptionPlan
        {
            Id = id,
            Label = input.Label,
            Months = input.Months,
            Price = PriceFor(input),
            PriceUsd = Math.Max(0, input.PriceUsd ?? 0),
            DiscountPercent = input.DiscountPercent,
        });
        if (!ok) return NotFound();
        return _store.GetPlans().First(p => p.Id == id).ToDto();
    }

    [HttpDelete("plans/{id:int}")]
    public IActionResult DeletePlan(int id) => _store.DeletePlan(id) ? NoContent() : NotFound();

    // Toman price for a plan: derived from the live USD rate when a dollar price is given, else the manual Toman.
    private long PriceFor(PlanInput input) =>
        input.PriceUsd is > 0 && _rate.TomanPerUsd > 0
            ? (long)Math.Round(input.PriceUsd.Value * _rate.TomanPerUsd)
            : input.Price;
}

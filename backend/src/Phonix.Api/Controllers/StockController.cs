using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;
using Phonix.Api.Services;

namespace Phonix.Api.Controllers;

public record AddStockInput(int ProductId, List<string> Lines);
public record PullStockInput(int OrderId, int UnitId);
public record StockAutoDeliverInput(int ProductId, bool Enabled);
public record AddStockAccountInput(int ProductId, string Username, string Password, string Plan, string PlanType, int Capacity, int Months);
public record SlotFulfillmentInput(int ProductId, bool Enabled);
public record ServiceNameInput(int ProductId, string ServiceName);
// The plan types a product offers, so the account form can bind an account to one of them.
public record StockPlanTypeDto(string Type);

// A slot without anything sensitive — labels and lifecycle only; the credentials live on the account.
public record StockSlotDto(int Id, int Index, string Label, StockItemStatus Status, int? OrderId, int? UnitId,
    DateTime? DeliveredAtUtc)
{
    public static StockSlotDto From(StockSlot s) =>
        new(s.Id, s.Index, s.Label, s.Status, s.OrderId, s.UnitId, s.DeliveredAtUtc);
}

// An account WITHOUT its password (live credential — revealed one account at a time, audited, like items).
public record StockAccountDto(int Id, int ProductId, string Username, string Plan, string PlanType, int Capacity, int Months,
    bool Disabled, string? AddedBy, DateTime AddedAtUtc, List<StockSlotDto> Slots)
{
    public static StockAccountDto From(StockAccount a) =>
        new(a.Id, a.ProductId, a.Username, a.Plan, a.PlanType, a.Capacity, a.Months, a.Disabled, a.AddedBy, a.AddedAtUtc,
            a.Slots.OrderBy(s => s.Index).Select(StockSlotDto.From).ToList());
}

// A stock item WITHOUT its payload. Item contents are live credentials, so lists never carry them — an admin
// reveals one item at a time through the content endpoint (which leaves an audit-log trail like every admin
// action).
public record StockItemDto(int Id, int ProductId, StockItemStatus Status, int? OrderId, int? UnitId,
    string? AddedBy, DateTime AddedAtUtc, DateTime? DeliveredAtUtc)
{
    public static StockItemDto From(StockItem s) =>
        new(s.Id, s.ProductId, s.Status, s.OrderId, s.UnitId, s.AddedBy, s.AddedAtUtc, s.DeliveredAtUtc);
}

public record StockSummaryDto(int ProductId, string Name, string Image, bool AutoDeliver,
    int Available, int Reserved, int Delivered, int Disabled,
    bool SlotFulfillment, int Accounts, int SlotAvailable, int SlotReserved, int SlotDelivered, int SlotDisabled,
    string ServiceName, IReadOnlyList<string> PlanTypes);

[ApiController]
[Route("api/stock")]
[Authorize(Roles = AuthExtensions.StaffRoles)]
[AdminPermission("stock-pool")]
public class StockController : ControllerBase
{
    private const int MaxLinesPerAdd = 500;
    private const int MaxContentLength = 2000;

    private readonly IDataStore _store;
    private readonly IStockFulfillmentService _fulfillment;
    public StockController(IDataStore store, IStockFulfillmentService fulfillment)
    {
        _store = store;
        _fulfillment = fulfillment;
    }

    // Per-product pool counters for the overview table. Every product appears (not just pooled ones) so the
    // admin can start a pool — or flip auto-delivery — without leaving the page.
    [HttpGet("summary")]
    public IEnumerable<StockSummaryDto> Summary()
    {
        var byProduct = _store.GetStockItems().GroupBy(s => s.ProductId).ToDictionary(g => g.Key, g => g.ToList());
        var accountsByProduct = _store.GetStockAccounts().GroupBy(a => a.ProductId).ToDictionary(g => g.Key, g => g.ToList());
        return _store.GetProducts().Select(p =>
        {
            var items = byProduct.TryGetValue(p.Id, out var list) ? list : new List<StockItem>();
            var accounts = accountsByProduct.TryGetValue(p.Id, out var accs) ? accs : new List<StockAccount>();
            var slots = accounts.SelectMany(a => a.Slots).ToList();
            int Count(StockItemStatus st) => items.Count(s => s.Status == st);
            int Slot(StockItemStatus st) => slots.Count(s => s.Status == st);
            return new StockSummaryDto(p.Id, p.Name, string.IsNullOrWhiteSpace(p.Logo) ? p.Image : p.Logo,
                p.AutoDeliverStock, Count(StockItemStatus.Available), Count(StockItemStatus.Reserved),
                Count(StockItemStatus.Delivered), Count(StockItemStatus.Disabled),
                p.SlotFulfillment, accounts.Count, Slot(StockItemStatus.Available), Slot(StockItemStatus.Reserved),
                Slot(StockItemStatus.Delivered), Slot(StockItemStatus.Disabled),
                p.ServiceName, p.Plans.Where(pl => pl.IsActive).Select(pl => pl.Type).Distinct().ToList());
        });
    }

    [HttpGet]
    public IEnumerable<StockItemDto> Items([FromQuery] int productId) =>
        _store.GetStockItems(productId).Select(StockItemDto.From);

    // Reveals a single item's payload. Separate endpoint on purpose: the reveal is deliberate and audited.
    [HttpGet("{id:int}/content")]
    public ActionResult<object> Content(int id)
    {
        var item = _store.GetStockItem(id);
        if (item is null) return NotFound();
        return new { content = SensitiveField.Reveal(item.Content) };
    }

    // Bulk add: one item per non-empty line, encrypted before it ever reaches the store.
    [HttpPost]
    public ActionResult<IEnumerable<StockItemDto>> Add(AddStockInput input)
    {
        if (_store.GetProduct(input.ProductId) is null) return NotFound("محصول یافت نشد.");
        var lines = (input.Lines ?? new())
            .Select(l => (l ?? "").Trim())
            .Where(l => l.Length > 0)
            .Select(l => l.Length > MaxContentLength ? l[..MaxContentLength] : l)
            .ToList();
        if (lines.Count == 0) return BadRequest("هیچ آیتمی برای افزودن وارد نشده است.");
        if (lines.Count > MaxLinesPerAdd) return BadRequest($"حداکثر {MaxLinesPerAdd} آیتم در هر نوبت قابل افزودن است.");

        var added = _store.AddStockItems(input.ProductId, lines.Select(SensitiveField.Protect), User.Identity?.Name);
        return Ok(added.Select(StockItemDto.From));
    }

    [HttpPost("{id:int}/disable")]
    public IActionResult Disable(int id) =>
        _store.SetStockItemStatus(id, StockItemStatus.Disabled) ? Ok() : BadRequest("این آیتم قابل غیرفعال‌سازی نیست.");

    [HttpPost("{id:int}/enable")]
    public IActionResult Enable(int id) =>
        _store.SetStockItemStatus(id, StockItemStatus.Available) ? Ok() : BadRequest("این آیتم قابل فعال‌سازی نیست.");

    // Puts an abandoned Reserved item back into rotation (e.g. the admin closed the deliver modal).
    [HttpPost("{id:int}/release")]
    public IActionResult Release(int id) =>
        _store.SetStockItemStatus(id, StockItemStatus.Available) ? Ok() : BadRequest("این آیتم رزرو نیست.");

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id) =>
        _store.DeleteStockItem(id) ? Ok() : BadRequest("آیتم تحویل‌شده قابل حذف نیست.");

    // Toggles automatic pool fulfillment for a product (used by the stock page's per-product switch).
    [HttpPost("auto-deliver")]
    public IActionResult AutoDeliver(StockAutoDeliverInput input)
    {
        var product = _store.GetProduct(input.ProductId);
        if (product is null) return NotFound();
        product.AutoDeliverStock = input.Enabled;
        _store.UpdateProduct(product);
        return Ok();
    }

    // ── Multi-seat stock accounts ──────────────────────────────────────────────────────────────────

    [HttpGet("accounts")]
    public IEnumerable<StockAccountDto> Accounts([FromQuery] int? productId) =>
        _store.GetStockAccounts(productId).Select(StockAccountDto.From);

    // The product's active plan types, offered as bind options in the account form.
    [HttpGet("plan-types")]
    public ActionResult<IEnumerable<StockPlanTypeDto>> PlanTypes([FromQuery] int productId)
    {
        if (_store.GetProduct(productId) is not { } p) return NotFound("محصول یافت نشد.");
        return Ok(p.Plans.Where(pl => pl.IsActive).Select(pl => pl.Type).Distinct().Select(t => new StockPlanTypeDto(t)));
    }

    // Re-applies the current slot-delivery format to accounts delivered before the format changed.
    [HttpPost("reformat-deliveries")]
    public IActionResult ReformatDeliveries() => Ok(new { updated = _fulfillment.ReformatDeliveredSlotOrders() });

    // Sets the bare service name printed on this product's slot-delivery message (blank = auto-derive).
    [HttpPost("service-name")]
    public IActionResult SetServiceName(ServiceNameInput input)
    {
        var product = _store.GetProduct(input.ProductId);
        if (product is null) return NotFound();
        product.ServiceName = (input.ServiceName ?? "").Trim();
        _store.UpdateProduct(product);
        return Ok();
    }

    // Creates the account; the slots (A0, A1, … up to Capacity) are generated by the store, never typed in.
    [HttpPost("accounts")]
    public ActionResult<StockAccountDto> AddAccount(AddStockAccountInput input)
    {
        if (_store.GetProduct(input.ProductId) is not { } product) return NotFound("محصول یافت نشد.");
        if (string.IsNullOrWhiteSpace(input.Username) || string.IsNullOrWhiteSpace(input.Password))
            return BadRequest("نام کاربری و گذرواژه اکانت الزامی است.");
        if (input.Capacity is < 1 or > 1000) return BadRequest("ظرفیت اکانت باید بین ۱ تا ۱۰۰۰ باشد.");
        if (input.Months is < 1 or > 120) return BadRequest("مدت اشتراک باید بین ۱ تا ۱۲۰ ماه باشد.");
        // A bound plan type must be one the product actually offers, so a purchase can never fail to match it.
        var planType = (input.PlanType ?? "").Trim();
        if (planType.Length > 0 && !product.Plans.Any(pl => pl.IsActive && pl.Type == planType))
            return BadRequest("نوع پلن انتخاب‌شده برای این محصول معتبر نیست.");

        var account = _store.AddStockAccount(new StockAccount
        {
            ProductId = input.ProductId,
            Username = input.Username.Trim(),
            Password = SensitiveField.Protect(input.Password.Trim()),
            Plan = (input.Plan ?? "").Trim(),
            PlanType = planType,
            Capacity = input.Capacity,
            Months = input.Months,
            AddedBy = User.Identity?.Name,
        });
        return Ok(StockAccountDto.From(account));
    }

    // Reveals one account's password. Separate endpoint on purpose: the reveal is deliberate and audited.
    [HttpGet("accounts/{id:int}/content")]
    public ActionResult<object> AccountContent(int id)
    {
        var acc = _store.GetStockAccount(id);
        if (acc is null) return NotFound();
        return new { username = acc.Username, password = SensitiveField.Reveal(acc.Password) };
    }

    [HttpPost("accounts/{id:int}/disable")]
    public IActionResult DisableAccount(int id) =>
        _store.SetStockAccountDisabled(id, true) ? Ok() : NotFound();

    [HttpPost("accounts/{id:int}/enable")]
    public IActionResult EnableAccount(int id) =>
        _store.SetStockAccountDisabled(id, false) ? Ok() : NotFound();

    [HttpDelete("accounts/{id:int}")]
    public IActionResult DeleteAccount(int id) =>
        _store.DeleteStockAccount(id) ? Ok() : BadRequest("اکانتی که جای تحویل‌شده دارد قابل حذف نیست.");

    [HttpPost("accounts/{id:int}/slots/{slotId:int}/disable")]
    public IActionResult DisableSlot(int id, int slotId) =>
        _store.SetStockSlotStatus(id, slotId, StockItemStatus.Disabled) ? Ok() : BadRequest("این جایگاه قابل غیرفعال‌سازی نیست.");

    [HttpPost("accounts/{id:int}/slots/{slotId:int}/enable")]
    public IActionResult EnableSlot(int id, int slotId) =>
        _store.SetStockSlotStatus(id, slotId, StockItemStatus.Available) ? Ok() : BadRequest("این جایگاه قابل فعال‌سازی نیست.");

    // Puts an abandoned Reserved slot back into rotation.
    [HttpPost("accounts/{id:int}/slots/{slotId:int}/release")]
    public IActionResult ReleaseSlot(int id, int slotId) =>
        _store.SetStockSlotStatus(id, slotId, StockItemStatus.Available) ? Ok() : BadRequest("این جایگاه رزرو نیست.");

    private (StockAccount Account, List<StockSlot> Slots)? FindSlotReservation(int productId, int orderId, int unitId)
    {
        foreach (var acc in _store.GetStockAccounts(productId))
        {
            var mine = acc.Slots
                .Where(s => s.Status == StockItemStatus.Reserved && s.OrderId == orderId && s.UnitId == unitId)
                .OrderBy(s => s.Index)
                .ToList();
            if (mine.Count > 0) return (acc, mine);
        }
        return null;
    }

    // Toggles slot-account fulfillment for a product (its stock-page switch, like auto-deliver).
    [HttpPost("slot-fulfillment")]
    public IActionResult SlotFulfillment(SlotFulfillmentInput input)
    {
        var product = _store.GetProduct(input.ProductId);
        if (product is null) return NotFound();
        product.SlotFulfillment = input.Enabled;
        _store.UpdateProduct(product);
        return Ok();
    }

    // Reserves the next available item for an order unit and returns its payload — the deliver modal's
    // «برداشت از انبار». The item stays Reserved until the unit is actually delivered (DeliverUnit marks it
    // Delivered); an unused reservation is released from the stock page.
    [HttpPost("pull")]
    public ActionResult<object> Pull(PullStockInput input)
    {
        var order = _store.GetOrder(input.OrderId);
        var unit = order?.Units.FirstOrDefault(u => u.Id == input.UnitId);
        if (order is null || unit is null) return NotFound("سفارش یا واحد آن یافت نشد.");
        if (unit.Delivered) return BadRequest("این اکانت قبلاً تحویل شده است.");

        // Slot-fulfilled product → seat the whole purchased quantity on consecutive slots of one account and
        // hand back the ready-to-send delivery message.
        if (_store.GetProduct(unit.ProductId) is { SlotFulfillment: true } slotProduct)
        {
            var count = StockFulfillmentService.ConnectionCount(order, unit);
            var planType = StockFulfillmentService.PlanType(unit.Plan);
            // an earlier reservation for this unit is reused instead of claiming a second run of seats.
            var reservation = FindSlotReservation(unit.ProductId, order.Id, unit.Id)
                              ?? _store.ReserveStockSlots(unit.ProductId, count, planType, order.Id, unit.Id);
            if (reservation is not { } res) return BadRequest("هیچ اکانتی با جای خالی پیوسته کافی برای این نوع پلن موجود نیست.");
            var service = StockAccount.DeriveServiceName(slotProduct.ServiceName, unit.Name);
            return new
            {
                stockAccountId = res.Account.Id,
                content = StockFulfillmentService.BuildSlotDeliveryContent(service, res.Account, res.Slots),
            };
        }

        // a previous pull for this unit is reused instead of burning a second item.
        var existing = _store.GetStockItems(unit.ProductId)
            .FirstOrDefault(s => s.Status == StockItemStatus.Reserved && s.OrderId == order.Id && s.UnitId == unit.Id);
        var item = existing ?? _store.PullStockItem(unit.ProductId, order.Id, unit.Id);
        if (item is null) return BadRequest("انبار این محصول خالی است.");
        return new { stockItemId = item.Id, content = SensitiveField.Reveal(item.Content) };
    }
}

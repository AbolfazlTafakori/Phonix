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
    int Available, int Reserved, int Delivered, int Disabled);

[ApiController]
[Route("api/stock")]
[Authorize(Roles = AuthExtensions.StaffRoles)]
[AdminPermission("stock-pool")]
public class StockController : ControllerBase
{
    private const int MaxLinesPerAdd = 500;
    private const int MaxContentLength = 2000;

    private readonly IDataStore _store;
    public StockController(IDataStore store) => _store = store;

    // Per-product pool counters for the overview table. Every product appears (not just pooled ones) so the
    // admin can start a pool — or flip auto-delivery — without leaving the page.
    [HttpGet("summary")]
    public IEnumerable<StockSummaryDto> Summary()
    {
        var byProduct = _store.GetStockItems().GroupBy(s => s.ProductId).ToDictionary(g => g.Key, g => g.ToList());
        return _store.GetProducts().Select(p =>
        {
            var items = byProduct.TryGetValue(p.Id, out var list) ? list : new List<StockItem>();
            int Count(StockItemStatus st) => items.Count(s => s.Status == st);
            return new StockSummaryDto(p.Id, p.Name, string.IsNullOrWhiteSpace(p.Logo) ? p.Image : p.Logo,
                p.AutoDeliverStock, Count(StockItemStatus.Available), Count(StockItemStatus.Reserved),
                Count(StockItemStatus.Delivered), Count(StockItemStatus.Disabled));
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

        // a previous pull for this unit is reused instead of burning a second item.
        var existing = _store.GetStockItems(unit.ProductId)
            .FirstOrDefault(s => s.Status == StockItemStatus.Reserved && s.OrderId == order.Id && s.UnitId == unit.Id);
        var item = existing ?? _store.PullStockItem(unit.ProductId, order.Id, unit.Id);
        if (item is null) return BadRequest("انبار این محصول خالی است.");
        return new { stockItemId = item.Id, content = SensitiveField.Reveal(item.Content) };
    }
}

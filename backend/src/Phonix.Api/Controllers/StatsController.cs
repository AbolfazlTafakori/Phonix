using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Controllers;

public record OverviewDto(
    long Revenue, int OrdersCount, int PendingOrders, int PreparingOrders, int CompletedOrders,
    int UsersCount, int ProductsCount, int OpenTickets, int PendingComments, int PendingKyc);

public record TopProductDto(int ProductId, string Name, string Image, long Sold, long Revenue);

[ApiController]
[Route("api/stats")]
[Authorize(Roles = AuthExtensions.StaffRoles)]
[AdminPermission("reports")]
public class StatsController : ControllerBase
{
    private readonly IDataStore _store;
    public StatsController(IDataStore store) => _store = store;

    [HttpGet("overview")]
    public OverviewDto Overview()
    {
        var orders = _store.GetOrders();
        return new OverviewDto(
            Revenue: orders.Where(o => o.Status == OrderStatus.Completed).Sum(o => o.Total),
            OrdersCount: orders.Count,
            PendingOrders: orders.Count(o => o.Status == OrderStatus.PendingApproval),
            PreparingOrders: orders.Count(o => o.Status == OrderStatus.Preparing),
            CompletedOrders: orders.Count(o => o.Status == OrderStatus.Completed),
            UsersCount: _store.GetUsers().Count,
            ProductsCount: _store.GetProducts().Count,
            OpenTickets: _store.GetTickets(TicketStatus.Open).Count,
            PendingComments: _store.GetComments(status: CommentStatus.Pending).Count,
            PendingKyc: _store.GetAllKyc(KycStatus.Pending).Count);
    }

    // Ranks products by what actually sold. A cancelled order counts for nothing whoever cancelled it, and
    // neither does an individual account staff rejected inside an order that otherwise went through: the
    // buyer was refunded for it, so counting it would inflate the very product it was rejected from. The
    // lines stay on the order in both cases, which is why the rejected units have to be subtracted back out.
    [HttpGet("top-products")]
    public IEnumerable<TopProductDto> TopProducts()
    {
        var live = _store.GetOrders().Where(o => o.Status != OrderStatus.Cancelled).ToList();

        var rejected = live
            .SelectMany(o => o.Units)
            .Where(u => u.Rejected)
            .GroupBy(u => u.ProductId)
            .ToDictionary(g => g.Key, g => (Units: g.Count(), Refunded: g.Sum(u => u.RefundedAmount)));

        return live
            .SelectMany(o => o.Items)
            .GroupBy(i => i.ProductId)
            .Select(g =>
            {
                var off = rejected.TryGetValue(g.Key, out var r) ? r : (Units: 0, Refunded: 0L);
                return new TopProductDto(
                    g.Key, g.First().Name, g.First().Image,
                    Math.Max(0, g.Sum(x => x.Quantity) - off.Units),
                    Math.Max(0, g.Sum(x => x.LineTotal) - off.Refunded));
            })
            // Every unit rejected leaves nothing sold — such a product drops out rather than sitting at zero.
            .Where(p => p.Sold > 0)
            .OrderByDescending(x => x.Sold)
            // The dashboard panel scrolls its own list now, so it is no longer limited to what fits in a
            // fixed-height card — a shop with a wide catalogue gets a ranking worth scrolling through.
            .Take(12)
            .ToList();
    }
}

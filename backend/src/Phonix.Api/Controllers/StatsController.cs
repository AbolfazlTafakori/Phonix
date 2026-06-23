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
    private readonly StoreData _store;
    public StatsController(StoreData store) => _store = store;

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

    [HttpGet("top-products")]
    public IEnumerable<TopProductDto> TopProducts()
    {
        return _store.GetOrders()
            .Where(o => o.Status != OrderStatus.Cancelled)
            .SelectMany(o => o.Items)
            .GroupBy(i => i.ProductId)
            .Select(g => new TopProductDto(g.Key, g.First().Name, g.First().Image, g.Sum(x => x.Quantity), g.Sum(x => x.LineTotal)))
            .OrderByDescending(x => x.Sold)
            .Take(6)
            .ToList();
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;
using Phonix.Api.Services;

namespace Phonix.Api.Controllers;

public record OrderLineInput(int ProductId, int Quantity, int? PlanId);
public record PlaceOrderInput(List<OrderLineInput> Items, string PaymentMethod, bool FromWallet, string? DiscountCode, int? PaymentMethodId);
public record DeliverInput(string Content, bool Email, string? EmailSubject, string? EmailBody);

[ApiController]
[Route("api/orders")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly StoreData _store;
    private readonly IEmailSender _email;
    public OrdersController(StoreData store, IEmailSender email)
    {
        _store = store;
        _email = email;
    }

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [HttpGet]
    public IEnumerable<Order> Get([FromQuery] OrderStatus? status) => _store.GetOrders(status);

    [HttpGet("user/{userId:int}")]
    public ActionResult<IEnumerable<Order>> ForUser(int userId)
    {
        if (!this.OwnsOrStaff(userId)) return Forbid();
        return Ok(_store.GetUserOrders(userId));
    }

    [HttpGet("{id:int}")]
    public ActionResult<Order> Get(int id)
    {
        var order = _store.GetOrder(id);
        if (order is null) return NotFound();
        if (!this.OwnsOrStaff(order.UserId)) return Forbid();
        return order;
    }

    [HttpPost]
    public ActionResult<Order> Place(PlaceOrderInput input)
    {
        if (input.Items is null || input.Items.Count == 0)
            return BadRequest("سبد خرید خالی است.");

        // the order is always placed for the authenticated user, never a client-supplied id.
        var userId = this.CurrentUserId();
        var user = userId is int uid ? _store.GetUser(uid) : null;
        if (user is null) return Unauthorized();
        if (!user.EmailVerified) return StatusCode(403, "برای ثبت سفارش ابتدا ایمیل خود را تأیید کنید.");

        var result = _store.PlaceOrder(
            user,
            input.Items.Select(i => (i.ProductId, i.Quantity, i.PlanId)),
            input.PaymentMethod,
            input.FromWallet,
            input.DiscountCode,
            input.PaymentMethodId);
        if (result.Error is not null) return BadRequest(result.Error);
        return result.Order!;
    }

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [HttpPost("{id:int}/approve")]
    public ActionResult<Order> Approve(int id) =>
        _store.SetOrderStatus(id, OrderStatus.Preparing) is { } o ? o : NotFound();

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [HttpPost("{id:int}/complete")]
    public ActionResult<Order> Complete(int id) =>
        _store.SetOrderStatus(id, OrderStatus.Completed) is { } o ? o : NotFound();

    // delivers the order: stores the in-site content (shown in the buyer's account) and,
    // when requested, emails the buyer the (manually written) message.
    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [HttpPost("{id:int}/deliver")]
    public async Task<ActionResult<Order>> Deliver(int id, DeliverInput input)
    {
        var order = _store.DeliverOrder(id, (input.Content ?? "").Trim());
        if (order is null) return NotFound();

        if (input.Email)
        {
            var user = _store.GetUser(order.UserId);
            var subject = string.IsNullOrWhiteSpace(input.EmailSubject) ? $"سفارش {order.Code} آماده شد" : input.EmailSubject!;
            var body = string.IsNullOrWhiteSpace(input.EmailBody)
                ? "سفارش شما آماده شد. برای مشاهده به حساب کاربری خود، بخش سفارش‌ها مراجعه کنید."
                : input.EmailBody!;
            if (user is not null) await _email.SendAsync(user.Email, subject, body);
        }

        return order;
    }

    [HttpPost("{id:int}/cancel")]
    public ActionResult<Order> Cancel(int id)
    {
        var order = _store.GetOrder(id);
        if (order is null) return NotFound();
        if (!this.OwnsOrStaff(order.UserId)) return Forbid();
        var result = _store.CancelOrder(id);
        if (result.Error is not null) return BadRequest(result.Error);
        return result.Order!;
    }
}

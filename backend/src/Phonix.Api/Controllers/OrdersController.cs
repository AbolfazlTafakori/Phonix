using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Phonix.Api.Data;
using Phonix.Api.Dtos;
using Phonix.Api.Models;
using Phonix.Api.Security;
using Phonix.Api.Services;

namespace Phonix.Api.Controllers;

public record CustomerInputDto(string Label, string Value);
public record OrderLineInput(int ProductId, int Quantity, int? PlanId, List<CustomerInputDto>? Inputs = null, string? Note = null);
public record PlaceOrderInput(List<OrderLineInput> Items, string PaymentMethod, bool FromWallet, string? DiscountCode, int? PaymentMethodId, int? CardId, string? ReceiptUrl, string? TrackingNumber, string? PaymentDate, string? Description);
public record DeliverInput(string Content, bool Email, string? EmailSubject, string? EmailBody);
public record CancelOrderInput(string? Reason);

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

    private static string FrontendUrl => Environment.GetEnvironmentVariable("PHONIX_FRONTEND_URL") ?? "http://localhost:3000";

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("orders", "orders-status")]
    [HttpGet]
    public IEnumerable<Order> Get([FromQuery] OrderStatus? status) => _store.GetOrders(status).Select(RevealInputs);

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("orders", "orders-status")]
    [HttpGet("page")]
    public PagedResult<Order> GetPage([FromQuery] OrderStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        PagedResult<Order>.From(_store.GetOrders(status).Select(RevealInputs).ToList(), page, pageSize);

    [HttpGet("user/{userId:int}")]
    public ActionResult<IEnumerable<Order>> ForUser(int userId)
    {
        if (!this.OwnsOrStaff(userId)) return Forbid();
        return Ok(_store.GetUserOrders(userId).Select(RevealInputs));
    }

    [HttpGet("{id:int}")]
    public ActionResult<Order> Get(int id)
    {
        var order = _store.GetOrder(id);
        if (order is null) return NotFound();
        if (!this.OwnsOrStaff(order.UserId)) return Forbid();
        return RevealInputs(order);
    }

    // Returns a deep clone of the order with any encrypted sensitive customer inputs decrypted for display.
    // Cloning keeps the live store entity untouched (decrypting in place would persist the plaintext back to
    // store.json on the next flush). Orders without sensitive inputs are returned as-is to avoid the copy.
    private static Order RevealInputs(Order order)
    {
        if (!order.Items.Any(i => i.CustomerInputs.Any(v => v.Sensitive))) return order;
        var clone = JsonSerializer.Deserialize<Order>(JsonSerializer.Serialize(order))!;
        foreach (var item in clone.Items)
            foreach (var v in item.CustomerInputs)
                if (v.Sensitive) v.Value = SensitiveField.Reveal(v.Value);
        return clone;
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

        // When a card-to-card payment date is supplied for the remainder, it must be a real date and not in
        // the future; the store enforces presence, here we enforce validity (the client cannot be trusted).
        if (!string.IsNullOrWhiteSpace(input.PaymentDate) && !JalaliDate.IsValidAndNotFuture(input.PaymentDate))
            return BadRequest("تاریخ پرداخت نامعتبر است یا از امروز جلوتر است.");

        // Validate and capture any per-plan customer inputs (e.g. account email/password) before placing the
        // order. Required fields are enforced here so the client can't skip them; sensitive values are
        // encrypted before they ever reach the store.
        var lineInfo = new List<OrderLineInfo>();
        foreach (var line in input.Items)
        {
            var info = BuildLineInfo(line, out var error);
            if (error is not null) return BadRequest(error);
            lineInfo.Add(info);
        }

        var result = _store.PlaceOrder(
            user,
            input.Items.Select(i => (i.ProductId, i.Quantity, i.PlanId)),
            input.PaymentMethod,
            input.FromWallet,
            input.DiscountCode,
            input.PaymentMethodId,
            new RemainderPayment(input.CardId, input.ReceiptUrl, input.TrackingNumber, input.PaymentDate, input.Description),
            customerCheckout: true,
            lineInfo: lineInfo);
        if (result.Error is not null) return BadRequest(result.Error);
        return RevealInputs(result.Order!);
    }

    private const int MaxInputLength = 1000;
    private const int MaxNoteLength = 2000;

    // Validates a single checkout line's customer inputs against its plan definition and returns the values
    // ready to store (sensitive ones encrypted). Lines whose plan collects nothing yield an empty result.
    private OrderLineInfo BuildLineInfo(OrderLineInput line, out string? error)
    {
        error = null;
        var empty = new OrderLineInfo(new List<OrderInputValue>(), null);

        if (line.PlanId is not int planId) return empty;
        var plan = _store.GetProduct(line.ProductId)?.Plans.FirstOrDefault(p => p.Id == planId && p.IsActive);
        if (plan is null || !plan.CollectsInfo) return empty;

        var supplied = line.Inputs ?? new();
        var values = new List<OrderInputValue>();
        foreach (var field in plan.InputFields)
        {
            var raw = supplied.FirstOrDefault(s => s.Label == field.Label)?.Value?.Trim() ?? "";
            if (raw.Length == 0)
            {
                if (field.Required) { error = $"فیلد «{field.Label}» الزامی است."; return empty; }
                continue;
            }
            if (raw.Length > MaxInputLength) raw = raw[..MaxInputLength];
            values.Add(new OrderInputValue
            {
                Label = field.Label,
                Value = field.Sensitive ? SensitiveField.Protect(raw) : raw,
                Sensitive = field.Sensitive,
            });
        }

        string? note = null;
        if (plan.AllowNotes && !string.IsNullOrWhiteSpace(line.Note))
        {
            note = line.Note.Trim();
            if (note.Length > MaxNoteLength) note = note[..MaxNoteLength];
        }
        return new OrderLineInfo(values, note);
    }

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("orders")]
    [HttpPost("{id:int}/approve")]
    public ActionResult<Order> Approve(int id) =>
        _store.SetOrderStatus(id, OrderStatus.Preparing, User.Identity?.Name, "تأیید سفارش") is { } o ? o : NotFound();

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("orders")]
    [HttpPost("{id:int}/complete")]
    public ActionResult<Order> Complete(int id) =>
        _store.SetOrderStatus(id, OrderStatus.Completed, User.Identity?.Name, "تکمیل سفارش") is { } o ? o : NotFound();

    // delivers the order: stores the in-site content (shown in the buyer's account) and,
    // when requested, emails the buyer the (manually written) message.
    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("orders")]
    [HttpPost("{id:int}/deliver")]
    public async Task<ActionResult<Order>> Deliver(int id, DeliverInput input)
    {
        var order = _store.DeliverOrder(id, (input.Content ?? "").Trim(), User.Identity?.Name);
        if (order is null) return NotFound();

        if (input.Email)
        {
            var user = _store.GetUser(order.UserId);
            var subject = string.IsNullOrWhiteSpace(input.EmailSubject) ? $"سفارش {order.Code} آماده شد" : input.EmailSubject!;
            var accountUrl = $"{FrontendUrl}/account";
            var (text, html) = EmailTemplates.OrderDelivered(order.Code, accountUrl, input.EmailBody);
            if (user is not null) await _email.SendAsync(user.Email, subject, text, html);
        }

        return order;
    }

    [HttpPost("{id:int}/cancel")]
    public ActionResult<Order> Cancel(int id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CancelOrderInput? input)
    {
        var order = _store.GetOrder(id);
        if (order is null) return NotFound();
        if (!this.OwnsOrStaff(order.UserId)) return Forbid();
        // a staff cancellation can carry an explicit reason; a customer self-cancel falls back to a default.
        var reason = string.IsNullOrWhiteSpace(input?.Reason)
            ? (this.IsStaff() ? "لغو توسط پشتیبانی" : "لغو توسط کاربر")
            : input!.Reason;
        var result = _store.CancelOrder(id, User.Identity?.Name, reason);
        if (result.Error is not null) return BadRequest(result.Error);
        return result.Order!;
    }
}

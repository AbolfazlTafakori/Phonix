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
// One account's worth of customer inputs at checkout. A line with quantity 2 sends two of these.
public record UnitInputDto(List<CustomerInputDto>? Inputs, string? Note);
public record OrderLineInput(int ProductId, int Quantity, int? PlanId, List<UnitInputDto>? Units = null, List<CustomerInputDto>? Inputs = null, string? Note = null);
public record PlaceOrderInput(List<OrderLineInput> Items, string PaymentMethod, bool FromWallet, string? DiscountCode, int? PaymentMethodId, int? CardId, string? ReceiptUrl, string? TrackingNumber, string? PaymentDate, string? Description);
public record DeliverInput(string Content, bool Email, string? EmailSubject, string? EmailBody);
public record RejectOrderInput(string? Reason);
public record DeliverUnitInput(string Content, bool Email, string? EmailSubject, string? EmailBody, bool Final);
public record CancelOrderInput(string? Reason);

[ApiController]
[Route("api/orders")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly IDataStore _store;
    private readonly IEmailSender _email;
    private readonly ITelegramReceiptService _receiptBot;
    private readonly ITelegramOrderService _orderBot;
    private readonly IUserMailer _mailer;
    public OrdersController(IDataStore store, IEmailSender email, ITelegramReceiptService receiptBot,
        ITelegramOrderService orderBot, IUserMailer mailer)
    {
        _store = store;
        _email = email;
        _receiptBot = receiptBot;
        _orderBot = orderBot;
        _mailer = mailer;
    }

    // Announces an order's accounts to the orders group. The claim makes this safe to call from every approval
    // path: only the first one through actually posts.
    private void AnnounceToOrderBot(Order order)
    {
        if (order.Status != OrderStatus.Preparing) return;
        if (!_store.TryClaimOrderBotNotification(order.Id)) return;
        // Pass the stored order as-is: the bot decrypts the sensitive inputs itself when it builds the message.
        _ = _orderBot.NotifyOrderAsync(order);
    }

    private static string FrontendUrl => Environment.GetEnvironmentVariable("PHONIX_FRONTEND_URL") ?? "http://localhost:3000";

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("orders", "orders-receipts", "orders-fulfillment", "orders-status")]
    [HttpGet]
    public IEnumerable<Order> Get([FromQuery] OrderStatus? status) => _store.GetOrders(status).Select(RevealInputs);

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("orders", "orders-receipts", "orders-fulfillment", "orders-status")]
    [HttpGet("page")]
    public PagedResult<Order> GetPage([FromQuery] OrderStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) =>
        PagedResult<Order>.From(_store.GetOrders(status).Select(RevealInputs).ToList(), page, pageSize);

    // Every issued invoice is a completed order — the 16-digit number is minted exactly at that transition, so
    // an order that was never delivered simply has no invoice and never shows up here. `q` matches the invoice
    // number, the order code or the buyer's name. Customer inputs stay masked: an invoice never needs them.
    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("invoices")]
    [HttpGet("invoices")]
    public PagedResult<Order> Invoices([FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var invoices = _store.GetOrders(OrderStatus.Completed)
            .Where(o => !string.IsNullOrWhiteSpace(o.InvoiceNumber));

        var term = (q ?? "").Trim();
        if (term.Length > 0)
        {
            // Staff type the number however their keyboard is set, so "۱۲۳" has to match "123".
            var digits = InputValidation.DigitsOnly(term);
            invoices = invoices.Where(o =>
                (digits.Length > 0 && (o.InvoiceNumber ?? "").Contains(digits, StringComparison.Ordinal))
                || o.Code.Contains(term, StringComparison.OrdinalIgnoreCase)
                || o.UserName.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        return PagedResult<Order>.From(invoices.ToList(), page, pageSize);
    }

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
        var hasSensitive = order.Units.Any(u => u.CustomerInputs.Any(v => v.Sensitive))
            || order.Items.Any(i => i.CustomerInputs.Any(v => v.Sensitive));
        if (!hasSensitive) return order;
        var clone = JsonSerializer.Deserialize<Order>(JsonSerializer.Serialize(order))!;
        foreach (var unit in clone.Units)
            foreach (var v in unit.CustomerInputs)
                if (v.Sensitive) v.Value = SensitiveField.Reveal(v.Value);
        foreach (var item in clone.Items)               // legacy orders captured inputs at the line level
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

        var order = result.Order!;
        // Card-to-card remainder → push its receipt to the admin Telegram chat for one-tap approve/reject
        // (no-op unless the receipt bot is enabled). Fire-and-forget: checkout never waits on Telegram.
        var payTx = _store.GetUserTransactions(order.UserId)
            .FirstOrDefault(t => t.OrderCode == order.Code && t.Type == TxTypes.OrderPayment && t.Status == TxStatus.Pending);
        if (payTx is not null) _ = _receiptBot.NotifyDepositAsync(payTx, CancellationToken.None);

        // The customer's own copy of the order. Fire-and-forget for the same reason: checkout must not fail
        // because SMTP is slow or down.
        _ = _mailer.OrderPlacedAsync(order);

        return RevealInputs(order);
    }

    private const int MaxInputLength = 1000;
    private const int MaxNoteLength = 2000;

    // Validates a checkout line's customer inputs against its plan and returns one entry per account the
    // customer is buying (quantity), with sensitive values encrypted. Lines whose plan collects nothing yield
    // null so the store still creates plain units. A required field missing on ANY account rejects the order.
    private OrderLineInfo BuildLineInfo(OrderLineInput line, out string? error)
    {
        error = null;
        if (line.PlanId is not int planId) return new OrderLineInfo(null);
        var plan = _store.GetProduct(line.ProductId)?.Plans.FirstOrDefault(p => p.Id == planId && p.IsActive);
        if (plan is null || !plan.CollectsInfo) return new OrderLineInfo(null);

        var qty = Math.Clamp(line.Quantity, 1, 100);
        // Per-unit groups; fall back to the legacy single Inputs/Note when Units isn't supplied.
        var groups = line.Units is { Count: > 0 }
            ? line.Units
            : new List<UnitInputDto> { new(line.Inputs, line.Note) };

        var units = new List<OrderUnitInfo>();
        for (var u = 0; u < qty; u++)
        {
            var group = u < groups.Count ? groups[u] : null;
            var supplied = group?.Inputs ?? new();
            var values = new List<OrderInputValue>();
            foreach (var field in plan.InputFields)
            {
                var raw = supplied.FirstOrDefault(s => s.Label == field.Label)?.Value?.Trim() ?? "";
                if (raw.Length == 0)
                {
                    if (field.Required) { error = $"فیلد «{field.Label}» برای اکانت {u + 1} الزامی است."; return new OrderLineInfo(null); }
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
            if (plan.AllowNotes && !string.IsNullOrWhiteSpace(group?.Note))
            {
                note = group!.Note!.Trim();
                if (note.Length > MaxNoteLength) note = note[..MaxNoteLength];
            }
            units.Add(new OrderUnitInfo(values, note));
        }
        return new OrderLineInfo(units);
    }

    // ── Receipt approval (financial team) ──
    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("orders", "orders-receipts")]
    [HttpPost("{id:int}/approve")]
    public ActionResult<Order> Approve(int id)
    {
        if (_store.SetOrderStatus(id, OrderStatus.Preparing, User.Identity?.Name, "تأیید رسید") is not { } o)
            return NotFound();
        AnnounceToOrderBot(o);
        return RevealInputs(o);
    }

    // Rejects the deposit receipt: cancels the order (restoring stock) with the staff-supplied reason.
    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("orders", "orders-receipts")]
    [HttpPost("{id:int}/reject")]
    public ActionResult<Order> Reject(int id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RejectOrderInput? input)
    {
        var reason = string.IsNullOrWhiteSpace(input?.Reason) ? "رد رسید توسط بخش مالی" : input!.Reason!.Trim();
        var result = _store.CancelOrder(id, User.Identity?.Name, reason);
        if (result.Error is not null) return BadRequest(result.Error);
        return RevealInputs(result.Order!);
    }

    // ── Fulfillment (technical team), per account/unit ──
    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("orders", "orders-fulfillment")]
    [HttpPost("{id:int}/complete")]
    public ActionResult<Order> Complete(int id) =>
        _store.SetOrderStatus(id, OrderStatus.Completed, User.Identity?.Name, "تکمیل سفارش") is { } o ? RevealInputs(o) : NotFound();

    // Temporary save: keeps an account's in-progress delivery content without delivering it.
    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("orders", "orders-fulfillment")]
    [HttpPost("{id:int}/units/{unitId:int}/draft")]
    public ActionResult<Order> SaveUnitDraft(int id, int unitId, DeliverInput input)
    {
        var order = _store.SaveUnitDraft(id, unitId, (input.Content ?? "").Trim(), User.Identity?.Name);
        return order is null ? NotFound() : RevealInputs(order);
    }

    // Delivers a single account: stores its content (shown in the buyer's account), optionally emails the
    // buyer, and completes the whole order once the last account is delivered.
    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("orders", "orders-fulfillment")]
    [HttpPost("{id:int}/units/{unitId:int}/deliver")]
    public async Task<ActionResult<Order>> DeliverUnit(int id, int unitId, DeliverUnitInput input)
    {
        var (order, justCompleted) = _store.DeliverUnit(id, unitId, (input.Content ?? "").Trim(), User.Identity?.Name);
        if (order is null) return NotFound();

        if (input.Email)
        {
            var user = _store.GetUser(order.UserId);
            var subject = string.IsNullOrWhiteSpace(input.EmailSubject) ? $"سفارش {order.Code} آماده شد" : input.EmailSubject!;
            var accountUrl = $"{FrontendUrl}/account";
            var (text, html) = EmailTemplates.OrderDelivered(order.Code, accountUrl, input.EmailBody);
            if (user is not null) await _email.SendAsync(user.Email, subject, text, html);
        }

        return RevealInputs(order);
    }

    // Legacy whole-order deliver (kept for older flows): stores the in-site content and optionally emails.
    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("orders", "orders-fulfillment")]
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

        return RevealInputs(order);
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

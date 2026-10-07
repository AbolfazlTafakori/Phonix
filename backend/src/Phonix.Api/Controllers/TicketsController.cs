using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;
using Phonix.Api.Services;

namespace Phonix.Api.Controllers;

public record CreateTicketInput(string Subject, string Department, string Body, TicketPriority? Priority, string? Attachment);
public record AdminCreateTicketInput(int UserId, string Subject, string Department, string Body, TicketPriority? Priority, string? Attachment);
public record TicketReplyInput(string Body, bool IsAdmin, string? Attachment);

[ApiController]
[Route("api/tickets")]
[Authorize]
public class TicketsController : ControllerBase
{
    private readonly IDataStore _store;
    private readonly IUserMailer _mailer;
    private readonly ITelegramSupportBot? _support;
    public TicketsController(IDataStore store, IUserMailer mailer, ITelegramSupportBot? support = null)
    {
        _store = store;
        _mailer = mailer;
        _support = support;
    }

    // An attachment is always a picture uploaded to this site (/api/upload/<id>). It is shown to staff as a link
    // and forwarded to the support group, so anything else — someone else's site, a script URL — is dropped
    // rather than stored as an «attachment» staff would trust and open.
    private static string SafeAttachment(string? value)
    {
        var v = (value ?? "").Trim();
        return System.Text.RegularExpressions.Regex.IsMatch(v, @"^/api/upload/\d{1,9}__[0-9a-f]{32}\.(?:jpg|jpeg|png|webp)$") ? v : "";
    }

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("tickets")]
    [HttpGet]
    public IEnumerable<Ticket> Get([FromQuery] TicketStatus? status) => _store.GetTickets(status);

    [HttpGet("user/{userId:int}")]
    public ActionResult<IEnumerable<Ticket>> ForUser(int userId)
    {
        if (!this.OwnsOrStaff(userId)) return Forbid();
        return Ok(_store.GetUserTickets(userId));
    }

    [HttpGet("{id:int}")]
    public ActionResult<Ticket> Get(int id)
    {
        var ticket = _store.GetTicket(id);
        if (ticket is null) return NotFound();
        if (!this.OwnsOrStaff(ticket.UserId)) return Forbid();
        return ticket;
    }

    [HttpPost]
    public ActionResult<Ticket> Create(CreateTicketInput input)
    {
        var userId = this.CurrentUserId();
        var user = userId is int uid ? _store.GetUser(uid) : null;
        if (user is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(input.Subject) || string.IsNullOrWhiteSpace(input.Body))
            return BadRequest("موضوع و متن پیام الزامی است.");
        var name = string.IsNullOrWhiteSpace(user.Name) ? user.Username : user.Name;
        var ticket = _store.CreateTicket(user.Id, name, input.Subject, input.Department, input.Body,
            input.Priority ?? TicketPriority.Medium, SafeAttachment(input.Attachment));
        // Into the support group, where staff can answer it by replying.
        if (_support is not null) _ = _support.NotifyTicketOpenedAsync(ticket);
        return ticket;
    }

    // Staff opens a ticket ON BEHALF OF a user: the thread appears in that user's account, already answered
    // by support. Gated to staff with the "tickets" section, same as the rest of the support inbox.
    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("tickets")]
    [HttpPost("admin")]
    public ActionResult<Ticket> CreateForUser(AdminCreateTicketInput input)
    {
        var target = _store.GetUser(input.UserId);
        if (target is null) return NotFound("کاربر یافت نشد.");
        if (string.IsNullOrWhiteSpace(input.Subject) || string.IsNullOrWhiteSpace(input.Body))
            return BadRequest("موضوع و متن پیام الزامی است.");
        var name = string.IsNullOrWhiteSpace(target.Name) ? target.Username : target.Name;
        var ticket = _store.CreateTicketForUser(target.Id, name, input.Subject, input.Department, input.Body,
            "پشتیبانی فونیکس", input.Priority ?? TicketPriority.Medium, SafeAttachment(input.Attachment));
        // The in-app notification only lands if they come back to the site; support opened this thread, so
        // reach them where they are.
        _ = _mailer.TicketOpenedByStaffAsync(ticket);
        if (_support is not null) _ = _support.NotifyTicketOpenedAsync(ticket);
        return ticket;
    }

    [HttpPost("{id:int}/reply")]
    public ActionResult<Ticket> Reply(int id, TicketReplyInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Body)) return BadRequest("متن پیام خالی است.");
        var ticket = _store.GetTicket(id);
        if (ticket is null) return NotFound();
        if (!this.OwnsOrStaff(ticket.UserId)) return Forbid();

        // only staff may post a reply as support; a customer can never impersonate it.
        var isAdmin = input.IsAdmin && this.IsStaff();
        var author = isAdmin ? "پشتیبانی فونیکس" : ticket.UserName;
        var t = _store.ReplyTicket(id, author, input.Body, isAdmin, SafeAttachment(input.Attachment));
        if (t is null) return NotFound();
        // Only a support reply is worth an email — the customer's own reply doesn't need mailing back to them.
        if (isAdmin) _ = _mailer.TicketRepliedAsync(t);
        // The support group sees the customer's reply (to answer it) or the panel's answer (so it's handled).
        if (_support is not null && t.Messages.LastOrDefault() is { } posted)
            _ = _support.NotifyTicketMessageAsync(t, posted, isAdmin ? User.Identity?.Name : null);
        return t;
    }

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("tickets")]
    [HttpPost("{id:int}/close")]
    public IActionResult Close(int id) => _store.SetTicketStatus(id, TicketStatus.Closed) ? NoContent() : NotFound();
}

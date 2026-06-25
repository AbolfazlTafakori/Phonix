using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Controllers;

public record CreateTicketInput(string Subject, string Department, string Body, TicketPriority? Priority, string? Attachment);
public record AdminCreateTicketInput(int UserId, string Subject, string Department, string Body, TicketPriority? Priority, string? Attachment);
public record TicketReplyInput(string Body, bool IsAdmin, string? Attachment);

[ApiController]
[Route("api/tickets")]
[Authorize]
public class TicketsController : ControllerBase
{
    private readonly StoreData _store;
    public TicketsController(StoreData store) => _store = store;

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
        return _store.CreateTicket(user.Id, name, input.Subject, input.Department, input.Body,
            input.Priority ?? TicketPriority.Medium, input.Attachment ?? "");
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
        return _store.CreateTicketForUser(target.Id, name, input.Subject, input.Department, input.Body,
            "پشتیبانی فونیکس", input.Priority ?? TicketPriority.Medium, input.Attachment ?? "");
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
        var t = _store.ReplyTicket(id, author, input.Body, isAdmin, input.Attachment);
        return t is null ? NotFound() : t;
    }

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("tickets")]
    [HttpPost("{id:int}/close")]
    public IActionResult Close(int id) => _store.SetTicketStatus(id, TicketStatus.Closed) ? NoContent() : NotFound();
}

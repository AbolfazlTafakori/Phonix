using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Controllers;

public record NotificationDto(int Id, string Title, string Body, string? Link, bool IsPublic, bool IsRead, string CreatedAtUtc);
public record SendNotificationInput(int? UserId, string Title, string Body, string? Link);

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly IDataStore _store;
    public NotificationsController(IDataStore store) => _store = store;

    private static NotificationDto ToDto(Notification n, int viewerId) =>
        new(n.Id, n.Title, n.Body, n.Link, n.UserId is null, n.ReadBy.Contains(viewerId), n.CreatedAtUtc);

    // the signed-in user's feed (their private notifications + every public broadcast) and the unread count.
    [HttpGet]
    public ActionResult<IEnumerable<NotificationDto>> Mine()
    {
        if (this.CurrentUserId() is not int id) return Unauthorized();
        return Ok(_store.GetUserNotifications(id).Select(n => ToDto(n, id)));
    }

    [HttpGet("unread-count")]
    public ActionResult<int> UnreadCount()
    {
        if (this.CurrentUserId() is not int id) return Unauthorized();
        return _store.CountUnread(id);
    }

    [HttpPost("read")]
    public IActionResult MarkRead()
    {
        if (this.CurrentUserId() is not int id) return Unauthorized();
        _store.MarkNotificationsRead(id);
        return NoContent();
    }

    // staff: send a private message to one user (UserId set) or broadcast to everyone (UserId null).
    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("notifications")]
    [HttpPost]
    public ActionResult<Notification> Send(SendNotificationInput input)
    {
        var title = (input.Title ?? "").Trim();
        var body = (input.Body ?? "").Trim();
        if (title.Length == 0) return BadRequest("عنوان پیام الزامی است.");
        if (input.UserId is int uid && _store.GetUser(uid) is null) return BadRequest("کاربر یافت نشد.");
        // The link is rendered as an href in the customer's notification bell. A notification can be
        // BROADCAST to every user, so a "javascript:" or "data:" value here would be a stored injection
        // shipped to the whole customer base by anyone holding the notifications section. The nonce-based
        // CSP already refuses to execute those, but a defence that depends on one header holding is not a
        // defence — this is meant to be an in-app destination, so only an in-app path is accepted.
        var link = string.IsNullOrWhiteSpace(input.Link) ? null : input.Link.Trim();
        if (link is not null && !link.StartsWith('/'))
            return BadRequest("لینک باید یک مسیر داخلی سایت باشد (با / شروع شود).");
        // "//evil.example" is protocol-relative: it starts with '/' but leaves the site entirely.
        if (link is not null && link.StartsWith("//", StringComparison.Ordinal))
            return BadRequest("لینک باید یک مسیر داخلی سایت باشد (با / شروع شود).");
        return _store.AddNotification(input.UserId, title, body, link);
    }

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("notifications")]
    [HttpGet("all")]
    public IEnumerable<Notification> All() => _store.GetAllNotifications();

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("notifications")]
    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id) => _store.DeleteNotification(id) ? NoContent() : NotFound();
}

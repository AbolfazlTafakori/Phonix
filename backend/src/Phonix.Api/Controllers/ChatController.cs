using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Controllers;

public record ChatSendInput(string Body);

public record ConversationSummaryDto(int Id, int UserId, string UserName, string Status, string LastMessageAtUtc, string LastPreview, int Unread);

// Customer-facing projection of a live-chat thread. Deliberately omits AdminReadUpTo (support-side read
// state) and any other staff bookkeeping — the customer only sees their own thread and messages. Built from
// a DETACHED snapshot (see StoreData.Clone), so the Messages list it carries is safe to serialize.
public record ChatThreadDto(
    int Id,
    int UserId,
    string UserName,
    string Status,
    string CreatedAtUtc,
    string LastMessageAtUtc,
    int UserReadUpTo,
    IReadOnlyList<ChatMessage> Messages)
{
    public static ChatThreadDto From(ChatConversation c) => new(
        c.Id, c.UserId, c.UserName, c.Status.ToString(), c.CreatedAtUtc, c.LastMessageAtUtc, c.UserReadUpTo, c.Messages);
}

[ApiController]
[Route("api/chat")]
[Authorize]
public class ChatController : ControllerBase
{
    private const int MaxBody = 2000;

    private readonly IDataStore _store;
    public ChatController(IDataStore store) => _store = store;

    // ── Customer side: a single live thread with support ──────────────────────────────────────────────

    [HttpGet("me")]
    public ActionResult<ChatThreadDto?> Mine()
    {
        if (this.CurrentUserId() is not int id) return Unauthorized();
        var conv = _store.GetUserConversation(id);
        return conv is null ? null : ChatThreadDto.From(conv);
    }

    [HttpGet("me/unread")]
    public ActionResult<int> MyUnread()
    {
        if (this.CurrentUserId() is not int id) return Unauthorized();
        return _store.CountUnreadForUser(id);
    }

    [HttpPost("me/messages")]
    public ActionResult<ChatThreadDto> SendMine(ChatSendInput input)
    {
        if (this.CurrentUserId() is not int id) return Unauthorized();
        var user = _store.GetUser(id);
        if (user is null) return Unauthorized();
        var body = Clean(input.Body);
        if (body.Length == 0) return BadRequest("متن پیام خالی است.");
        var name = string.IsNullOrWhiteSpace(user.Name) ? user.Username : user.Name;
        return ChatThreadDto.From(_store.SendUserMessage(id, name, body));
    }

    [HttpPost("me/read")]
    public IActionResult ReadMine()
    {
        if (this.CurrentUserId() is not int id) return Unauthorized();
        if (_store.GetUserConversation(id) is { } conv) _store.MarkConversationRead(conv.Id, byAdmin: false);
        return NoContent();
    }

    // Archives the customer's open thread so their widget starts empty again. The LiveChat widget calls this
    // once per new browser session (tracked in sessionStorage), giving "close the browser → chat resets"
    // without losing anything on the support side.
    [HttpPost("me/reset")]
    public IActionResult ResetMine()
    {
        if (this.CurrentUserId() is not int id) return Unauthorized();
        _store.CloseUserConversation(id);
        return NoContent();
    }

    // ── Staff side: every thread, separated per customer ──────────────────────────────────────────────

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("chat")]
    [HttpGet]
    public IEnumerable<ConversationSummaryDto> List() =>
        _store.GetConversations().Select(c => new ConversationSummaryDto(
            c.Id, c.UserId, c.UserName, c.Status.ToString(), c.LastMessageAtUtc,
            c.Messages.Count == 0 ? "" : c.Messages[^1].Body,
            _store.UnreadMessagesForAdmin(c)));

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("chat")]
    [HttpGet("{id:int}")]
    public ActionResult<ChatConversation> Get(int id) =>
        _store.GetConversation(id) is { } c ? c : NotFound();

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("chat")]
    [HttpPost("{id:int}/messages")]
    public ActionResult<ChatConversation> Reply(int id, ChatSendInput input)
    {
        var body = Clean(input.Body);
        if (body.Length == 0) return BadRequest("متن پیام خالی است.");
        return _store.AddAdminMessage(id, "پشتیبانی فونیکس", body) is { } c ? c : NotFound();
    }

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("chat")]
    [HttpPost("{id:int}/read")]
    public IActionResult MarkRead(int id)
    {
        _store.MarkConversationRead(id, byAdmin: true);
        return NoContent();
    }

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("chat")]
    [HttpPost("{id:int}/close")]
    public IActionResult Close(int id) => _store.CloseConversation(id) ? NoContent() : NotFound();

    private static string Clean(string? body)
    {
        var t = (body ?? "").Trim();
        return t.Length > MaxBody ? t[..MaxBody] : t;
    }
}

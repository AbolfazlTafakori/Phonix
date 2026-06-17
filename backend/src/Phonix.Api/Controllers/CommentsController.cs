using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Models;

namespace Phonix.Api.Controllers;

public record CommentInput(int ProductId, string UserName, string Body, int Rating, int? ParentId);
public record ReplyInput(string Body);

[ApiController]
[Route("api/comments")]
public class CommentsController : ControllerBase
{
    private readonly StoreData _store;
    public CommentsController(StoreData store) => _store = store;

    [HttpGet]
    public IEnumerable<Comment> Get([FromQuery] CommentStatus? status, [FromQuery] int? productId) =>
        _store.GetComments(productId, status);

    [HttpPost]
    public ActionResult<Comment> Create(CommentInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Body)) return BadRequest("متن نظر خالی است.");
        var comment = _store.AddComment(new Comment
        {
            ProductId = input.ProductId,
            UserName = string.IsNullOrWhiteSpace(input.UserName) ? "کاربر مهمان" : input.UserName,
            Body = input.Body,
            Rating = Math.Clamp(input.Rating, 0, 5),
            ParentId = input.ParentId,
            Status = CommentStatus.Pending,
        });
        return comment;
    }

    [HttpPost("{id:int}/approve")]
    public IActionResult Approve(int id) => _store.SetCommentStatus(id, CommentStatus.Approved) ? NoContent() : NotFound();

    [HttpPost("{id:int}/reject")]
    public IActionResult Reject(int id) => _store.SetCommentStatus(id, CommentStatus.Rejected) ? NoContent() : NotFound();

    [HttpPost("{id:int}/reply")]
    public ActionResult<Comment> Reply(int id, ReplyInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Body)) return BadRequest("متن پاسخ خالی است.");
        var reply = _store.AddReply(id, input.Body, "پشتیبانی فونیکس");
        return reply is null ? NotFound() : reply;
    }

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id) => _store.DeleteComment(id) ? NoContent() : NotFound();

    [HttpGet("/api/products/{productId:int}/comments")]
    public IEnumerable<Comment> ForProduct(int productId) => _store.GetApprovedForProduct(productId);
}

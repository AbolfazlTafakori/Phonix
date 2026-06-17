namespace Phonix.Api.Models;

public enum CommentStatus
{
    Pending,
    Approved,
    Rejected,
}

public class Comment
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string UserName { get; set; } = "";
    public string Body { get; set; } = "";
    public int Rating { get; set; }
    public CommentStatus Status { get; set; } = CommentStatus.Pending;
    public int? ParentId { get; set; }
    public bool IsAdminReply { get; set; }
    public string Date { get; set; } = "";
}

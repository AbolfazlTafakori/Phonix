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
    // Who wrote it. "My reviews" used to be resolved by matching the DISPLAY NAME instead, which is neither
    // unique nor stable: any customer could rename themselves to someone else's display name and read that
    // person's comments back — including the pending and rejected ones nobody else is meant to see. Nullable
    // because rows written before this field existed have no id; those keep falling back to the name match.
    public int? UserId { get; set; }
    public string UserName { get; set; } = "";
    public string Body { get; set; } = "";
    public int Rating { get; set; }
    public CommentStatus Status { get; set; } = CommentStatus.Pending;
    public int? ParentId { get; set; }
    public bool IsAdminReply { get; set; }
    // Admin-curated: when true, the (approved) comment is eligible for the home-page reviews carousel.
    public bool FeaturedOnHome { get; set; }
    public string Date { get; set; } = "";
}

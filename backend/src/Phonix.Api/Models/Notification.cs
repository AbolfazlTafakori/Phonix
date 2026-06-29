namespace Phonix.Api.Models;

// A bell notification. UserId set = a private message for that one user (order ready, ticket reply,
// wallet charged, or a manual admin message); UserId null = a public broadcast.
// ReadBy holds the ids of users who have already seen it (works for both private and broadcast).
public class Notification
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? Link { get; set; }
    public string CreatedAtUtc { get; set; } = "";
    public List<int> ReadBy { get; set; } = new();

    // For a broadcast (UserId null): the highest user id that existed when it was sent. The broadcast reaches
    // only users whose id is at or below this, so someone who registers afterwards never sees older broadcasts.
    // 0 means "no cutoff" — applies to private notifications and to legacy broadcasts saved before this field.
    public int AudienceMaxUserId { get; set; }
}

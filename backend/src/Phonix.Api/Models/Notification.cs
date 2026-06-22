namespace Phonix.Api.Models;

// A bell notification. UserId set = a private message for that one user (order ready, ticket reply,
// wallet charged, or a manual admin message); UserId null = a public broadcast shown to everyone.
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
}

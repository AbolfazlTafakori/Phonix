namespace Phonix.Api.Models;

public enum ConversationStatus
{
    Open,
    Closed,
}

public class ChatMessage
{
    public int Id { get; set; }
    public bool FromAdmin { get; set; }
    public string AuthorName { get; set; } = "";
    public string Body { get; set; } = "";
    public string CreatedAtUtc { get; set; } = "";
}

// One live-chat thread between a customer and the support team. A customer has at most one open thread at a
// time; it persists in the store so either side can resume it after navigating away or a restart. Read state
// is tracked as "last message id seen" per side, which is cheaper than a flag on every message.
public class ChatConversation
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public ConversationStatus Status { get; set; } = ConversationStatus.Open;
    public string CreatedAtUtc { get; set; } = "";
    public string LastMessageAtUtc { get; set; } = "";
    public int UserReadUpTo { get; set; }
    public int AdminReadUpTo { get; set; }
    public List<ChatMessage> Messages { get; set; } = new();
}

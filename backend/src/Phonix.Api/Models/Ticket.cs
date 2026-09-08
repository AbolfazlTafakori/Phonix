namespace Phonix.Api.Models;

public enum TicketStatus
{
    Open,
    Answered,
    Closed,
}

public enum TicketPriority
{
    Low,
    Medium,
    High,
}

public class TicketMessage
{
    public string Author { get; set; } = "";
    public string Body { get; set; } = "";
    public bool IsAdmin { get; set; }
    public string Date { get; set; } = "";
    // Date above is the Persian day, for display. This is the machine-readable instant the reply was sent —
    // a day string cannot answer "has this been quiet for 24 hours?".
    public DateTime? SentAtUtc { get; set; }
    public string Attachment { get; set; } = ""; // optional public URL of a file attached to this reply
}

public class Ticket
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Department { get; set; } = "";
    public TicketPriority Priority { get; set; } = TicketPriority.Medium;
    public string Attachment { get; set; } = ""; // optional public URL of a user-uploaded supporting file
    public TicketStatus Status { get; set; } = TicketStatus.Open;
    public List<TicketMessage> Messages { get; set; } = new();
    public string Date { get; set; } = "";
    // When the last message landed, from either side. The auto-close sweep measures silence from here.
    // Null on tickets that predate this field; the sweep stamps those rather than treating them as ancient.
    public DateTime? LastMessageAtUtc { get; set; }
    // Set when the sweep closed the ticket, so the panel can tell an automatic close from one support made.
    // Cleared if the customer replies and reopens it.
    public DateTime? AutoClosedAtUtc { get; set; }
}

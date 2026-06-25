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
}

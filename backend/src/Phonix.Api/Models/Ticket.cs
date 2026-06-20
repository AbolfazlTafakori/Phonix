namespace Phonix.Api.Models;

public enum TicketStatus
{
    Open,
    Answered,
    Closed,
}

public class TicketMessage
{
    public string Author { get; set; } = "";
    public string Body { get; set; } = "";
    public bool IsAdmin { get; set; }
    public string Date { get; set; } = "";
}

public class Ticket
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Department { get; set; } = "";
    public TicketStatus Status { get; set; } = TicketStatus.Open;
    public List<TicketMessage> Messages { get; set; } = new();
    public string Date { get; set; } = "";
}

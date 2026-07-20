namespace Phonix.Api.Models;

// One outbound email, as it was actually attempted.
//
// info@ is a send-only address: nothing is delivered back to it, so a reply or a bounce leaves no trace on
// the server. This record is the only place the shop can answer "what did we send this customer, and did it
// go out?" — which matters when a buyer says they never received their account, or a password reset.
//
// The body is deliberately NOT stored. Delivery emails carry live credentials, and keeping a copy would put
// them in a second place with weaker protection than the order itself. Recipient, subject and outcome are
// enough to answer the operational question.
public class SentEmail
{
    public int Id { get; set; }
    public string To { get; set; } = "";
    public string Subject { get; set; } = "";
    public DateTime SentAtUtc { get; set; }
    // False when SMTP refused it, the settings were missing, or the address was empty — with `Error` saying
    // which. A failure is worth more than a success here: it is the one the shop has to act on.
    public bool Success { get; set; }
    public string? Error { get; set; }
}

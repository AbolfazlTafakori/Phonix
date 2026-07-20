namespace Phonix.Api.Models;

// Connection details for the shop's RECEIVING mailbox (deploy/mailbox-setup.sh creates it: support@<domain>,
// Maildir over Dovecot, IMAPS on 993).
//
// Kept separate from EmailSettings on purpose. EmailSettings is the send-only info@ transport the whole app
// depends on for delivery mails and password resets; breaking it stops orders reaching customers. This is a
// second, independent account that only the admin inbox uses, so an operator can point it at a different
// host — or get it wrong — without touching the outbound path.
//
// Replies are sent through this account's own SMTP so the customer sees the address they wrote to as the
// sender, and so the conversation stays on one thread in their client.
public class MailboxSettings
{
    public bool Enabled { get; set; }

    // ── IMAP (reading) ──────────────────────────────────────────────────────────────────────────────
    public string ImapHost { get; set; } = "";
    public int ImapPort { get; set; } = 993;
    public bool ImapUseSsl { get; set; } = true;

    // ── SMTP (replying as this mailbox) ─────────────────────────────────────────────────────────────
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public bool SmtpUseSsl { get; set; } = true;

    // One credential pair: the Dovecot/Postfix setup authenticates both protocols against the same system
    // user, so splitting them would only invite a mismatch.
    public string Username { get; set; } = "";
    // Encrypted at rest with SensitiveField; never leaves the server (see MailboxSettingsDto).
    public string Password { get; set; } = "";

    public string Address { get; set; } = "";   // support@example.com — the From: on replies
    public string DisplayName { get; set; } = "پشتیبانی فونیکس";
}

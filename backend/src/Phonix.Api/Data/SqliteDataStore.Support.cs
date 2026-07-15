using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Data;

// Support: tickets and live chat.
// Partial of SqliteDataStore -- split by domain the same way the JSON StoreData is (StoreOrders.cs etc.).
public sealed partial class SqliteDataStore
{
    // ── Tickets ─────────────────────────────────────────────────────────────────────────────────────────
    public IReadOnlyList<Ticket> GetTickets(TicketStatus? status = null)
    {
        var all = AllJson<Ticket>("Tickets").AsEnumerable();
        if (status is TicketStatus s) all = all.Where(t => t.Status == s);
        return all.OrderByDescending(t => t.Id).ToList();
    }
    public IReadOnlyList<Ticket> GetUserTickets(int userId) =>
        AllJson<Ticket>("Tickets").Where(t => t.UserId == userId).OrderByDescending(t => t.Id).ToList();
    public Ticket? GetTicket(int id) => OneJson<Ticket>("Tickets", id);

    public Ticket CreateTicket(int userId, string userName, string subject, string department, string body,
        TicketPriority priority = TicketPriority.Medium, string attachment = "")
    {
        var t = new Ticket
        {
            UserId = userId, UserName = userName, Subject = subject, Department = department, Priority = priority,
            Attachment = attachment ?? "", Status = TicketStatus.Open, Date = Today(),
        };
        InsertJson("Tickets", t, (x, id) => { x.Id = id; x.Code = $"T-{5800 + id}"; });
        // append the opening message and re-save (Code/Id were just assigned).
        t.Messages.Add(new TicketMessage { Author = userName, Body = body, IsAdmin = false, Date = Today() });
        UpdateJson("Tickets", t.Id, t);
        return t;
    }

    public Ticket CreateTicketForUser(int userId, string userName, string subject, string department, string body,
        string authorName, TicketPriority priority = TicketPriority.Medium, string attachment = "")
    {
        var t = new Ticket
        {
            UserId = userId, UserName = userName, Subject = subject, Department = department, Priority = priority,
            Status = TicketStatus.Answered, Date = Today(),
        };
        InsertJson("Tickets", t, (x, id) => { x.Id = id; x.Code = $"T-{5800 + id}"; });
        t.Messages.Add(new TicketMessage { Author = authorName, Body = body, IsAdmin = true, Date = Today(), Attachment = attachment ?? "" });
        UpdateJson("Tickets", t.Id, t);
        AddNotification(userId, "تیکت جدید از پشتیبانی", $"پشتیبانی فونیکس برای شما تیکت «{subject}» باز کرد.", "/account/tickets");
        return t;
    }

    public Ticket? ReplyTicket(int id, string author, string body, bool isAdmin, string? attachment = null)
    {
        var t = OneJson<Ticket>("Tickets", id);
        if (t is null) return null;
        t.Messages.Add(new TicketMessage { Author = author, Body = body, IsAdmin = isAdmin, Date = Today(), Attachment = attachment ?? "" });
        t.Status = isAdmin ? TicketStatus.Answered : TicketStatus.Open;
        UpdateJson("Tickets", id, t);
        if (isAdmin) AddNotification(t.UserId, "پاسخ تیکت پشتیبانی", $"به تیکت «{t.Subject}» پاسخ داده شد.", "/account/tickets");
        return t;
    }

    public bool SetTicketStatus(int id, TicketStatus status)
    {
        var t = OneJson<Ticket>("Tickets", id);
        if (t is null) return false;
        t.Status = status;
        return UpdateJson("Tickets", id, t);
    }

    // ── Live chat ───────────────────────────────────────────────────────────────────────────────────────
    private static string NowIso() => DateTime.UtcNow.ToString("o");

    public ChatConversation? GetUserConversation(int userId) =>
        AllJson<ChatConversation>("Conversations")
            .Where(c => c.UserId == userId && c.Status == ConversationStatus.Open)
            .OrderByDescending(c => c.LastMessageAtUtc).FirstOrDefault();

    public void CloseUserConversation(int userId) =>
        WriteTx<object?>((conn, tx) =>
        {
            foreach (var row in conn.Query("SELECT Id, DataJson FROM Conversations", transaction: tx).ToList())
            {
                var c = Deserialize<ChatConversation>((string)row.DataJson)!;
                if (c.UserId == userId && c.Status == ConversationStatus.Open)
                {
                    c.Status = ConversationStatus.Closed;
                    conn.Execute("UPDATE Conversations SET DataJson=@d WHERE Id=@id", new { d = Serialize(c), id = (long)row.Id }, tx);
                }
            }
            return null;
        });

    public ChatConversation? GetConversation(int id) => OneJson<ChatConversation>("Conversations", id);
    public IReadOnlyList<ChatConversation> GetConversations() =>
        AllJson<ChatConversation>("Conversations").OrderByDescending(c => c.LastMessageAtUtc).ToList();

    private static void AppendChatMessage(SqliteConnection conn, SqliteTransaction tx, ChatConversation conv, bool fromAdmin, string authorName, string body)
    {
        var msg = new ChatMessage { Id = NextCounter(conn, tx, "chatMessage"), FromAdmin = fromAdmin, AuthorName = authorName, Body = body, CreatedAtUtc = NowIso() };
        conv.Messages.Add(msg);
        conv.LastMessageAtUtc = msg.CreatedAtUtc;
        conv.Status = ConversationStatus.Open;
        if (fromAdmin) conv.AdminReadUpTo = msg.Id; else conv.UserReadUpTo = msg.Id;
    }

    public ChatConversation SendUserMessage(int userId, string userName, string body) =>
        WriteTx((conn, tx) =>
        {
            var rows = conn.Query("SELECT Id, DataJson FROM Conversations", transaction: tx).ToList();
            var mine = rows.Select(r => (Id: (long)r.Id, Conv: Deserialize<ChatConversation>((string)r.DataJson)!))
                .Where(x => x.Conv.UserId == userId).OrderByDescending(x => x.Conv.LastMessageAtUtc).ToList();
            var open = mine.FirstOrDefault(x => x.Conv.Status == ConversationStatus.Open);
            ChatConversation conv;
            long rowId;
            if (open.Conv is not null) { conv = open.Conv; rowId = open.Id; }
            else if (mine.Count > 0) { conv = mine[0].Conv; rowId = mine[0].Id; }
            else
            {
                conv = new ChatConversation { UserId = userId, UserName = userName, CreatedAtUtc = NowIso(), LastMessageAtUtc = NowIso() };
                rowId = conn.ExecuteScalar<long>("INSERT INTO Conversations (DataJson) VALUES (@d); SELECT last_insert_rowid();", new { d = Serialize(conv) }, tx);
                conv.Id = (int)rowId;
            }
            AppendChatMessage(conn, tx, conv, fromAdmin: false, userName, body);
            conn.Execute("UPDATE Conversations SET DataJson=@d WHERE Id=@id", new { d = Serialize(conv), id = rowId }, tx);
            return conv;
        });

    public ChatConversation? AddAdminMessage(int conversationId, string authorName, string body)
    {
        var result = WriteTx<(ChatConversation? Conv, int UserId)>((conn, tx) =>
        {
            var cj = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Conversations WHERE Id=@id", new { id = conversationId }, tx);
            if (cj is null) return (null, 0);
            var conv = Deserialize<ChatConversation>(cj)!;
            AppendChatMessage(conn, tx, conv, fromAdmin: true, authorName, body);
            conn.Execute("UPDATE Conversations SET DataJson=@d WHERE Id=@id", new { d = Serialize(conv), id = conversationId }, tx);
            return (conv, conv.UserId);
        });
        if (result.Conv is null) return null;
        AddNotification(result.UserId, "پاسخ پشتیبانی", "پشتیبانی به گفتگوی زنده‌ی شما پاسخ داد.", null);
        return result.Conv;
    }

    public void MarkConversationRead(int conversationId, bool byAdmin) =>
        WriteTx<object?>((conn, tx) =>
        {
            var cj = conn.QueryFirstOrDefault<string>("SELECT DataJson FROM Conversations WHERE Id=@id", new { id = conversationId }, tx);
            if (cj is null) return null;
            var conv = Deserialize<ChatConversation>(cj)!;
            var lastId = conv.Messages.Count == 0 ? 0 : conv.Messages.Max(m => m.Id);
            if (byAdmin) conv.AdminReadUpTo = lastId; else conv.UserReadUpTo = lastId;
            conn.Execute("UPDATE Conversations SET DataJson=@d WHERE Id=@id", new { d = Serialize(conv), id = conversationId }, tx);
            return null;
        });

    public bool CloseConversation(int id)
    {
        var conv = OneJson<ChatConversation>("Conversations", id);
        if (conv is null) return false;
        conv.Status = ConversationStatus.Closed;
        return UpdateJson("Conversations", id, conv);
    }

    public int CountUnreadForUser(int userId)
    {
        var conv = GetUserConversation(userId);
        return conv is null ? 0 : conv.Messages.Count(m => m.FromAdmin && m.Id > conv.UserReadUpTo);
    }
    public int UnreadChatsForAdmin() =>
        AllJson<ChatConversation>("Conversations").Count(c => c.Messages.Any(m => !m.FromAdmin && m.Id > c.AdminReadUpTo));
    public int UnreadMessagesForAdmin(ChatConversation conv) =>
        conv.Messages.Count(m => !m.FromAdmin && m.Id > conv.AdminReadUpTo);
}

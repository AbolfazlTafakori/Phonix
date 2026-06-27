using Phonix.Api.Models;

namespace Phonix.Api.Data;

public partial class StoreData
{
    private readonly List<ChatConversation> _conversations = new();
    private int _conversationSeq;
    private int _chatMessageSeq;

    private static string NowIso() => DateTime.UtcNow.ToString("o");

    // A DETACHED deep copy of a conversation: the outer object AND the Messages list are freshly allocated.
    // Everything that reads a conversation after the lock is released (MVC serialization on the controller,
    // the persistence snapshot) must operate on one of these — never on the live thread — so it can never
    // race a concurrent AppendMessage that mutates conv.Messages under _gate. ChatMessage instances are
    // immutable once created, so copying the list (not each element) is sufficient.
    private static ChatConversation Clone(ChatConversation c) => new()
    {
        Id = c.Id,
        UserId = c.UserId,
        UserName = c.UserName,
        Status = c.Status,
        CreatedAtUtc = c.CreatedAtUtc,
        LastMessageAtUtc = c.LastMessageAtUtc,
        UserReadUpTo = c.UserReadUpTo,
        AdminReadUpTo = c.AdminReadUpTo,
        Messages = new List<ChatMessage>(c.Messages),
    };

    // The customer's current OPEN thread, or null if they have none. Closed threads (archived after the
    // customer resets the chat on a new browser session, or closed by support) are intentionally hidden from
    // the customer so a fresh browser session starts with an empty chat — while staff keep the full history
    // via GetConversations. Reading never creates a thread. Returns a detached copy, safe to serialize.
    public ChatConversation? GetUserConversation(int userId)
    {
        lock (_gate)
        {
            var conv = _conversations
                .Where(c => c.UserId == userId && c.Status == ConversationStatus.Open)
                .OrderByDescending(c => c.LastMessageAtUtc)
                .FirstOrDefault();
            return conv is null ? null : Clone(conv);
        }
    }

    // Archives the customer's open thread (if any) without deleting it: the customer's widget goes back to an
    // empty state, but support still sees the conversation in the panel. Called when a new browser session
    // begins (see the LiveChat widget) so chat history resets per browser session for the customer only.
    public void CloseUserConversation(int userId)
    {
        bool changed = false;
        lock (_gate)
        {
            foreach (var conv in _conversations.Where(c => c.UserId == userId && c.Status == ConversationStatus.Open))
            {
                conv.Status = ConversationStatus.Closed;
                changed = true;
            }
        }
        if (changed) MarkDirty();
    }

    public ChatConversation? GetConversation(int id)
    {
        lock (_gate)
        {
            var conv = _conversations.FirstOrDefault(c => c.Id == id);
            return conv is null ? null : Clone(conv);
        }
    }

    // Detached copies, newest first. The staff list controller can safely read each copy's Messages
    // (preview, unread count) outside the lock because every element is independent of the live store.
    public IReadOnlyList<ChatConversation> GetConversations()
    {
        lock (_gate)
            return _conversations.OrderByDescending(c => c.LastMessageAtUtc).Select(Clone).ToList();
    }

    // Posts a customer message, creating (or reopening) their thread as needed, and returns a detached
    // snapshot of the live thread.
    public ChatConversation SendUserMessage(int userId, string userName, string body)
    {
        ChatConversation snapshot;
        lock (_gate)
        {
            var conv = _conversations.FirstOrDefault(c => c.UserId == userId && c.Status == ConversationStatus.Open)
                       ?? _conversations.Where(c => c.UserId == userId).OrderByDescending(c => c.LastMessageAtUtc).FirstOrDefault();
            if (conv is null)
            {
                conv = new ChatConversation
                {
                    Id = ++_conversationSeq,
                    UserId = userId,
                    UserName = userName,
                    CreatedAtUtc = NowIso(),
                    LastMessageAtUtc = NowIso(),
                };
                _conversations.Add(conv);
            }
            AppendMessage(conv, fromAdmin: false, userName, body);
            snapshot = Clone(conv);
        }
        // Chat is non-financial and high-volume: flag the change for the next periodic flush instead of
        // rewriting the entire store.json on every message. Durability is provided by the 10s flush and
        // the unconditional shutdown save.
        MarkDirty();
        return snapshot;
    }

    // Posts a support reply to an existing thread. Returns null if the thread is gone, otherwise a detached snapshot.
    public ChatConversation? AddAdminMessage(int conversationId, string authorName, string body)
    {
        ChatConversation snapshot;
        int userId;
        lock (_gate)
        {
            var conv = _conversations.FirstOrDefault(c => c.Id == conversationId);
            if (conv is null) return null;
            AppendMessage(conv, fromAdmin: true, authorName, body);
            userId = conv.UserId;
            snapshot = Clone(conv);
        }
        // Tell the customer support replied, in case their widget is collapsed on another page. Done before
        // MarkDirty so the reply AND the notification are both captured by the next flush.
        AddNotification(userId, "پاسخ پشتیبانی", "پشتیبانی به گفتگوی زنده‌ی شما پاسخ داد.", null);
        MarkDirty();
        return snapshot;
    }

    // Caller holds _gate. Adds a message, bumps the thread, reopens it on a new message, and marks the
    // author's own side as caught up.
    private void AppendMessage(ChatConversation conv, bool fromAdmin, string authorName, string body)
    {
        var msg = new ChatMessage
        {
            Id = ++_chatMessageSeq,
            FromAdmin = fromAdmin,
            AuthorName = authorName,
            Body = body,
            CreatedAtUtc = NowIso(),
        };
        conv.Messages.Add(msg);
        conv.LastMessageAtUtc = msg.CreatedAtUtc;
        conv.Status = ConversationStatus.Open;
        if (fromAdmin) conv.AdminReadUpTo = msg.Id;
        else conv.UserReadUpTo = msg.Id;
    }

    public void MarkConversationRead(int conversationId, bool byAdmin)
    {
        bool changed;
        lock (_gate)
        {
            var conv = _conversations.FirstOrDefault(c => c.Id == conversationId);
            if (conv is null) { changed = false; }
            else
            {
                var lastId = conv.Messages.Count == 0 ? 0 : conv.Messages.Max(m => m.Id);
                if (byAdmin) conv.AdminReadUpTo = lastId;
                else conv.UserReadUpTo = lastId;
                changed = true;
            }
        }
        if (changed) MarkDirty();
    }

    public bool CloseConversation(int id)
    {
        bool ok;
        lock (_gate)
        {
            var conv = _conversations.FirstOrDefault(c => c.Id == id);
            if (conv is null) { ok = false; }
            else { conv.Status = ConversationStatus.Closed; ok = true; }
        }
        if (ok) MarkDirty();
        return ok;
    }

    // Admin messages the customer hasn't seen yet (drives the floating widget badge). Counts only the open
    // thread, so a reset/closed conversation never leaves a stale badge on the customer's bubble.
    public int CountUnreadForUser(int userId)
    {
        lock (_gate)
        {
            var conv = _conversations
                .Where(c => c.UserId == userId && c.Status == ConversationStatus.Open)
                .OrderByDescending(c => c.LastMessageAtUtc)
                .FirstOrDefault();
            return conv is null ? 0 : conv.Messages.Count(m => m.FromAdmin && m.Id > conv.UserReadUpTo);
        }
    }

    // Threads with at least one customer message the support team hasn't read (drives the sidebar badge).
    public int UnreadChatsForAdmin()
    {
        lock (_gate)
            return _conversations.Count(c => c.Messages.Any(m => !m.FromAdmin && m.Id > c.AdminReadUpTo));
    }

    // Unread customer messages for a SINGLE thread. The caller must pass a detached copy (from
    // GetConversations) so reading conv.Messages outside _gate cannot race a concurrent AppendMessage.
    public int UnreadMessagesForAdmin(ChatConversation conv) =>
        conv.Messages.Count(m => !m.FromAdmin && m.Id > conv.AdminReadUpTo);
}

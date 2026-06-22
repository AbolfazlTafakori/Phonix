using Phonix.Api.Models;

namespace Phonix.Api.Data;

public partial class StoreData
{
    private readonly List<Notification> _notifications = new();
    private int _notificationSeq;

    // Creates a notification. userId null = public broadcast to all users. Callers may already hold _gate
    // (the lock is reentrant), so the auto-notifications fired from order/ticket/transaction flows are safe.
    public Notification AddNotification(int? userId, string title, string body, string? link = null)
    {
        lock (_gate)
        {
            var n = new Notification
            {
                Id = ++_notificationSeq,
                UserId = userId,
                Title = title,
                Body = body,
                Link = link,
                CreatedAtUtc = DateTime.UtcNow.ToString("o"),
            };
            _notifications.Add(n);
            return n;
        }
    }

    // A user's feed = their own private notifications plus every public broadcast, newest first.
    public IReadOnlyList<Notification> GetUserNotifications(int userId)
    {
        lock (_gate)
            return _notifications
                .Where(n => n.UserId == userId || n.UserId is null)
                .OrderByDescending(n => n.CreatedAtUtc)
                .ToList();
    }

    public IReadOnlyList<Notification> GetAllNotifications()
    {
        lock (_gate) return _notifications.OrderByDescending(n => n.CreatedAtUtc).ToList();
    }

    public int CountUnread(int userId)
    {
        lock (_gate)
            return _notifications.Count(n => (n.UserId == userId || n.UserId is null) && !n.ReadBy.Contains(userId));
    }

    public void MarkNotificationsRead(int userId)
    {
        lock (_gate)
            foreach (var n in _notifications.Where(n => n.UserId == userId || n.UserId is null))
                if (!n.ReadBy.Contains(userId)) n.ReadBy.Add(userId);
    }

    public bool DeleteNotification(int id)
    {
        lock (_gate)
        {
            var n = _notifications.FirstOrDefault(x => x.Id == id);
            if (n is null) return false;
            _notifications.Remove(n);
            return true;
        }
    }
}

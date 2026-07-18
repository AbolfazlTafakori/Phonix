using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Data;

// Content and engagement: comments, hero/home/showcase/blog, notifications, favorites.
// Partial of SqliteDataStore -- split by domain the same way the JSON StoreData is (StoreOrders.cs etc.).
public sealed partial class SqliteDataStore
{
    // ── Comments ────────────────────────────────────────────────────────────────────────────────────────
    public IReadOnlyList<Comment> GetComments(int? productId = null, CommentStatus? status = null)
    {
        var all = AllJson<Comment>("Comments").AsEnumerable();
        if (productId is int pid) all = all.Where(c => c.ProductId == pid);
        if (status is CommentStatus s) all = all.Where(c => c.Status == s);
        return all.OrderByDescending(c => c.Id).ToList();
    }
    public IReadOnlyList<Comment> GetApprovedForProduct(int productId) =>
        AllJson<Comment>("Comments").Where(c => c.ProductId == productId && c.Status == CommentStatus.Approved).OrderBy(c => c.Id).ToList();

    public Comment AddComment(Comment c)
    {
        if (string.IsNullOrWhiteSpace(c.Date)) c.Date = Today();
        InsertJson("Comments", c, (x, id) => x.Id = id);
        return c;
    }
    public bool SetCommentStatus(int id, CommentStatus status)
    {
        var c = OneJson<Comment>("Comments", id);
        if (c is null) return false;
        c.Status = status;
        return UpdateJson("Comments", id, c);
    }
    public bool SetCommentFeaturedOnHome(int id, bool on)
    {
        var c = OneJson<Comment>("Comments", id);
        if (c is null) return false;
        c.FeaturedOnHome = on;
        return UpdateJson("Comments", id, c);
    }
    public IReadOnlyList<Comment> GetHomeTestimonials() =>
        AllJson<Comment>("Comments")
            .Where(c => c.FeaturedOnHome && c.Status == CommentStatus.Approved && c.ParentId == null)
            .OrderByDescending(c => c.Id)
            .ToList();
    public Comment? AddReply(int parentId, string body, string author)
    {
        var parent = OneJson<Comment>("Comments", parentId);
        if (parent is null) return null;
        var reply = new Comment
        {
            ProductId = parent.ProductId, UserName = author, Body = body, Rating = 0,
            Status = CommentStatus.Approved, ParentId = parentId, IsAdminReply = true, Date = Today(),
        };
        InsertJson("Comments", reply, (x, id) => x.Id = id);
        return reply;
    }
    public bool DeleteComment(int id) =>
        WriteTx((conn, tx) =>
        {
            var ids = conn.Query("SELECT Id, DataJson FROM Comments", transaction: tx)
                .Where(r => (long)r.Id == id || (Deserialize<Comment>((string)r.DataJson)!.ParentId == id))
                .Select(r => (long)r.Id).ToList();
            if (ids.Count == 0) return false;
            conn.Execute("DELETE FROM Comments WHERE Id = @id", ids.Select(x => new { id = x }), tx);
            foreach (var deletedId in ids) AppendOutbox(conn, tx, "Comments", deletedId, SyncOp.Delete, null);
            return true;
        });


    // ── Content: hero / home categories / showcase / blog ───────────────────────────────────────────────
    private static List<T> Ordered<T>(IEnumerable<T> items) where T : IContentItem =>
        items.OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToList();

    public IReadOnlyList<HeroSlide> GetHeroSlides() => Ordered(AllJson<HeroSlide>("HeroSlides"));
    public HeroSlide? GetHeroSlide(int id) => OneJson<HeroSlide>("HeroSlides", id);
    public HeroSlide AddHeroSlide(HeroSlide s) { InsertJson("HeroSlides", s, (x, id) => x.Id = id); return s; }
    public bool UpdateHeroSlide(HeroSlide s) { if (OneJson<HeroSlide>("HeroSlides", s.Id) is null) return false; return UpdateJson("HeroSlides", s.Id, s); }
    public bool DeleteHeroSlide(int id) => DeleteRow("HeroSlides", id);

    public IReadOnlyList<HomeCategory> GetHomeCategories() => Ordered(AllJson<HomeCategory>("HomeCategories"));
    public HomeCategory? GetHomeCategory(int id) => OneJson<HomeCategory>("HomeCategories", id);
    public HomeCategory AddHomeCategory(HomeCategory c) { InsertJson("HomeCategories", c, (x, id) => x.Id = id); return c; }
    public bool UpdateHomeCategory(HomeCategory c) { if (OneJson<HomeCategory>("HomeCategories", c.Id) is null) return false; return UpdateJson("HomeCategories", c.Id, c); }
    public bool DeleteHomeCategory(int id) => DeleteRow("HomeCategories", id);

    public IReadOnlyList<Showcase> GetShowcase() => Ordered(AllJson<Showcase>("Showcase"));
    public Showcase? GetShowcaseItem(int id) => OneJson<Showcase>("Showcase", id);
    public Showcase AddShowcase(Showcase s) { InsertJson("Showcase", s, (x, id) => x.Id = id); return s; }
    public bool UpdateShowcase(Showcase s) { if (OneJson<Showcase>("Showcase", s.Id) is null) return false; return UpdateJson("Showcase", s.Id, s); }
    public bool DeleteShowcase(int id) => DeleteRow("Showcase", id);

    public IReadOnlyList<BlogPost> GetBlogPosts() => Ordered(AllJson<BlogPost>("BlogPosts"));
    public BlogPost? GetBlogPost(int id) => OneJson<BlogPost>("BlogPosts", id);
    public BlogPost AddBlogPost(BlogPost p) { InsertJson("BlogPosts", p, (x, id) => x.Id = id); return p; }
    public bool UpdateBlogPost(BlogPost p) { if (OneJson<BlogPost>("BlogPosts", p.Id) is null) return false; return UpdateJson("BlogPosts", p.Id, p); }
    public bool DeleteBlogPost(int id) => DeleteRow("BlogPosts", id);


    // ── Notifications ───────────────────────────────────────────────────────────────────────────────────
    public Notification AddNotification(int? userId, string title, string body, string? link = null) =>
        WriteTx((conn, tx) =>
        {
            var n = new Notification { UserId = userId, Title = title, Body = body, Link = link, CreatedAtUtc = DateTime.UtcNow.ToString("o") };
            // A broadcast is frozen to the users who exist right now, so newcomers never see older broadcasts.
            if (userId is null) n.AudienceMaxUserId = conn.ExecuteScalar<int?>("SELECT MAX(Id) FROM Users", transaction: tx) ?? 0;
            var nid = (int)conn.ExecuteScalar<long>("INSERT INTO Notifications (UserId, DataJson) VALUES (@UserId,@DataJson); SELECT last_insert_rowid();",
                new { UserId = userId, DataJson = Serialize(n) }, tx);
            n.Id = nid;
            var json = Serialize(n);
            conn.Execute("UPDATE Notifications SET DataJson=@d WHERE Id=@id", new { d = json, id = nid }, tx);
            AppendOutbox(conn, tx, "Notifications", nid, SyncOp.Upsert, json);
            return n;
        });

    // A broadcast (UserId null) reaches a user only if it was sent while they already had an account; a private
    // notification always reaches its owner. AudienceMaxUserId == 0 = legacy/unbounded (shown to everyone).
    private static bool IsVisibleTo(Notification n, int userId) =>
        n.UserId == userId || (n.UserId is null && (n.AudienceMaxUserId == 0 || userId <= n.AudienceMaxUserId));

    public IReadOnlyList<Notification> GetUserNotifications(int userId)
    {
        using var conn = OpenConnection();
        return conn.Query<string>("SELECT DataJson FROM Notifications WHERE UserId=@u OR UserId IS NULL", new { u = userId })
            .Select(j => Deserialize<Notification>(j)!).Where(n => IsVisibleTo(n, userId)).OrderByDescending(n => n.CreatedAtUtc).ToList();
    }
    public IReadOnlyList<Notification> GetAllNotifications() =>
        AllJson<Notification>("Notifications").OrderByDescending(n => n.CreatedAtUtc).ToList();
    public int CountUnread(int userId)
    {
        using var conn = OpenConnection();
        return conn.Query<string>("SELECT DataJson FROM Notifications WHERE UserId=@u OR UserId IS NULL", new { u = userId })
            .Select(j => Deserialize<Notification>(j)!).Count(n => IsVisibleTo(n, userId) && !n.ReadBy.Contains(userId));
    }
    public void MarkNotificationsRead(int userId) =>
        WriteTx<object?>((conn, tx) =>
        {
            foreach (var row in conn.Query("SELECT Id, DataJson FROM Notifications WHERE UserId=@u OR UserId IS NULL", new { u = userId }, tx).ToList())
            {
                var n = Deserialize<Notification>((string)row.DataJson)!;
                if (IsVisibleTo(n, userId) && !n.ReadBy.Contains(userId))
                {
                    n.ReadBy.Add(userId);
                    var json = Serialize(n);
                    conn.Execute("UPDATE Notifications SET DataJson=@d WHERE Id=@id", new { d = json, id = (long)row.Id }, tx);
                    AppendOutbox(conn, tx, "Notifications", (long)row.Id, SyncOp.Upsert, json);
                }
            }
            return null;
        });
    public bool DeleteNotification(int id) => DeleteRow("Notifications", id);

    // ── Favorites (singleton dict) ──────────────────────────────────────────────────────────────────────
    public IReadOnlyList<int> GetFavorites(int userId)
    {
        var fav = GetSingleton<Dictionary<int, List<int>>>(FavoritesKey);
        return fav.TryGetValue(userId, out var list) ? list.ToList() : new List<int>();
    }
    public bool ToggleFavorite(int userId, int productId) =>
        WriteTx((conn, tx) =>
        {
            var fav = ReadSingleton<Dictionary<int, List<int>>>(conn, tx, FavoritesKey);
            if (!fav.TryGetValue(userId, out var list)) { list = new List<int>(); fav[userId] = list; }
            bool added;
            if (list.Remove(productId)) added = false;
            else { list.Add(productId); added = true; }
            WriteSingleton(conn, tx, FavoritesKey, fav);
            return added;
        });
}

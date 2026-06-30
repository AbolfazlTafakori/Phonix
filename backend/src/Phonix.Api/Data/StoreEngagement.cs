using System.Globalization;
using Phonix.Api.Models;

namespace Phonix.Api.Data;

public partial class StoreData
{
    private readonly List<Comment> _comments = new();
    private int _commentSeq;

    public IReadOnlyList<Comment> GetComments(int? productId = null, CommentStatus? status = null)
    {
        lock (_gate)
        {
            IEnumerable<Comment> q = _comments;
            if (productId is int pid) q = q.Where(c => c.ProductId == pid);
            if (status is CommentStatus s) q = q.Where(c => c.Status == s);
            return q.OrderByDescending(c => c.Id).ToList();
        }
    }

    public IReadOnlyList<Comment> GetApprovedForProduct(int productId)
    {
        lock (_gate)
        {
            return _comments
                .Where(c => c.ProductId == productId && c.Status == CommentStatus.Approved)
                .OrderBy(c => c.Id)
                .ToList();
        }
    }

    public Comment AddComment(Comment c)
    {
        lock (_gate)
        {
            c.Id = ++_commentSeq;
            if (string.IsNullOrWhiteSpace(c.Date)) c.Date = Today();
            _comments.Add(c);
            return c;
        }
    }

    public bool SetCommentStatus(int id, CommentStatus status)
    {
        lock (_gate)
        {
            var e = _comments.FirstOrDefault(c => c.Id == id);
            if (e is null) return false;
            e.Status = status;
            return true;
        }
    }

    public bool SetCommentFeaturedOnHome(int id, bool on)
    {
        lock (_gate)
        {
            var e = _comments.FirstOrDefault(c => c.Id == id);
            if (e is null) return false;
            e.FeaturedOnHome = on;
            return true;
        }
    }

    public IReadOnlyList<Comment> GetHomeTestimonials()
    {
        lock (_gate)
        {
            return _comments
                .Where(c => c.FeaturedOnHome && c.Status == CommentStatus.Approved && c.ParentId == null)
                .OrderByDescending(c => c.Id)
                .ToList();
        }
    }

    public Comment? AddReply(int parentId, string body, string author)
    {
        lock (_gate)
        {
            var parent = _comments.FirstOrDefault(c => c.Id == parentId);
            if (parent is null) return null;
            var reply = new Comment
            {
                Id = ++_commentSeq,
                ProductId = parent.ProductId,
                UserName = author,
                Body = body,
                Rating = 0,
                Status = CommentStatus.Approved,
                ParentId = parentId,
                IsAdminReply = true,
                Date = Today(),
            };
            _comments.Add(reply);
            return reply;
        }
    }

    public bool DeleteComment(int id)
    {
        lock (_gate)
        {
            var e = _comments.FirstOrDefault(c => c.Id == id);
            if (e is null) return false;
            _comments.RemoveAll(c => c.Id == id || c.ParentId == id);
            return true;
        }
    }

    private static string Today()
    {
        var pc = new PersianCalendar();
        var now = DateTime.Now;
        var s = $"{pc.GetYear(now):0000}/{pc.GetMonth(now):00}/{pc.GetDayOfMonth(now):00}";
        return new string(s.Select(ch => char.IsDigit(ch) ? (char)('۰' + (ch - '0')) : ch).ToArray());
    }

    private void SeedEngagement()
    {
        var c1 = AddComment(new Comment { ProductId = 1, UserName = "علی محمدی", Body = "اکانت سالم بود و خیلی سریع تحویل دادن. ممنون!", Rating = 5, Status = CommentStatus.Approved, Date = "۱۴۰۳/۰۳/۱۸" });
        AddReply(c1.Id, "ممنون از خرید شما 🌹 خوشحالیم که راضی بودید.", "پشتیبانی فونیکس");
        AddComment(new Comment { ProductId = 1, UserName = "زهرا کریمی", Body = "کیفیت خوب بود ولی پشتیبانی کمی دیر جواب داد.", Rating = 4, Status = CommentStatus.Approved, Date = "۱۴۰۳/۰۳/۱۵" });
        AddComment(new Comment { ProductId = 1, UserName = "کاربر مهمان", Body = "قبل از خرید سوال داشتم، آیا روی چند دستگاه کار می‌کند؟", Rating = 0, Status = CommentStatus.Pending, Date = "۱۴۰۳/۰۳/۲۲" });
        AddComment(new Comment { ProductId = 2, UserName = "محمد رضایی", Body = "اسپاتیفای عالیه، پیشنهاد می‌کنم.", Rating = 5, Status = CommentStatus.Pending, Date = "۱۴۰۳/۰۳/۲۱" });
    }
}

namespace Phonix.Api.Models;

// How-to guides the buyer of a product gets after paying for it: step-by-step text with pictures, plus videos.
// One tutorial can serve several products (the same app setup for two plans of one service), and a product can
// have several tutorials (install, first login, troubleshooting) — so the link is a list of product ids here,
// not a field on the product.
public class TutorialLibrary
{
    public List<Tutorial> Items { get; set; } = new();
    public int NextId { get; set; } = 1;
}

public class Tutorial
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    // Markdown, rendered like an article; its pictures are ordinary public uploads.
    public string Body { get; set; } = "";
    public List<int> ProductIds { get; set; } = new();
    public List<TutorialVideo> Videos { get; set; } = new();
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

// A video file uploaded for a tutorial. Kept in its own protected folder and streamed only to staff and to
// customers who bought one of the tutorial's products.
public class TutorialVideo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public long Size { get; set; }
}

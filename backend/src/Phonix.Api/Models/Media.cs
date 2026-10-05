namespace Phonix.Api.Models;

// Images staff upload to the panel's image library, to use anywhere on the site (articles, banners, pages)
// without shipping them inside a release. The file itself is an ordinary public upload (served from
// /api/upload/{Id}); this record carries what the file can't — a readable name, who uploaded it, when.
//
// Being listed here is also what keeps an image that nothing uses YET from being treated as an orphan: the
// orphan sweep keeps any file whose id still appears in the store, and this list is part of the store.
public class MediaLibrary
{
    public List<MediaItem> Items { get; set; } = new();
}

public class MediaItem
{
    public string Id { get; set; } = "";   // the storage id, e.g. 7__<32 hex>.webp
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public string UploadedBy { get; set; } = "";
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
}

using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;
using Phonix.Api.Services;

namespace Phonix.Api.Controllers;

public record MediaItemDto(string Id, string Url, string Name, long Size, string UploadedBy, DateTime UploadedAtUtc,
    bool InLibrary, bool InUse);

// The panel's image library: every picture staff have uploaded, in one place, with a link to copy — so an image
// for an article, a banner or a page is uploaded here instead of being committed and deployed.
//
// What it lists: images uploaded through the library, plus every other public image a STAFF account uploaded
// (product photos, banners, article images from before the library existed). Customers' profile pictures
// share the same folder and are deliberately left out.
[ApiController]
[Route("api/media")]
[Authorize(Roles = AuthExtensions.StaffRoles)]
[AdminPermission("media")]
public partial class MediaController : ControllerBase
{
    private readonly IDataStore _store;
    private readonly IFileStorageService _files;

    public MediaController(IDataStore store, IFileStorageService files)
    {
        _store = store;
        _files = files;
    }

    [GeneratedRegex(@"\d{1,9}__[0-9a-f]{32}\.(?:jpg|jpeg|png|webp)")]
    private static partial Regex StoredIdPattern();

    // How many times each image id is mentioned anywhere in the store — one pass over the snapshot, however
    // many images there are. The library's own entry for an image is one of those mentions.
    private Dictionary<string, int> ReferenceCounts()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match m in StoredIdPattern().Matches(_store.SerializeSnapshot()))
            counts[m.Value] = counts.GetValueOrDefault(m.Value) + 1;
        return counts;
    }

    [HttpGet]
    public IEnumerable<MediaItemDto> List()
    {
        var library = _store.GetMediaLibrary().ToDictionary(i => i.Id, StringComparer.Ordinal);
        var staff = _store.GetUsers().Where(u => u.Role != UserRole.Customer).ToDictionary(u => u.Id);
        var refs = ReferenceCounts();

        return _files.ListPublicImages()
            .Select(f =>
            {
                library.TryGetValue(f.Id, out var item);
                var owner = _files.OwnerOf(f.Id);
                AppUser? uploader = owner is int o && staff.TryGetValue(o, out var u) ? u : null;
                if (item is null && uploader is null) return null; // a customer's avatar, not site imagery
                var uses = refs.GetValueOrDefault(f.Id) - (item is null ? 0 : 1);
                return new MediaItemDto(
                    f.Id, PublicUrl(f.Id), item?.Name ?? "", item?.Size ?? f.Size,
                    item?.UploadedBy ?? uploader?.Username ?? "", item?.UploadedAtUtc ?? f.LastWriteUtc,
                    item is not null, uses > 0);
            })
            .OfType<MediaItemDto>()
            .OrderByDescending(m => m.UploadedAtUtc)
            .ToList();
    }

    [HttpPost]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<ActionResult<MediaItemDto>> Upload(IFormFile? file, CancellationToken ct)
    {
        if (this.CurrentUserId() is not int userId) return Unauthorized();
        var saved = await _files.SavePublicImageAsync(userId, file, ct);
        if (saved.Id is null) return BadRequest(saved.Error);

        // The stored size, not the upload's: the image was re-encoded on the way in.
        long size = 0;
        if (_files.Open("avatars", saved.Id) is { } stored)
            using (stored.Content) size = stored.Content.Length;

        var name = Path.GetFileNameWithoutExtension(file!.FileName ?? "").Trim();
        if (name.Length > 120) name = name[..120];
        var item = new MediaItem
        {
            Id = saved.Id, Name = name.Length > 0 ? name : "تصویر", Size = size,
            UploadedBy = User.Identity?.Name ?? "", UploadedAtUtc = DateTime.UtcNow,
        };
        _store.AddMediaItem(item);
        return Ok(new MediaItemDto(item.Id, PublicUrl(item.Id), item.Name, item.Size, item.UploadedBy, item.UploadedAtUtc, true, false));
    }

    // Deletes the file — unless something on the site still shows it. A picture in use (an article, a product,
    // a banner) is refused with a reason instead of leaving a broken image behind.
    [HttpDelete("{id}")]
    public IActionResult Delete(string id)
    {
        if (_files.OwnerOf(id) is not int owner) return BadRequest("شناسه تصویر نامعتبر است.");
        var inLibrary = _store.GetMediaLibrary().Any(i => i.Id == id);
        var byStaff = _store.GetUser(owner) is { Role: not UserRole.Customer };
        if (!inLibrary && !byStaff) return NotFound();

        var removed = _store.RemoveMediaItem(id);
        var snapshot = _store.SerializeSnapshot();
        if (snapshot.Contains(id, StringComparison.Ordinal))
        {
            if (removed is not null) _store.AddMediaItem(removed);
            return Conflict("این تصویر در سایت استفاده شده است (مثلاً در یک مقاله، محصول یا بنر). ابتدا آن را از آن‌جا بردارید، بعد حذف کنید.");
        }
        _files.DeletePublicImageIfUnreferenced(id, snapshot);
        return NoContent();
    }

    private static string PublicUrl(string id) => $"/api/upload/{id}";
}

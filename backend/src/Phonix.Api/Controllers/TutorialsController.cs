using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;
using Phonix.Api.Services;

namespace Phonix.Api.Controllers;

public record TutorialVideoInput(string Id, string Name);
public record TutorialInput(string Title, string? Body, List<int>? ProductIds, List<TutorialVideoInput>? Videos, int SortOrder, bool IsActive);
public record TutorialVideoDto(string Id, string Name, long Size, string Url);
public record TutorialDto(int Id, string Title, string Body, List<int> ProductIds, List<TutorialVideoDto> Videos,
    int SortOrder, bool IsActive, DateTime UpdatedAtUtc);

// Product tutorials: written and linked in the panel, read by the people who bought the product — in their
// orders, once the payment has been confirmed. Neither the text nor the videos are public.
[ApiController]
[Route("api/tutorials")]
[Authorize]
public class TutorialsController : ControllerBase
{
    private const int MaxTitle = 150;
    private const int MaxBody = 50_000;
    private const long VideoRequestLimit = LocalFileStorageService.MaxVideoBytes + 1024 * 1024;

    private readonly IDataStore _store;
    private readonly IFileStorageService _files;

    public TutorialsController(IDataStore store, IFileStorageService files)
    {
        _store = store;
        _files = files;
    }

    private static TutorialDto ToDto(Tutorial t) =>
        new(t.Id, t.Title, t.Body, t.ProductIds,
            t.Videos.Select(v => new TutorialVideoDto(v.Id, v.Name, v.Size, $"/api/tutorials/videos/{v.Id}")).ToList(),
            t.SortOrder, t.IsActive, t.UpdatedAtUtc);

    // ── Panel ──────────────────────────────────────────────────────────────────────────────────────────

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("tutorials")]
    [HttpGet]
    public IEnumerable<TutorialDto> All() => _store.GetTutorials().Select(ToDto);

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("tutorials")]
    [HttpPost]
    public ActionResult<TutorialDto> Create(TutorialInput input) => Save(0, input);

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("tutorials")]
    [HttpPut("{id:int}")]
    public ActionResult<TutorialDto> Update(int id, TutorialInput input) => Save(id, input);

    private ActionResult<TutorialDto> Save(int id, TutorialInput input)
    {
        var title = (input.Title ?? "").Trim();
        if (title.Length == 0) return BadRequest("عنوان آموزش الزامی است.");
        if (title.Length > MaxTitle) title = title[..MaxTitle];
        var body = (input.Body ?? "").Trim();
        if (body.Length > MaxBody) return BadRequest("متن آموزش بیش از حد طولانی است.");

        // Only videos that really are on disk; the size is read from the file, not taken from the client.
        var videos = new List<TutorialVideo>();
        foreach (var v in input.Videos ?? new())
        {
            if (videos.Any(x => x.Id == v.Id) || _files.OpenVideo(v.Id) is not { } file) continue;
            long size;
            using (file.Content) size = file.Content.Length;
            var name = (v.Name ?? "").Trim();
            videos.Add(new TutorialVideo { Id = v.Id, Name = name.Length > 120 ? name[..120] : name, Size = size });
        }

        var result = _store.SaveTutorial(new Tutorial
        {
            Id = id,
            Title = title,
            Body = body,
            ProductIds = (input.ProductIds ?? new()).Where(p => p > 0).Distinct().ToList(),
            Videos = videos,
            SortOrder = input.SortOrder,
            IsActive = input.IsActive,
        });
        if (result is not { } saved) return NotFound();
        foreach (var gone in saved.Dropped) _files.DeleteVideo(gone.Id);
        return Ok(ToDto(saved.Saved));
    }

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("tutorials")]
    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id)
    {
        if (_store.DeleteTutorial(id) is not { } removed) return NotFound();
        foreach (var v in removed.Videos) _files.DeleteVideo(v.Id);
        return NoContent();
    }

    // A video is uploaded on its own and attached when the tutorial is saved.
    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("tutorials")]
    [HttpPost("videos")]
    [RequestSizeLimit(VideoRequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = VideoRequestLimit)]
    public async Task<ActionResult<TutorialVideoDto>> UploadVideo(IFormFile? file, CancellationToken ct)
    {
        if (this.CurrentUserId() is not int userId) return Unauthorized();
        var saved = await _files.SaveVideoAsync(userId, file, ct);
        if (saved.Id is null) return BadRequest(saved.Error);
        var name = Path.GetFileNameWithoutExtension(file!.FileName ?? "").Trim();
        return Ok(new TutorialVideoDto(saved.Id, name.Length > 120 ? name[..120] : name, file.Length, $"/api/tutorials/videos/{saved.Id}"));
    }

    // ── Customer ───────────────────────────────────────────────────────────────────────────────────────

    // Products the user has actually paid for. An order still awaiting payment approval doesn't count yet, and
    // a cancelled one no longer does.
    private HashSet<int> PaidProducts(int userId) =>
        _store.GetUserOrders(userId)
            .Where(o => o.Status is OrderStatus.Preparing or OrderStatus.Completed)
            .SelectMany(o => o.Items.Select(i => i.ProductId))
            .ToHashSet();

    [HttpGet("mine")]
    public ActionResult<IEnumerable<TutorialDto>> Mine()
    {
        if (this.CurrentUserId() is not int userId) return Unauthorized();
        var paid = PaidProducts(userId);
        if (paid.Count == 0) return Ok(Array.Empty<TutorialDto>());
        return Ok(_store.GetTutorials()
            .Where(t => t.IsActive && t.ProductIds.Any(paid.Contains))
            .Select(ToDto)
            .ToList());
    }

    // Streamed with range support, so the player can seek without downloading the whole file first.
    [HttpGet("videos/{id}")]
    public IActionResult Video(string id)
    {
        if (this.CurrentUserId() is not int userId) return Unauthorized();
        if (!this.IsStaff())
        {
            var paid = PaidProducts(userId);
            var allowed = _store.GetTutorials().Any(t =>
                t.IsActive && t.Videos.Any(v => v.Id == id) && t.ProductIds.Any(paid.Contains));
            if (!allowed) return NotFound();
        }
        if (_files.OpenVideo(id) is not { } stored) return NotFound();
        Response.Headers.CacheControl = "private, max-age=3600";
        return File(stored.Content, stored.ContentType, enableRangeProcessing: true);
    }
}

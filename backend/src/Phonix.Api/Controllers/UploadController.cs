using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Security;
using Phonix.Api.Services;

namespace Phonix.Api.Controllers;

// Public image uploads (profile avatars + site/admin imagery). Saving requires an authenticated session;
// the stored image itself is served anonymously, since these pictures are shown openly across the store.
// Any decodable image format is accepted and re-encoded to WebP server-side (see SavePublicImageAsync).
[ApiController]
[Route("api/upload")]
public class UploadController : ControllerBase
{
    private readonly IFileStorageService _files;
    public UploadController(IFileStorageService files) => _files = files;

    [Authorize]
    [HttpPost]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile? file, CancellationToken ct)
    {
        if (this.CurrentUserId() is not int userId) return Unauthorized();

        var result = await _files.SavePublicImageAsync(userId, file, ct);
        if (result.Id is null) return BadRequest(new { error = result.Error });

        // Absolute URL so the value works as an <img src> from any origin (storefront, admin, SSR) and behind
        // the proxy/fallback domain. Scheme/host come from the forwarded headers when PHONIX_BEHIND_PROXY=true.
        var url = $"{Request.Scheme}://{Request.Host}/api/upload/{result.Id}";
        return Ok(new { url });
    }

    [AllowAnonymous]
    [HttpGet("{id}")]
    public IActionResult Get(string id)
    {
        var stored = _files.Open("avatars", id);
        if (stored is null) return NotFound();

        // Content is immutable (ids are random and never reused), so it can be cached aggressively.
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return File(stored.Content, stored.ContentType);
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;
using Phonix.Api.Services;

namespace Phonix.Api.Controllers;

// What the customer sends for one seat. The seat is addressed by its position in the unit's delivered seat
// list; the label travels along only so the admin queue shows the same «A - 8» the customer saw.
public record SeatSubmissionInput(int OrderId, int UnitId, int SeatIndex, string SeatLabel, string? ImageId, string Text);
public record SeatReviewInput(string? Note);
// What the panel needs for one delivered unit: whether this service asks for seat info at all, and whatever
// the customer has already filed.
public record SeatUnitInfoDto(bool Enabled, IReadOnlyList<SeatSubmissionDto> Submissions);

// A submission as it leaves the API. The image is referenced by id — never a public URL — and is streamed back
// through the owner-checked download endpoint below.
public record SeatSubmissionDto(int Id, int OrderId, int UnitId, int SeatIndex, string SeatLabel, int ProductId,
    string ProductName, string OrderCode, string UserName, string? ImageId, string Text, SeatSubmissionStatus Status,
    bool Editable, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, string? ReviewedBy, DateTime? ReviewedAtUtc,
    string? ReviewNote)
{
    public static SeatSubmissionDto From(SeatSubmission s) =>
        new(s.Id, s.OrderId, s.UnitId, s.SeatIndex, s.SeatLabel, s.ProductId, s.ProductName, s.OrderCode, s.UserName,
            s.ImageId, s.Text, s.Status, s.Editable, s.CreatedAtUtc, s.UpdatedAtUtc, s.ReviewedBy, s.ReviewedAtUtc,
            s.ReviewNote);
}

// Per-seat information a buyer files after delivery. A purchase covering several seats gets one submission per
// seat, so every person on a shared account supplies their own details independently.
//
// Ownership is never taken from the request: the order is loaded and checked against the session before
// anything is written, and images go to PROTECTED storage (as with KYC) so a customer's picture is not
// reachable by URL — only its owner or staff can stream it back.
[ApiController]
[Route("api/seat-info")]
[Authorize]
public class SeatSubmissionsController : ControllerBase
{
    private const int MaxTextLength = 2000;
    private const string FileCategory = "seat-info";

    private readonly IDataStore _store;
    private readonly IFileStorageService _files;
    public SeatSubmissionsController(IDataStore store, IFileStorageService files)
    {
        _store = store;
        _files = files;
    }

    // Resolves the unit the caller is addressing and refuses unless they own the order (or are staff). Whether
    // the product actually collects seat info is a SEPARATE question — see Enabled — because the panel needs a
    // plain "no" for products that don't, not a permission error.
    private (Order Order, OrderUnit Unit)? Resolve(int orderId, int unitId)
    {
        var order = _store.GetOrder(orderId);
        var unit = order?.Units.FirstOrDefault(u => u.Id == unitId);
        if (order is null || unit is null) return null;
        if (!this.OwnsOrStaff(order.UserId)) return null;
        return (order, unit);
    }

    private bool Enabled(OrderUnit unit) => _store.GetProduct(unit.ProductId) is { CollectSeatInfo: true };

    // Uploads one picture to protected storage and returns its opaque id; the client then sends that id with
    // the submission. Kept separate from the save so a slow upload never blocks the text the customer typed.
    [HttpPost("upload")]
    [RequestSizeLimit(8_000_000)]
    public async Task<ActionResult<FileUploadResult>> Upload(IFormFile? file)
    {
        if (this.CurrentUserId() is not int userId) return Unauthorized();
        var result = await _files.SaveAsync(userId, FileCategory, file);
        if (result.Error is not null) return BadRequest(result.Error);
        return new FileUploadResult(result.Id!);
    }

    // Streams a submitted picture. The owner is encoded in the id: a customer may only read their own, staff
    // may read anyone's. There is no other way to reach the file.
    [HttpGet("image/{id}")]
    public IActionResult Image(string id)
    {
        if (_files.OwnerOf(id) is not int ownerId) return BadRequest("شناسه فایل نامعتبر است.");
        if (!this.OwnsOrStaff(ownerId)) return Forbid();
        var stored = _files.Open(FileCategory, id);
        if (stored is null) return NotFound();
        return File(stored.Content, stored.ContentType);
    }

    // Whether this unit collects seat info, plus everything already filed for it — one call, which is exactly
    // what the panel needs to decide whether to render the form at all.
    [HttpGet("unit/{orderId:int}/{unitId:int}")]
    public ActionResult<SeatUnitInfoDto> ForUnit(int orderId, int unitId)
    {
        if (Resolve(orderId, unitId) is not { } ctx) return Forbid();
        return Ok(new SeatUnitInfoDto(Enabled(ctx.Unit),
            _store.GetSeatSubmissionsForUnit(orderId, unitId).Select(SeatSubmissionDto.From).ToList()));
    }

    [HttpPost]
    public ActionResult<SeatSubmissionDto> Save(SeatSubmissionInput input)
    {
        if (Resolve(input.OrderId, input.UnitId) is not { } ctx) return Forbid();
        if (!Enabled(ctx.Unit)) return BadRequest("این سرویس اطلاعات اضافه‌ای نمی‌خواهد.");
        if (!ctx.Unit.Delivered) return BadRequest("این اکانت هنوز تحویل نشده است.");
        if (input.SeatIndex < 0) return BadRequest("جایگاه نامعتبر است.");

        var text = (input.Text ?? "").Trim();
        if (text.Length > MaxTextLength) text = text[..MaxTextLength];
        if (text.Length == 0 && string.IsNullOrWhiteSpace(input.ImageId))
            return BadRequest("حداقل یک تصویر یا متن وارد کنید.");
        // An image id is only accepted from the customer who uploaded it — otherwise a guessed id could pin
        // someone else's file to this submission.
        if (!string.IsNullOrWhiteSpace(input.ImageId) && _files.OwnerOf(input.ImageId) != ctx.Order.UserId)
            return BadRequest("شناسه تصویر نامعتبر است.");

        var saved = _store.SaveSeatSubmission(new SeatSubmission
        {
            UserId = ctx.Order.UserId,
            OrderId = ctx.Order.Id,
            UnitId = ctx.Unit.Id,
            SeatIndex = input.SeatIndex,
            SeatLabel = (input.SeatLabel ?? "").Trim(),
            ProductId = ctx.Unit.ProductId,
            ProductName = ctx.Unit.Name,
            OrderCode = ctx.Order.Code,
            UserName = ctx.Order.UserName,
            ImageId = string.IsNullOrWhiteSpace(input.ImageId) ? null : input.ImageId,
            Text = text,
        });
        if (saved is null) return BadRequest("این اطلاعات بررسی شده و دیگر قابل ویرایش نیست.");
        return Ok(SeatSubmissionDto.From(saved));
    }

    // ── Staff review queue ─────────────────────────────────────────────────────────────────────────

    [HttpGet]
    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("seat-info")]
    public IEnumerable<SeatSubmissionDto> All([FromQuery] SeatSubmissionStatus? status) =>
        _store.GetSeatSubmissions(status).Select(SeatSubmissionDto.From);

    [HttpPost("{id:int}/review")]
    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("seat-info")]
    public ActionResult<SeatSubmissionDto> Review(int id, SeatReviewInput input)
    {
        var reviewed = _store.ReviewSeatSubmission(id, User.Identity?.Name, (input.Note ?? "").Trim() is { Length: > 0 } n ? n : null);
        return reviewed is null ? NotFound() : Ok(SeatSubmissionDto.From(reviewed));
    }

    // Hands the seat back to the customer — how staff ask for a clearer picture or a correction.
    [HttpPost("{id:int}/reopen")]
    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("seat-info")]
    public ActionResult<SeatSubmissionDto> Reopen(int id, SeatReviewInput input)
    {
        var reopened = _store.ReopenSeatSubmission(id, (input.Note ?? "").Trim() is { Length: > 0 } n ? n : null);
        return reopened is null ? NotFound() : Ok(SeatSubmissionDto.From(reopened));
    }
}

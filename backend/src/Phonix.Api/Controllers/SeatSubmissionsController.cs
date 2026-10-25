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
// What the panel needs for one delivered unit: whether this plan asks for seat info at all, the wording it
// asks with, and whatever the customer has already filed.
public record SeatUnitInfoDto(bool Enabled, string Hint, IReadOnlyList<SeatSubmissionDto> Submissions);

// A submission as it leaves the API. The image is referenced by id — never a public URL — and is streamed back
// through the owner-checked download endpoint below.
public record SeatSubmissionDto(int Id, int UserId, int OrderId, int UnitId, int SeatIndex, string SeatLabel, int ProductId,
    string ProductName, string OrderCode, string UserName, string? ImageId, string Text, SeatSubmissionStatus Status,
    bool Editable, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, string? ReviewedBy, DateTime? ReviewedAtUtc,
    string? ReviewNote, int EditLimit, int EditsUsed, int EditsLeft, IReadOnlyList<SeatSubmissionVersion> History,
    IReadOnlyList<SeatSubmissionEvent> Events,
    // Staff-side state: whether the seat is still waiting on the buyer, the warnings sent, and the warranty.
    bool ReopenedForEdit, int WarningsSent, DateTime? Warning1AtUtc, DateTime? Warning2AtUtc,
    string? LastWarningText, bool Revoked, DateTime? RevokedAtUtc, string? RevokedBy, string? RevokeReason,
    DateTime? RestoredAtUtc)
{
    public static SeatSubmissionDto From(SeatSubmission s) =>
        new(s.Id, s.UserId, s.OrderId, s.UnitId, s.SeatIndex, s.SeatLabel, s.ProductId, s.ProductName, s.OrderCode, s.UserName,
            s.ImageId, s.Text, s.Status, s.Editable, s.CreatedAtUtc, s.UpdatedAtUtc, s.ReviewedBy, s.ReviewedAtUtc,
            s.ReviewNote, s.EditLimit, s.EditsUsed, s.EditsLeft, s.History ?? new(), s.Events ?? new(),
            s.ReopenedForEdit, s.WarningsSent, s.Warning1AtUtc, s.Warning2AtUtc, s.LastWarningText,
            s.Revoked, s.RevokedAtUtc, s.RevokedBy, s.RevokeReason, s.RestoredAtUtc);
}

// One seat of one delivered account as the panel lists it: who bought it, which block of which shared account
// it is, what the buyer entered at checkout, and whatever they filed for it.
public record SeatRowDto(int OrderId, string OrderCode, int UserId, string UserName, string? UserEmail,
    string? UserPhone, int UnitId, int UnitIndex, int UnitCount, int ProductId, string ProductName, string? Plan,
    int SeatIndex, string SeatLabel, string AccountUsername, string? DeliveredAt, bool CollectsInfo,
    string SeatInfoHint, IReadOnlyList<OrderInputValue> DeviceInfo, SeatSubmissionDto? Submission)
{
    public static SeatRowDto From(SeatRow r) =>
        new(r.OrderId, r.OrderCode, r.UserId, r.UserName, r.UserEmail, r.UserPhone, r.UnitId, r.UnitIndex,
            r.UnitCount, r.ProductId, r.ProductName, r.Plan, r.SeatIndex, r.SeatLabel, r.AccountUsername,
            r.DeliveredAt, r.CollectsInfo, r.SeatInfoHint, r.DeviceInfo,
            r.Submission is null ? null : SeatSubmissionDto.From(r.Submission));
}

// A seat the signed-in buyer may no longer see the details of, and why. Their orders page needs all of them at
// once — before anything is clicked — because a closed seat greys its service out in the list.
public record SeatRevocationDto(int OrderId, int UnitId, int SeatIndex, string SeatLabel, int ProductId,
    string ProductName, string Reason, DateTime? RevokedAtUtc);

// Which seats one staff action applies to. Seats are addressed by position, not by submission id, because a
// seat the buyer never filed anything for has no row until the action itself creates one.
public record SeatTargetInput(int OrderId, int UnitId, List<int> SeatIndexes);
public record SeatWarnInput(int OrderId, int UnitId, List<int> SeatIndexes, int Level, string Message);
public record SeatRevokeInput(int OrderId, int UnitId, List<int> SeatIndexes, string Reason);

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
    // A staff action addresses the seats of ONE account; this is well past any real account's capacity and
    // keeps a hand-made request from asking for thousands of writes in one go.
    private const int MaxSeatsPerAction = 100;
    private const string FileCategory = "seat-info";

    private readonly IDataStore _store;
    private readonly IFileStorageService _files;
    private readonly IUserMailer _mailer;
    private readonly ISeatRosterService _roster;
    public SeatSubmissionsController(IDataStore store, IFileStorageService files, IUserMailer mailer,
        ISeatRosterService roster)
    {
        _store = store;
        _files = files;
        _mailer = mailer;
        _roster = roster;
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

    // The PLAN this unit was sold under — it owns whether seat info is collected, what to ask for, and how many
    // post-approval corrections the buyer gets. The unit records its plan as the display string
    // «{Type} · {Months} ماهه», so the plan is matched back by type + duration: the same two fields the seat
    // pool routes on. Null when the plan was deleted after the sale, which collects nothing.
    private ProductPlan? PlanFor(Order order, OrderUnit unit) => _roster.SeatInfoPlan(order, unit);

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
        var plan = PlanFor(ctx.Order, ctx.Unit);
        return Ok(new SeatUnitInfoDto(plan is not null, plan?.SeatInfoHint ?? "",
            _store.GetSeatSubmissionsForUnit(orderId, unitId).Select(SeatSubmissionDto.From).ToList()));
    }

    [HttpPost]
    public ActionResult<SeatSubmissionDto> Save(SeatSubmissionInput input)
    {
        if (Resolve(input.OrderId, input.UnitId) is not { } ctx) return Forbid();
        if (PlanFor(ctx.Order, ctx.Unit) is not { } plan) return BadRequest("این سرویس اطلاعات اضافه‌ای نمی‌خواهد.");
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
            // Snapshot the plan's allowance onto the submission, so changing the plan later never alters what
            // an existing buyer was already promised.
            EditLimit = Math.Max(0, plan.SeatInfoEditLimit),
        });
        if (saved is null)
            // A closed seat has nothing to set up, so the refusal has to say THAT rather than talk about review.
            return BadRequest(_store.GetSeatSubmissionsForUnit(input.OrderId, input.UnitId)
                .FirstOrDefault(s => s.SeatIndex == input.SeatIndex)?.Revoked == true
                ? "این اکانت از گارانتی خارج شده است و اطلاعات آن قابل ثبت نیست."
                : "این اطلاعات ثبت شده و قابل ویرایش نیست. برای تغییر آن، پشتیبانی باید آن را برای ویرایش باز کند.");
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

    // Turns the seat down. Unlike a reopen — which hands the seat back with what the customer sent still on it
    // — this clears the entry and the buyer files fresh details. What was refused moves into the seat's History
    // (picture included), so staff can still compare it with what comes next. The buyer is told twice (in-site
    // notification and email) because the form they come back to is blank, and a blank form with no
    // explanation looks like data loss.
    [HttpPost("{id:int}/reject")]
    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("seat-info")]
    public ActionResult<SeatSubmissionDto> Reject(int id, SeatReviewInput input)
    {
        var reason = (input.Note ?? "").Trim();
        if (reason.Length > MaxTextLength) reason = reason[..MaxTextLength];
        if (_store.RejectSeatSubmission(id, User.Identity?.Name, reason.Length > 0 ? reason : null) is not { } result)
            return NotFound();

        // The picture is NOT deleted: the refused version in History still shows it to staff.
        _ = _mailer.SeatInfoRejectedAsync(result.Submission);
        return Ok(SeatSubmissionDto.From(result.Submission));
    }

    // Hands the seat back to the customer — how staff ask for a clearer picture or a correction.
    [HttpPost("{id:int}/reopen")]
    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("seat-info")]
    public ActionResult<SeatSubmissionDto> Reopen(int id, SeatReviewInput input)
    {
        var note = (input.Note ?? "").Trim();
        if (note.Length > MaxTextLength) note = note[..MaxTextLength];
        var reopened = _store.ReopenSeatSubmission(id, note.Length > 0 ? note : null, User.Identity?.Name);
        if (reopened is null) return NotFound();

        // Reopening only helps if the customer knows: they hear it in the bell and by email, with the admin's
        // message in full (or the standard request for their new device's details when it was left empty).
        var message = note.Length > 0 ? note : OrderNotices.SeatInfoReopenDefault;
        var seat = string.IsNullOrWhiteSpace(reopened.SeatLabel) ? $"پروفایل {reopened.SeatIndex + 1}" : reopened.SeatLabel;
        var notice = OrderNotices.SeatInfoReopened(reopened.OrderCode, reopened.ProductName, seat, message);
        _store.AddNotification(reopened.UserId, notice.Title, notice.Body, notice.Link);
        _ = _mailer.SeatInfoReopenedAsync(reopened, message);
        return Ok(SeatSubmissionDto.From(reopened));
    }

    // ── Seat roster: search, «ثبت نشده‌ها», «خارج از گارانتی» ──────────────────────────────────────

    // Finds seats by anything that leads to them: order code, buyer name/email/phone, product, plan, the seat
    // label, the shared account's username, what the buyer filed, and what they entered at checkout. Every
    // seat of a matching order comes back, so searching an order code shows which block is whose.
    [HttpGet("seats")]
    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("seat-info")]
    public IEnumerable<SeatRowDto> Seats([FromQuery] string? q) =>
        (string.IsNullOrWhiteSpace(q) ? _roster.All() : _roster.Search(q!)).Select(SeatRowDto.From);

    // Seats whose plan asks for details and that are waiting on the BUYER: nothing ever filed, or staff
    // reopened the entry and the buyer never saved again.
    [HttpGet("unregistered")]
    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("seat-info")]
    public IEnumerable<SeatRowDto> Unregistered() => _roster.Unregistered().Select(SeatRowDto.From);

    [HttpGet("revoked")]
    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("seat-info")]
    public IEnumerable<SeatRowDto> RevokedSeats() => _roster.Revoked().Select(SeatRowDto.From);

    // Every seat of ONE delivered account — what the panel asks for before taking a single seat of a shared
    // account out of warranty («which user should be banned»).
    [HttpGet("unit/{orderId:int}/{unitId:int}/seats")]
    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("seat-info")]
    public IEnumerable<SeatRowDto> UnitSeats(int orderId, int unitId) =>
        _roster.ForUnit(orderId, unitId).Select(SeatRowDto.From);

    // ── Warranty actions ──────────────────────────────────────────────────────────────────────────────

    // Resolves the seats one action addresses, creating the staff-side row for any seat the buyer has never
    // filed anything for. Refuses anything that isn't a delivered seat of a real order, so a hand-typed index
    // can never mint a row for a seat that doesn't exist.
    private ActionResult<List<SeatSubmission>> Targets(int orderId, int unitId, IReadOnlyList<int>? seatIndexes)
    {
        if (seatIndexes is null || seatIndexes.Count == 0) return BadRequest("هیچ پروفایلی انتخاب نشده است.");
        if (seatIndexes.Count > MaxSeatsPerAction) return BadRequest("تعداد پروفایل‌های انتخابی بیش از حد مجاز است.");
        var order = _store.GetOrder(orderId);
        var unit = order?.Units.FirstOrDefault(u => u.Id == unitId);
        if (order is null || unit is null) return NotFound();
        if (!unit.Delivered || unit.Rejected) return BadRequest("این اکانت تحویل نشده است.");

        var seats = SeatContent.ParseOrWhole(unit.DeliveryContent);
        var plan = PlanFor(order, unit);
        var rows = new List<SeatSubmission>();
        foreach (var index in seatIndexes.Distinct())
        {
            if (seats.FirstOrDefault(s => s.Index == index) is not { } seat) return BadRequest("جایگاه نامعتبر است.");
            rows.Add(_store.EnsureSeatSubmission(new SeatSubmission
            {
                UserId = order.UserId,
                OrderId = order.Id,
                UnitId = unit.Id,
                SeatIndex = seat.Index,
                SeatLabel = seat.Label,
                ProductId = unit.ProductId,
                ProductName = unit.Name,
                OrderCode = order.Code,
                UserName = order.UserName,
                EditLimit = Math.Max(0, plan?.SeatInfoEditLimit ?? 0),
            }));
        }
        return rows;
    }

    // Warning 1 or 2 for the chosen seats. The buyer is told twice — in-site notification (written inside the
    // store's transaction) and email — because a warning they never saw is a warning that didn't happen.
    [HttpPost("warn")]
    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("seat-info")]
    public ActionResult<IEnumerable<SeatSubmissionDto>> Warn(SeatWarnInput input)
    {
        if (input.Level is not (1 or 2)) return BadRequest("سطح هشدار نامعتبر است.");
        var message = (input.Message ?? "").Trim();
        if (message.Length == 0) return BadRequest("متن پیام را وارد کنید.");
        if (message.Length > MaxTextLength) message = message[..MaxTextLength];

        var targets = Targets(input.OrderId, input.UnitId, input.SeatIndexes);
        if (targets.Result is not null) return targets.Result;

        var done = new List<SeatSubmissionDto>();
        foreach (var row in targets.Value!)
        {
            // Null means this level has already gone out for the seat; the others in the batch still go.
            if (_store.WarnSeat(row.Id, input.Level, User.Identity?.Name, message) is not { } warned) continue;
            _ = _mailer.SeatWarningAsync(warned, input.Level, message);
            done.Add(SeatSubmissionDto.From(warned));
        }
        if (done.Count == 0) return BadRequest("این هشدار قبلاً برای این پروفایل‌ها ارسال شده است.");
        return Ok(done);
    }

    // Takes the chosen seats out of warranty. On a shared account this is per-seat on purpose: one person
    // breaking the terms must not cost the other users of the same account their service.
    [HttpPost("revoke")]
    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("seat-info")]
    public ActionResult<IEnumerable<SeatSubmissionDto>> Revoke(SeatRevokeInput input)
    {
        var reason = (input.Reason ?? "").Trim();
        // The reason is the only thing the buyer is shown in place of their account details, so it is required.
        if (reason.Length == 0) return BadRequest("دلیل خروج از گارانتی را بنویسید.");
        if (reason.Length > MaxTextLength) reason = reason[..MaxTextLength];

        var targets = Targets(input.OrderId, input.UnitId, input.SeatIndexes);
        if (targets.Result is not null) return targets.Result;

        var done = new List<SeatSubmissionDto>();
        foreach (var row in targets.Value!)
        {
            if (_store.RevokeSeat(row.Id, User.Identity?.Name, reason) is not { } revoked) continue;
            _ = _mailer.SeatRevokedAsync(revoked);
            done.Add(SeatSubmissionDto.From(revoked));
        }
        return Ok(done);
    }

    // Hands the warranty back: the seat returns to exactly what it was — the buyer sees their details again
    // and may file or correct them exactly as before.
    [HttpPost("restore")]
    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("seat-info")]
    public ActionResult<IEnumerable<SeatSubmissionDto>> Restore(SeatTargetInput input)
    {
        var targets = Targets(input.OrderId, input.UnitId, input.SeatIndexes);
        if (targets.Result is not null) return targets.Result;

        var done = new List<SeatSubmissionDto>();
        foreach (var row in targets.Value!)
        {
            if (_store.RestoreSeat(row.Id, User.Identity?.Name) is not { } restored) continue;
            _ = _mailer.SeatRestoredAsync(restored);
            done.Add(SeatSubmissionDto.From(restored));
        }
        return Ok(done);
    }

    // ── Customer side ─────────────────────────────────────────────────────────────────────────────────

    // Every seat of the signed-in buyer's OWN orders that is currently out of warranty. The orders page needs
    // all of them up front: a service with a closed seat is greyed out before anything is opened, and the
    // reason replaces the account details once it is.
    [HttpGet("my-revocations")]
    public ActionResult<IEnumerable<SeatRevocationDto>> MyRevocations()
    {
        if (this.CurrentUserId() is not int userId) return Unauthorized();
        return Ok(_store.GetUserSeatSubmissions(userId)
            .Where(s => s.Revoked)
            .Select(s => new SeatRevocationDto(s.OrderId, s.UnitId, s.SeatIndex, s.SeatLabel, s.ProductId,
                s.ProductName, s.RevokeReason ?? "", s.RevokedAtUtc)));
    }
}

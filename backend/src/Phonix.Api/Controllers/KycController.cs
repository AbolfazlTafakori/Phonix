using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;
using Phonix.Api.Services;

namespace Phonix.Api.Controllers;

public record KycInput(string FullName, string NationalId, string BirthDate, string CardImage, string SelfieImage);
public record KycActionInput(string? Note);

[ApiController]
[Route("api/kyc")]
[Authorize]
public class KycController : ControllerBase
{
    private readonly StoreData _store;
    private readonly IFileStorageService _files;
    public KycController(StoreData store, IFileStorageService files)
    {
        _store = store;
        _files = files;
    }

    // Uploads a KYC image to protected storage (outside the web root) and returns its opaque id; the client
    // then submits that id as CardImage/SelfieImage. The owner is taken from the session, never the client.
    [HttpPost("upload")]
    [RequestSizeLimit(8_000_000)]
    public async Task<ActionResult<FileUploadResult>> Upload(IFormFile? file)
    {
        if (this.CurrentUserId() is not int userId) return Unauthorized();
        var result = await _files.SaveAsync(userId, "kyc", file);
        if (result.Error is not null) return BadRequest(result.Error);
        return new FileUploadResult(result.Id!);
    }

    // Streams a stored KYC image. The owner is encoded in the id; a customer may only read their own files,
    // staff may read anyone's — otherwise 403. The raw file is never reachable by URL any other way.
    [HttpGet("download/{id}")]
    public IActionResult Download(string id)
    {
        if (_files.OwnerOf(id) is not int ownerId) return BadRequest("شناسه فایل نامعتبر است.");
        if (!this.OwnsOrStaff(ownerId)) return Forbid();
        var stored = _files.Open("kyc", id);
        if (stored is null) return NotFound();
        return File(stored.Content, stored.ContentType);
    }

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [HttpGet]
    public IEnumerable<KycRequest> Get([FromQuery] KycStatus? status) => _store.GetAllKyc(status);

    [HttpGet("user/{userId:int}")]
    public ActionResult<KycRequest?> GetForUser(int userId)
    {
        if (!this.OwnsOrStaff(userId)) return Forbid();
        return _store.GetKycForUser(userId);
    }

    [HttpPost]
    public ActionResult<KycRequest> Submit(KycInput input)
    {
        if (this.CurrentUserId() is not int userId) return Unauthorized();
        // national-ID KYC is the level-2 step; it requires level 1 (an approved bank card) first.
        var current = _store.GetUser(userId);
        if (current is null) return Unauthorized();
        if (current.VerificationLevel < 1)
            return BadRequest("برای احراز هویت سطح ۲ ابتدا باید کارت بانکی شما ثبت و تأیید شود (سطح ۱).");
        if (string.IsNullOrWhiteSpace(input.FullName) || string.IsNullOrWhiteSpace(input.NationalId))
            return BadRequest("نام کامل و کد ملی الزامی است.");
        if (!InputValidation.IsValidNationalId(input.NationalId))
            return BadRequest("کد ملی واردشده معتبر نیست.");
        return _store.SubmitKyc(new KycRequest
        {
            UserId = userId,
            FullName = input.FullName,
            NationalId = input.NationalId,
            BirthDate = input.BirthDate,
            CardImage = input.CardImage,
            SelfieImage = input.SelfieImage,
        });
    }

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [HttpPost("{id:int}/approve")]
    public ActionResult<KycRequest> Approve(int id) =>
        _store.SetKycStatus(id, KycStatus.Approved, null) is { } k ? k : NotFound();

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [HttpPost("{id:int}/reject")]
    public ActionResult<KycRequest> Reject(int id, KycActionInput? input) =>
        _store.SetKycStatus(id, KycStatus.Rejected, input?.Note) is { } k ? k : NotFound();
}

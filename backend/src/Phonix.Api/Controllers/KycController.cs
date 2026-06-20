using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Controllers;

public record KycInput(string FullName, string NationalId, string BirthDate, string CardImage, string SelfieImage);
public record KycActionInput(string? Note);

[ApiController]
[Route("api/kyc")]
[Authorize]
public class KycController : ControllerBase
{
    private readonly StoreData _store;
    public KycController(StoreData store) => _store = store;

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
        if (string.IsNullOrWhiteSpace(input.FullName) || string.IsNullOrWhiteSpace(input.NationalId))
            return BadRequest("نام کامل و کد ملی الزامی است.");
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

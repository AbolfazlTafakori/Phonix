using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Models;

namespace Phonix.Api.Controllers;

public record KycInput(int UserId, string FullName, string NationalId, string BirthDate, string CardImage, string SelfieImage);
public record KycActionInput(string? Note);

[ApiController]
[Route("api/kyc")]
public class KycController : ControllerBase
{
    private readonly StoreData _store;
    public KycController(StoreData store) => _store = store;

    [HttpGet]
    public IEnumerable<KycRequest> Get([FromQuery] KycStatus? status) => _store.GetAllKyc(status);

    [HttpGet("user/{userId:int}")]
    public KycRequest? GetForUser(int userId) => _store.GetKycForUser(userId);

    [HttpPost]
    public ActionResult<KycRequest> Submit(KycInput input)
    {
        if (string.IsNullOrWhiteSpace(input.FullName) || string.IsNullOrWhiteSpace(input.NationalId))
            return BadRequest("نام کامل و کد ملی الزامی است.");
        return _store.SubmitKyc(new KycRequest
        {
            UserId = input.UserId,
            FullName = input.FullName,
            NationalId = input.NationalId,
            BirthDate = input.BirthDate,
            CardImage = input.CardImage,
            SelfieImage = input.SelfieImage,
        });
    }

    [HttpPost("{id:int}/approve")]
    public ActionResult<KycRequest> Approve(int id) =>
        _store.SetKycStatus(id, KycStatus.Approved, null) is { } k ? k : NotFound();

    [HttpPost("{id:int}/reject")]
    public ActionResult<KycRequest> Reject(int id, KycActionInput? input) =>
        _store.SetKycStatus(id, KycStatus.Rejected, input?.Note) is { } k ? k : NotFound();
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;
using Phonix.Api.Services;

namespace Phonix.Api.Controllers;

public record AddCardInput(string CardNumber, string HolderName, string CardImage);
public record CardActionInput(string? Note);

[ApiController]
[Route("api/cards")]
[Authorize]
public class CardsController : ControllerBase
{
    private readonly IDataStore _store;
    private readonly IFileStorageService _files;
    public CardsController(IDataStore store, IFileStorageService files)
    {
        _store = store;
        _files = files;
    }

    // Uploads a bank-card photo to protected storage and returns its opaque id; the client submits that id
    // as CardImage when registering the card. Owner is taken from the session, never the client.
    [HttpPost("upload")]
    [RequestSizeLimit(8_000_000)]
    public async Task<ActionResult<FileUploadResult>> Upload(IFormFile? file)
    {
        if (this.CurrentUserId() is not int userId) return Unauthorized();
        var result = await _files.SaveAsync(userId, "cards", file);
        if (result.Error is not null) return BadRequest(result.Error);
        return new FileUploadResult(result.Id!);
    }

    // Streams a stored card photo. Owner is encoded in the id; a customer may only read their own, staff
    // may read anyone's — otherwise 403.
    [HttpGet("download/{id}")]
    public IActionResult Download(string id)
    {
        if (_files.OwnerOf(id) is not int ownerId) return BadRequest("شناسه فایل نامعتبر است.");
        if (!this.OwnsOrStaff(ownerId)) return Forbid();
        var stored = _files.Open("cards", id);
        if (stored is null) return NotFound();
        return File(stored.Content, stored.ContentType);
    }

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("cards")]
    [HttpGet]
    public IEnumerable<BankCard> Get([FromQuery] BankCardStatus? status) => _store.GetAllCards(status);

    [HttpGet("user/{userId:int}")]
    public ActionResult<IEnumerable<BankCard>> ForUser(int userId)
    {
        if (!this.OwnsOrStaff(userId)) return Forbid();
        return Ok(_store.GetUserCards(userId));
    }

    // Registers a card for the authenticated user. Requires an approved KYC (enforced in the store) and
    // the holder name is copied from it — a customer can never register a card in someone else's name.
    [HttpPost]
    public ActionResult<BankCard> Add(AddCardInput input)
    {
        if (this.CurrentUserId() is not int userId) return Unauthorized();
        var result = _store.AddCard(userId, input.CardNumber ?? "", input.HolderName ?? "", input.CardImage ?? "");
        if (result.Error is not null) return BadRequest(result.Error);
        return result.Card!;
    }

    // Only staff can delete a card. A user may register a card but cannot remove it themselves — once
    // submitted it stays on record for the support team to verify or revoke.
    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("cards")]
    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id)
    {
        if (_store.GetCard(id) is null) return NotFound();
        return _store.DeleteCard(id) ? NoContent() : NotFound();
    }

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("cards")]
    [HttpPost("{id:int}/approve")]
    public ActionResult<BankCard> Approve(int id) =>
        _store.SetCardStatus(id, BankCardStatus.Approved, null) is { } c ? c : NotFound();

    [Authorize(Roles = AuthExtensions.StaffRoles)]
    [AdminPermission("cards")]
    [HttpPost("{id:int}/reject")]
    public ActionResult<BankCard> Reject(int id, CardActionInput? input) =>
        _store.SetCardStatus(id, BankCardStatus.Rejected, input?.Note) is { } c ? c : NotFound();
}

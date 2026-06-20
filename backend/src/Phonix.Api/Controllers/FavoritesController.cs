using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Security;

namespace Phonix.Api.Controllers;

public record FavoriteInput(int ProductId);

[ApiController]
[Route("api/favorites")]
[Authorize]
public class FavoritesController : ControllerBase
{
    private readonly StoreData _store;
    public FavoritesController(StoreData store) => _store = store;

    [HttpGet("user/{userId:int}")]
    public ActionResult<IEnumerable<int>> ForUser(int userId)
    {
        if (!this.OwnsOrStaff(userId)) return Forbid();
        return Ok(_store.GetFavorites(userId));
    }

    [HttpPost("toggle")]
    public ActionResult Toggle(FavoriteInput input)
    {
        if (this.CurrentUserId() is not int userId) return Unauthorized();
        return Ok(new { favorited = _store.ToggleFavorite(userId, input.ProductId) });
    }
}

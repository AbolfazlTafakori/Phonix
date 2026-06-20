using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;

namespace Phonix.Api.Controllers;

[ApiController]
[Route("api/hero")]
[Authorize(Roles = AuthExtensions.StaffRoles)]
public class HeroController : ControllerBase
{
    private readonly StoreData _store;
    public HeroController(StoreData store) => _store = store;

    [AllowAnonymous]
    [HttpGet]
    public IEnumerable<HeroSlide> Get() => _store.GetHeroSlides();

    [AllowAnonymous]
    [HttpGet("{id:int}")]
    public ActionResult<HeroSlide> Get(int id) => _store.GetHeroSlide(id) is { } s ? s : NotFound();

    [HttpPost]
    public ActionResult<HeroSlide> Create(HeroSlide input) => _store.AddHeroSlide(input);

    [HttpPut("{id:int}")]
    public ActionResult<HeroSlide> Update(int id, HeroSlide input)
    {
        input.Id = id;
        return _store.UpdateHeroSlide(input) ? _store.GetHeroSlide(id)! : NotFound();
    }

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id) => _store.DeleteHeroSlide(id) ? NoContent() : NotFound();
}

[ApiController]
[Route("api/home-categories")]
[Authorize(Roles = AuthExtensions.StaffRoles)]
public class HomeCategoriesController : ControllerBase
{
    private readonly StoreData _store;
    public HomeCategoriesController(StoreData store) => _store = store;

    [AllowAnonymous]
    [HttpGet]
    public IEnumerable<HomeCategory> Get() => _store.GetHomeCategories();

    [AllowAnonymous]
    [HttpGet("{id:int}")]
    public ActionResult<HomeCategory> Get(int id) => _store.GetHomeCategory(id) is { } c ? c : NotFound();

    [HttpPost]
    public ActionResult<HomeCategory> Create(HomeCategory input) => _store.AddHomeCategory(input);

    [HttpPut("{id:int}")]
    public ActionResult<HomeCategory> Update(int id, HomeCategory input)
    {
        input.Id = id;
        return _store.UpdateHomeCategory(input) ? _store.GetHomeCategory(id)! : NotFound();
    }

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id) => _store.DeleteHomeCategory(id) ? NoContent() : NotFound();
}

[ApiController]
[Route("api/showcase")]
[Authorize(Roles = AuthExtensions.StaffRoles)]
public class ShowcaseController : ControllerBase
{
    private readonly StoreData _store;
    public ShowcaseController(StoreData store) => _store = store;

    [AllowAnonymous]
    [HttpGet]
    public IEnumerable<Showcase> Get() => _store.GetShowcase();

    [AllowAnonymous]
    [HttpGet("{id:int}")]
    public ActionResult<Showcase> Get(int id) => _store.GetShowcaseItem(id) is { } s ? s : NotFound();

    [HttpPost]
    public ActionResult<Showcase> Create(Showcase input) => _store.AddShowcase(input);

    [HttpPut("{id:int}")]
    public ActionResult<Showcase> Update(int id, Showcase input)
    {
        input.Id = id;
        return _store.UpdateShowcase(input) ? _store.GetShowcaseItem(id)! : NotFound();
    }

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id) => _store.DeleteShowcase(id) ? NoContent() : NotFound();
}

[ApiController]
[Route("api/blog")]
[Authorize(Roles = AuthExtensions.StaffRoles)]
public class BlogController : ControllerBase
{
    private readonly StoreData _store;
    public BlogController(StoreData store) => _store = store;

    [AllowAnonymous]
    [HttpGet]
    public IEnumerable<BlogPost> Get() => _store.GetBlogPosts();

    [AllowAnonymous]
    [HttpGet("{id:int}")]
    public ActionResult<BlogPost> Get(int id) => _store.GetBlogPost(id) is { } p ? p : NotFound();

    [HttpPost]
    public ActionResult<BlogPost> Create(BlogPost input) => _store.AddBlogPost(input);

    [HttpPut("{id:int}")]
    public ActionResult<BlogPost> Update(int id, BlogPost input)
    {
        input.Id = id;
        return _store.UpdateBlogPost(input) ? _store.GetBlogPost(id)! : NotFound();
    }

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id) => _store.DeleteBlogPost(id) ? NoContent() : NotFound();
}

[ApiController]
[Route("api/site-content")]
[Authorize(Roles = AuthExtensions.StaffRoles)]
public class SiteContentController : ControllerBase
{
    private readonly StoreData _store;
    public SiteContentController(StoreData store) => _store = store;

    [AllowAnonymous]
    [HttpGet]
    public SiteContent Get() => _store.GetSiteContent();

    [HttpPut]
    public SiteContent Update(SiteContent input)
    {
        _store.UpdateSiteContent(input);
        return _store.GetSiteContent();
    }
}

[ApiController]
[Route("api/advanced-settings")]
[Authorize(Roles = AuthExtensions.StaffRoles)]
public class AdvancedSettingsController : ControllerBase
{
    private readonly StoreData _store;
    public AdvancedSettingsController(StoreData store) => _store = store;

    [AllowAnonymous]
    [HttpGet]
    public AdvancedSettings Get() => _store.GetAdvancedSettings();

    [HttpPut]
    public AdvancedSettings Update(AdvancedSettings input)
    {
        _store.UpdateAdvancedSettings(input);
        return _store.GetAdvancedSettings();
    }
}

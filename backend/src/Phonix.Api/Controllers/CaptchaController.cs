using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Security;

namespace Phonix.Api.Controllers;

public record CaptchaDto(string Id, string Image);

[ApiController]
[Route("api/captcha")]
[AllowAnonymous]
public class CaptchaController : ControllerBase
{
    private readonly ICaptchaService _captcha;
    public CaptchaController(ICaptchaService captcha) => _captcha = captcha;

    // Issues a fresh image challenge for the login/register forms. Anonymous by design — it gates those flows.
    [HttpGet]
    public CaptchaDto Get()
    {
        var (id, image) = _captcha.Issue();
        return new CaptchaDto(id, image);
    }
}

using System.Security.Cryptography;

namespace Phonix.Api.Security;

public static class AuthCookies
{
    public const string Token = "ppx_token";
    public const string Csrf = "ppx_csrf";
    public const string CsrfHeader = "X-CSRF-Token";

    // issues the httpOnly session cookie plus a JS-readable CSRF token (double-submit pattern).
    public static void Issue(HttpResponse response, string token, bool secure)
    {
        var expires = DateTimeOffset.UtcNow.AddDays(3);
        response.Cookies.Append(Token, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = expires,
        });
        response.Cookies.Append(Csrf, Convert.ToHexString(RandomNumberGenerator.GetBytes(16)), new CookieOptions
        {
            HttpOnly = false,
            Secure = secure,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = expires,
        });
    }

    public static void Clear(HttpResponse response)
    {
        response.Cookies.Delete(Token);
        response.Cookies.Delete(Csrf);
    }
}

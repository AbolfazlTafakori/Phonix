using System.Security.Cryptography;

namespace Phonix.Api.Security;

public static class AuthCookies
{
    public const string Token = "ppx_token";
    public const string Csrf = "ppx_csrf";
    public const string CsrfHeader = "X-CSRF-Token";

    // issues the httpOnly session cookie plus a JS-readable CSRF token (double-submit pattern).
    // persistent=false omits Expires, making both cookies session-only: the browser drops them when it
    // closes. Admin-panel sessions use this so re-entering the panel always requires a fresh login + 2FA,
    // while main-site customer sessions stay persistent (survive a browser restart).
    public static void Issue(HttpResponse response, string token, bool secure, bool persistent = true)
    {
        DateTimeOffset? expires = persistent ? DateTimeOffset.UtcNow.AddDays(3) : null;
        var tokenOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Strict,
            Path = "/",
        };
        var csrfOptions = new CookieOptions
        {
            HttpOnly = false,
            Secure = secure,
            SameSite = SameSiteMode.Strict,
            Path = "/",
        };
        if (expires is { } e)
        {
            tokenOptions.Expires = e;
            csrfOptions.Expires = e;
        }
        response.Cookies.Append(Token, token, tokenOptions);
        response.Cookies.Append(Csrf, Convert.ToHexString(RandomNumberGenerator.GetBytes(16)), csrfOptions);
    }

    public static void Clear(HttpResponse response)
    {
        response.Cookies.Delete(Token);
        response.Cookies.Delete(Csrf);
    }
}

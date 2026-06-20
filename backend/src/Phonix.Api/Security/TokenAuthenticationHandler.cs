using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Phonix.Api.Data;

namespace Phonix.Api.Security;

public class TokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Bearer";
    private readonly StoreData _store;

    public TokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        StoreData store)
        : base(options, logger, encoder)
        => _store = store;

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // browsers send the token in an httpOnly cookie; API clients may use a Bearer header.
        string? token = null;
        string? header = Request.Headers.Authorization;
        if (!string.IsNullOrWhiteSpace(header) && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            token = header["Bearer ".Length..].Trim();
        else if (Request.Cookies.TryGetValue(AuthCookies.Token, out var cookieToken))
            token = cookieToken;

        if (string.IsNullOrWhiteSpace(token))
            return Task.FromResult(AuthenticateResult.NoResult());

        var user = _store.ResolveSession(token);
        if (user is null)
            return Task.FromResult(AuthenticateResult.Fail("توکن نامعتبر است."));

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}

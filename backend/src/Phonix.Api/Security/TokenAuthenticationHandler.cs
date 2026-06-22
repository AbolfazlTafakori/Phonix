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
    private readonly ISessionProtector _sessions;

    public TokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        StoreData store,
        ISessionProtector sessions)
        : base(options, logger, encoder)
    {
        _store = store;
        _sessions = sessions;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // browsers send the encrypted session token in an httpOnly cookie; API clients may use a Bearer header.
        string? token = null;
        string? header = Request.Headers.Authorization;
        if (!string.IsNullOrWhiteSpace(header) && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            token = header["Bearer ".Length..].Trim();
        else if (Request.Cookies.TryGetValue(AuthCookies.Token, out var cookieToken))
            token = cookieToken;

        if (string.IsNullOrWhiteSpace(token))
            return Task.FromResult(AuthenticateResult.NoResult());

        // The token is self-contained: decrypt and validate it with the persisted Data Protection key ring
        // rather than an in-memory table, so a session stays valid across server restarts.
        var payload = _sessions.Unprotect(token);
        if (payload is null)
            return Task.FromResult(AuthenticateResult.Fail("توکن نامعتبر است."));

        // Re-check the live user on every request so a ban, deletion, or password change (which rotates the
        // security stamp) takes effect immediately, even though the token itself carries no server state.
        var user = _store.GetUser(payload.UserId);
        if (user is null || user.Blocked)
            return Task.FromResult(AuthenticateResult.Fail("نشست نامعتبر است."));
        if (!string.Equals(user.SecurityStamp ?? "", payload.SecurityStamp, StringComparison.Ordinal))
            return Task.FromResult(AuthenticateResult.Fail("نشست منقضی شده است."));

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

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Phonix.Api.Models;

namespace Phonix.Api.Security;

// The decrypted contents of a session cookie. Everything needed to authenticate a request is carried
// inside the encrypted cookie itself, so there is no server-side session table and logins survive a
// process restart.
public sealed record SessionPayload(int UserId, string Username, UserRole Role, string SecurityStamp);

public interface ISessionProtector
{
    // Builds the encrypted, time-limited token to place in the auth cookie for this user.
    string Protect(AppUser user);

    // Decrypts and validates a token, returning its payload — or null when the token is missing,
    // tampered with, signed under a rotated/absent key, or expired.
    SessionPayload? Unprotect(string? token);
}

public sealed class SessionProtector : ISessionProtector
{
    // How long a session stays valid from issue. The cookie carries the same expiry (AuthCookies.Issue).
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(3);

    // Bumping this string force-invalidates every outstanding session, independent of key rotation.
    private const string Purpose = "Phonix.Session.v1";

    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ITimeLimitedDataProtector _protector;

    public SessionProtector(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector(Purpose).ToTimeLimitedDataProtector();

    public string Protect(AppUser user)
    {
        var payload = new SessionPayload(user.Id, user.Username, user.Role, user.SecurityStamp ?? "");
        return _protector.Protect(JsonSerializer.Serialize(payload, Json), Lifetime);
    }

    public SessionPayload? Unprotect(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            return JsonSerializer.Deserialize<SessionPayload>(_protector.Unprotect(token), Json);
        }
        catch
        {
            // CryptographicException (tampered / expired / unknown key) or a malformed payload → no session.
            return null;
        }
    }
}

public static class SessionProtectionExtensions
{
    // Registers Data Protection with a PERSISTED key ring so the keys that encrypt session cookies survive
    // restarts. Without persisted keys the ring regenerates on every boot and every cookie silently breaks —
    // which is exactly the "everyone logged out on restart" problem this is meant to fix. Also registers
    // the stateless session protector itself.
    public static IServiceCollection AddPhonixSessions(this IServiceCollection services, string keyRingPath)
    {
        Directory.CreateDirectory(keyRingPath);
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath))
            .SetApplicationName("Phonix");
        services.AddSingleton<ISessionProtector, SessionProtector>();
        services.AddSingleton<ITwoFactorChallenge, TwoFactorChallenge>();
        return services;
    }
}

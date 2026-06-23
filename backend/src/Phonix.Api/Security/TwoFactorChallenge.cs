using Microsoft.AspNetCore.DataProtection;

namespace Phonix.Api.Security;

// Carries the brief, signed "password was correct, now prove the second factor" state between the two
// login steps. Encrypted with the same persisted Data Protection ring as sessions, scoped to its own
// purpose and a 5-minute lifetime, so it cannot be replayed as a session cookie or reused after expiry.
public interface ITwoFactorChallenge
{
    string Issue(int userId);
    int? Resolve(string? token);
}

public sealed class TwoFactorChallenge : ITwoFactorChallenge
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private const string Purpose = "Phonix.TwoFactor.Challenge.v1";

    private readonly ITimeLimitedDataProtector _protector;

    public TwoFactorChallenge(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector(Purpose).ToTimeLimitedDataProtector();

    public string Issue(int userId) => _protector.Protect(userId.ToString(), Lifetime);

    public int? Resolve(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            return int.TryParse(_protector.Unprotect(token), out var id) ? id : null;
        }
        catch
        {
            return null;
        }
    }
}

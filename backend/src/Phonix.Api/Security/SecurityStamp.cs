using System.Security.Cryptography;

namespace Phonix.Api.Security;

// A user's security stamp: rotated whenever credentials change, which invalidates every session issued before
// the change. Lives here with the other auth primitives rather than on a store, since it is not persistence.
public static class SecurityStamp
{
    public static string New() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
}

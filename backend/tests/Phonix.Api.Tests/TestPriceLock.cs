using Microsoft.AspNetCore.DataProtection;
using Phonix.Api.Security;

namespace Phonix.Api.Tests;

// A real PriceLock over an ephemeral key ring — the tokens are signed and verified exactly as in production,
// they just do not outlive the test process.
internal static class TestPriceLock
{
    public static IPriceLock Create() => new PriceLock(DataProtectionProvider.Create("Phonix.Tests"));
}

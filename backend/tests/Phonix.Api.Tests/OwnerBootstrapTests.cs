using Phonix.Api.Data;
using Xunit;

namespace Phonix.Api.Tests;

// EnsureOwnerFromEnvironment only ever collects a username/password from env vars — there is no email input
// in that flow at all. It used to force EmailVerified=true anyway, so a fresh install's owner showed a green
// "verified" badge next to a blank email in the account panel: a real address was never proven, because there
// was never a real address to begin with.
public class OwnerBootstrapTests : IDisposable
{
    private readonly string? _origUser = Environment.GetEnvironmentVariable("PHONIX_OWNER_USERNAME");
    private readonly string? _origPass = Environment.GetEnvironmentVariable("PHONIX_OWNER_PASSWORD");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PHONIX_OWNER_USERNAME", _origUser);
        Environment.SetEnvironmentVariable("PHONIX_OWNER_PASSWORD", _origPass);
    }

    [Fact]
    public void A_freshly_bootstrapped_owner_has_no_email_and_is_not_marked_verified()
    {
        Environment.SetEnvironmentVariable("PHONIX_OWNER_USERNAME", "bootstrapowner1");
        Environment.SetEnvironmentVariable("PHONIX_OWNER_PASSWORD", "ownerpass1");

        var store = TestStore.Create();
        store.EnsureOwnerFromEnvironment();

        var owner = store.GetUserByUsername("bootstrapowner1");
        Assert.NotNull(owner);
        Assert.Equal("", owner!.Email);
        Assert.False(owner.EmailVerified); // nothing was ever proven — must not claim otherwise
    }

    [Fact]
    public void An_owner_stuck_in_the_old_buggy_state_is_repaired_on_the_next_boot()
    {
        Environment.SetEnvironmentVariable("PHONIX_OWNER_USERNAME", "bootstrapowner2");
        Environment.SetEnvironmentVariable("PHONIX_OWNER_PASSWORD", "ownerpass1");

        var store = TestStore.Create();
        store.EnsureOwnerFromEnvironment(); // creates it correctly (Email "", EmailVerified false)

        // Simulate an account that was created under the old buggy bootstrap (or otherwise ended up in this
        // state) — the invariant "EmailVerified requires an actual Email" was violated before this fix existed.
        var owner = store.GetUserByUsername("bootstrapowner2")!;
        store.UpdateUser(owner.Id, u => u.EmailVerified = true);
        Assert.True(store.GetUserByUsername("bootstrapowner2")!.EmailVerified);

        // The next boot's owner-invariant check must notice and correct it, exactly like it already does for
        // Role/Blocked/Password drift.
        store.EnsureOwnerFromEnvironment();
        Assert.False(store.GetUserByUsername("bootstrapowner2")!.EmailVerified);
    }
}

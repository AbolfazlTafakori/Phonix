using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;
using Xunit;

namespace Phonix.Api.Tests;

// The owner is not a role — it is whichever Admin account holds the username in PHONIX_OWNER_USERNAME
// (OwnerAccount). Every owner-only section keys off that string, so the username itself is a privilege
// handle: whoever ends up holding it inherits the payment infrastructure and the V2Ray panel credentials
// that the owner gate exists to keep above the ordinary Admin level.
//
// That makes two things load-bearing, and neither was enforced before:
//   • nobody else may take the name, and
//   • the owner may not walk away from it (which would free it for the next account that asks).
public class OwnerProtectionTests : IDisposable
{
    private readonly string? _origUser = Environment.GetEnvironmentVariable("PHONIX_OWNER_USERNAME");
    private readonly string? _origPass = Environment.GetEnvironmentVariable("PHONIX_OWNER_PASSWORD");

    public OwnerProtectionTests()
    {
        Environment.SetEnvironmentVariable("PHONIX_OWNER_USERNAME", "theowner");
        Environment.SetEnvironmentVariable("PHONIX_OWNER_PASSWORD", "ownerpass1");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PHONIX_OWNER_USERNAME", _origUser);
        Environment.SetEnvironmentVariable("PHONIX_OWNER_PASSWORD", _origPass);
    }

    private static IDataStore Seeded()
    {
        var store = TestStore.Create();
        store.EnsureOwnerFromEnvironment();
        return store;
    }

    [Fact]
    public void Another_account_cannot_rename_itself_to_the_owner_username()
    {
        var store = Seeded();
        var other = store.RegisterUser(new AppUser { Name = "Other", Username = "otheruser", Password = "x", Email = "o@example.com" });

        // Not merely "taken" — reserved. The distinction matters because the owner row can legitimately be
        // absent (a Standby that has not pulled its first snapshot), and that is exactly the moment a plain
        // uniqueness check would hand the name away.
        Assert.NotNull(store.SetUsername(other.Id, "theowner"));
        Assert.Equal("otheruser", store.GetUser(other.Id)!.Username);
    }

    [Fact]
    public void The_owner_cannot_rename_away_from_the_configured_username()
    {
        var store = Seeded();
        var owner = store.GetUserByUsername("theowner")!;

        Assert.NotNull(store.SetUsername(owner.Id, "somethingelse"));
        Assert.Equal("theowner", store.GetUser(owner.Id)!.Username);
        Assert.True(OwnerAccount.IsOwner(store.GetUser(owner.Id)!.Username));
    }

    [Fact]
    public void The_reservation_holds_even_when_the_owner_row_does_not_exist_yet()
    {
        // No EnsureOwnerFromEnvironment here: this is the un-bootstrapped state.
        var store = TestStore.Create();
        var other = store.RegisterUser(new AppUser { Name = "Other", Username = "otheruser", Password = "x", Email = "o@example.com" });

        Assert.NotNull(store.SetUsername(other.Id, "theowner"));
        Assert.True(OwnerAccount.IsReservedUsername("theowner"));
        Assert.True(OwnerAccount.IsReservedUsername("THEOWNER")); // the owner check is case-insensitive
        Assert.False(OwnerAccount.IsReservedUsername("theowner", currentUsername: "theowner"));
        Assert.False(OwnerAccount.IsReservedUsername("otheruser"));
    }

    [Fact]
    public void Renaming_is_still_ordinary_for_everyone_else()
    {
        var store = Seeded();
        var other = store.RegisterUser(new AppUser { Name = "Other", Username = "otheruser", Password = "x", Email = "o@example.com" });

        Assert.Null(store.SetUsername(other.Id, "renamed99"));
        Assert.Equal("renamed99", store.GetUser(other.Id)!.Username);
    }
}

using Phonix.Api.Data;
using Xunit;

namespace Phonix.Api.Tests;

// Verify/reset-password links used to live in an in-memory ConcurrentDictionary, so any restart or redeploy
// between "email sent" and "link clicked" silently invalidated every outstanding link. These tests pin the
// SQLite-backed replacement: the token must survive a restart, be usable exactly once, and honor its purpose
// and expiry.
public class TokenPersistenceTests
{
    [Fact]
    public void Token_survives_a_restart()
    {
        var store = TestStore.Create(out var dbPath);
        var token = store.CreateToken(1, "verify", TimeSpan.FromHours(1));

        var reopened = TestStore.Reopen(dbPath);
        Assert.Equal(1, reopened.ConsumeToken(token, "verify"));
    }

    [Fact]
    public void Token_can_only_be_consumed_once()
    {
        var store = TestStore.Create();
        var token = store.CreateToken(1, "reset", TimeSpan.FromHours(1));

        Assert.Equal(1, store.ConsumeToken(token, "reset"));
        Assert.Null(store.ConsumeToken(token, "reset"));
    }

    [Fact]
    public void Token_rejects_the_wrong_purpose()
    {
        var store = TestStore.Create();
        var token = store.CreateToken(1, "verify", TimeSpan.FromHours(1));

        Assert.Null(store.ConsumeToken(token, "reset"));
    }

    [Fact]
    public void Expired_token_is_rejected()
    {
        var store = TestStore.Create();
        var token = store.CreateToken(1, "reset", TimeSpan.FromSeconds(-1));

        Assert.Null(store.ConsumeToken(token, "reset"));
    }

    [Fact]
    public void Unknown_token_is_rejected()
    {
        var store = TestStore.Create();
        Assert.Null(store.ConsumeToken("does-not-exist", "verify"));
    }

    // The DELETE that consumes a token commits inside the same WriteTx as everything else — a restart must
    // not resurrect a row that was already removed, the mirror image of "unconsumed tokens survive a restart".
    [Fact]
    public void A_consumed_token_stays_dead_after_a_restart()
    {
        var store = TestStore.Create(out var dbPath);
        var token = store.CreateToken(1, "reset", TimeSpan.FromHours(1));
        Assert.Equal(1, store.ConsumeToken(token, "reset")); // used once, row deleted

        var reopened = TestStore.Reopen(dbPath);
        Assert.Null(reopened.ConsumeToken(token, "reset")); // still gone — a restart is not a second chance
    }

    // An expired-but-never-clicked token just sits in the table until the next CreateToken's opportunistic
    // sweep; a restart in that window must not reset or reinterpret its ExpiresAt.
    [Fact]
    public void An_expired_token_stays_dead_after_a_restart()
    {
        var store = TestStore.Create(out var dbPath);
        var token = store.CreateToken(1, "reset", TimeSpan.FromSeconds(-1)); // already expired, never consumed

        var reopened = TestStore.Reopen(dbPath);
        Assert.Null(reopened.ConsumeToken(token, "reset"));
    }
}

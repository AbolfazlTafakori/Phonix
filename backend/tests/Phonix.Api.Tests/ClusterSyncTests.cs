using System.Text.Json;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Xunit;

namespace Phonix.Api.Tests;

// Covers the sync-outbox mechanism directly on SqliteDataStore: the part every other cluster piece
// (ClusterSyncService's pull loop, the write-gate, promote/demote) is built on top of. HTTP-dependent
// behavior (the pull loop itself, auto-failover timing, the promote/demote handshake) is exercised by the
// manual two-container run in the implementation plan rather than here.
public class ClusterSyncTests
{
    private static SqliteDataStore FreshStore(bool clusterEnabled)
    {
        var dir = Path.Combine(Path.GetTempPath(), "phonix-sqlite-tests");
        Directory.CreateDirectory(dir);
        return new SqliteDataStore(Path.Combine(dir, Guid.NewGuid() + ".db"), clusterEnabled);
    }

    [Fact]
    public void A_write_appends_to_the_outbox_only_when_clustering_is_enabled()
    {
        var off = FreshStore(clusterEnabled: false);
        off.AddProduct(new Product { Name = "P", CategoryId = 1, Price = 1000, Stock = 1, IsActive = true });
        Assert.Empty(off.GetOutboxSince(0));

        var on = FreshStore(clusterEnabled: true);
        var product = on.AddProduct(new Product { Name = "P2", CategoryId = 1, Price = 1000, Stock = 1, IsActive = true });
        var entries = on.GetOutboxSince(0);
        Assert.Single(entries);
        Assert.Equal("Products", entries[0].EntityTable);
        Assert.Equal(product.Id, entries[0].EntityId);
        Assert.Equal("Upsert", entries[0].Op);
    }

    [Fact]
    public void Deleting_a_row_appends_a_delete_entry_with_no_payload()
    {
        var store = FreshStore(clusterEnabled: true);
        var cat = store.AddCategory(new Category { Name = "C" });
        var cursorBeforeDelete = store.GetOutboxHighWaterMark();

        Assert.True(store.DeleteCategory(cat.Id));

        var entries = store.GetOutboxSince(cursorBeforeDelete);
        Assert.Single(entries);
        Assert.Equal("Delete", entries[0].Op);
        Assert.Equal(cat.Id, entries[0].EntityId);
        Assert.Null(entries[0].DataJson);
    }

    [Fact]
    public void ApplyRemoteOp_replicates_a_new_row_verbatim_and_never_re_appends_to_its_own_outbox()
    {
        var origin = FreshStore(clusterEnabled: true);
        var product = origin.AddProduct(new Product { Name = "Remote", CategoryId = 1, Price = 1000, Stock = 2, IsActive = true });
        var entry = Assert.Single(origin.GetOutboxSince(0));

        var peer = FreshStore(clusterEnabled: true);
        Assert.True(peer.ApplyRemoteOp(entry));

        var replicated = peer.GetProduct(product.Id);
        Assert.NotNull(replicated);
        Assert.Equal("Remote", replicated!.Name);
        // Applying a row pulled from a peer must never create a new outbox entry of its own — otherwise the
        // two nodes would ping-pong the same change back and forth forever.
        Assert.Empty(peer.GetOutboxSince(0));
    }

    [Fact]
    public void ApplyRemoteOp_skips_a_stale_write_when_the_local_copy_is_already_newer()
    {
        var store = FreshStore(clusterEnabled: true);
        var product = store.AddProduct(new Product { Name = "Local Latest", CategoryId = 1, Price = 1000, Stock = 1, IsActive = true });
        // The insert above just stamped SyncRowVersion with "now". Simulate a remote write for the SAME row
        // timestamped well before that — last-writer-wins must skip it, not overwrite the newer local data.
        var staleEntry = new SyncOutboxEntry(999, "Products", product.Id, "Upsert",
            JsonSerializer.Serialize(new { Id = product.Id, Name = "Stale Remote", CategoryId = 1, Price = 1, Stock = 1, IsActive = true }),
            "2000-01-01T00:00:00.0000000Z");

        var applied = store.ApplyRemoteOp(staleEntry);

        Assert.True(applied); // the cursor still advances — this isn't a failure, just a no-op write
        Assert.Equal("Local Latest", store.GetProduct(product.Id)!.Name);
    }

    [Fact]
    public void ApplyRemoteOp_rejects_a_table_name_outside_the_synced_allowlist()
    {
        var store = FreshStore(clusterEnabled: true);
        var entry = new SyncOutboxEntry(1, "Users; DROP TABLE Users;--", 1, "Delete", null, DateTime.UtcNow.ToString("o"));
        Assert.False(store.ApplyRemoteOp(entry));
    }

    [Fact]
    public void BumpAutoincrementOffset_keeps_every_later_local_insert_strictly_above_the_offset()
    {
        var store = FreshStore(clusterEnabled: true);
        store.AddCategory(new Category { Name = "Seed" });

        store.BumpAutoincrementOffset(1_000_000_000);
        var afterBump = store.AddCategory(new Category { Name = "AfterBump" });

        Assert.True(afterBump.Id > 1_000_000_000);
    }

    [Fact]
    public void ClusterState_roundtrips_through_the_singletons_table()
    {
        var store = FreshStore(clusterEnabled: true);
        Assert.Equal(ClusterRole.Standalone, store.GetClusterState().Role); // default before any seeding

        var state = store.GetClusterState();
        state.Role = ClusterRole.Primary;
        state.LastAppliedCursor = 42;
        store.SetClusterState(state);

        var reloaded = store.GetClusterState();
        Assert.Equal(ClusterRole.Primary, reloaded.Role);
        Assert.Equal(42, reloaded.LastAppliedCursor);
    }

    [Fact]
    public void Reseeding_the_outbox_produces_exactly_one_upsert_per_existing_row()
    {
        var store = FreshStore(clusterEnabled: true);
        store.AddCategory(new Category { Name = "A" });
        store.AddCategory(new Category { Name = "B" });

        var seeded = store.ReseedOutboxFromCurrentState();

        var entries = store.GetOutboxSince(0);
        Assert.Equal(seeded, entries.Count);
        Assert.Equal(2, entries.Count); // a fresh temp-file store starts empty — only the 2 categories exist
        Assert.All(entries, e => Assert.Equal("Upsert", e.Op));
    }

    // ── Fix 1: id-collision protection is actually EXECUTED (EnsureStandbyIdBand), and concurrent writes on
    // two partitioned nodes never mint the same id. ──────────────────────────────────────────────────────
    [Fact]
    public void EnsureStandbyIdBand_is_applied_once_and_is_idempotent_across_restarts()
    {
        var store = FreshStore(clusterEnabled: true);
        var state = store.GetClusterState();
        state.Role = ClusterRole.Standby;
        store.SetClusterState(state);

        Assert.True(store.EnsureStandbyIdBand());  // first call performs the bump
        Assert.True(store.GetClusterState().IdBandApplied);
        Assert.False(store.EnsureStandbyIdBand()); // second call is a no-op — never double-applied

        var afterBump = store.AddCategory(new Category { Name = "Post" });
        Assert.True(afterBump.Id > SqliteDataStore.StandbyIdBandOffset);
    }

    [Fact]
    public void Concurrent_writes_on_a_partitioned_primary_and_standby_never_collide_on_id()
    {
        // Primary keeps the base id band; Standby reserves the high band (as it would the moment it becomes
        // Standby). During a simulated partition BOTH accept writes — the disjoint bands guarantee no overlap.
        var primary = FreshStore(clusterEnabled: true);
        var standby = FreshStore(clusterEnabled: true);
        var sbState = standby.GetClusterState();
        sbState.Role = ClusterRole.Standby;
        standby.SetClusterState(sbState);
        Assert.True(standby.EnsureStandbyIdBand());

        var primaryIds = new HashSet<int>();
        var standbyIds = new HashSet<int>();
        for (var i = 0; i < 50; i++)
        {
            primaryIds.Add(primary.AddProduct(new Product { Name = $"P{i}", CategoryId = 1, Price = 1, Stock = 1, IsActive = true }).Id);
            standbyIds.Add(standby.AddProduct(new Product { Name = $"S{i}", CategoryId = 1, Price = 1, Stock = 1, IsActive = true }).Id);
        }

        Assert.False(primaryIds.Overlaps(standbyIds)); // zero collisions across the partition
        Assert.All(standbyIds, id => Assert.True(id > SqliteDataStore.StandbyIdBandOffset));
    }

    // ── Fix 3: a fresh Standby bootstraps from an already-populated Primary snapshot. ─────────────────────
    [Fact]
    public void RestoreFromPeerSnapshot_seeds_a_fresh_standby_and_pins_the_cursor()
    {
        var primary = FreshStore(clusterEnabled: true);
        primary.AddCategory(new Category { Name = "Cat" });
        primary.AddProduct(new Product { Name = "Existing", CategoryId = 1, Price = 5, Stock = 3, IsActive = true });
        primary.RegisterUser(new AppUser { Username = "owner", Email = "o@x.io" });
        var snapshot = primary.CaptureSnapshot();
        var primaryHwm = primary.GetOutboxHighWaterMark();

        var standby = FreshStore(clusterEnabled: true); // brand new, empty (the Iran-server scenario)
        Assert.True(standby.IsEmpty());

        standby.RestoreFromPeerSnapshot(snapshot, primaryHwm);

        Assert.False(standby.IsEmpty()); // now carries the Primary's users/products
        Assert.Single(standby.GetProducts());
        Assert.Equal("Existing", standby.GetProducts().First().Name);
        var st = standby.GetClusterState();
        Assert.Equal(primaryHwm, st.LastAppliedCursor); // won't re-pull what the snapshot already contains
        Assert.NotNull(st.BootstrappedAtUtc);
        // The receiving side must NOT create outbox rows for the restored data (else it would push it back).
        Assert.Empty(standby.GetOutboxSince(0));
    }

    // ── Fix 5: a single poison event is dead-lettered, retried, and never wedges the cursor. ──────────────
    [Fact]
    public void Dead_letter_queue_tracks_retries_and_clears_on_success()
    {
        var store = FreshStore(clusterEnabled: true);
        var entry = new SyncOutboxEntry(77, "Products", 5, "Upsert", "{}", DateTime.UtcNow.ToString("o"));

        store.RecordSyncFailure(entry, "boom");
        store.RecordSyncFailure(entry, "boom again");
        Assert.Equal(1, store.GetDeadLetterCount()); // same OutboxId → one row, retry counter bumped

        var retryable = store.GetRetryableDeadLetters(maxRetries: 5);
        Assert.Single(retryable);
        Assert.Equal(77, retryable[0].Id);

        // Once the retry count reaches the cap it stays parked but is no longer offered for retry.
        Assert.Empty(store.GetRetryableDeadLetters(maxRetries: 2));
        Assert.Equal(1, store.GetDeadLetterCount());

        store.ClearSyncFailure(77);
        Assert.Equal(0, store.GetDeadLetterCount());
    }
}

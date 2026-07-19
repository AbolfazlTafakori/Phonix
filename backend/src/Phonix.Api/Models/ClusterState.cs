namespace Phonix.Api.Models;

public enum ClusterRole
{
    Standalone,
    Primary,
    Standby,
    Recovering,
}

// Live cluster role and its history, persisted as a Singletons row (see SqliteDataStore's ClusterKey) so a
// role change survives a restart. PHONIX_CLUSTER_MODE only seeds this the first time it doesn't exist yet —
// afterward this row, not the env var, is the source of truth (the same relationship PHONIX_OWNER_USERNAME/
// PASSWORD has to the live owner account).
public class ClusterState
{
    public ClusterRole Role { get; set; } = ClusterRole.Standalone;
    // Set the moment this node auto-promotes itself (unattended failover). Null means this node's Primary
    // status is "legitimate" — seeded at install time or reached through an explicit manual promote — which
    // is exactly the signal a node uses to know IT must self-demote (never the auto-failover side) the
    // moment it discovers its peer also claims Primary. Cleared by a manual promote.
    public DateTime? LastFailoverAtUtc { get; set; }
    public DateTime? LastPromotedAtUtc { get; set; }
    public DateTime? LastDemotedAtUtc { get; set; }
    // The highest peer SyncOutbox.Id this node has already applied — the whole incremental-sync cursor.
    public long LastAppliedCursor { get; set; }
    // True once this node has reserved its disjoint autoincrement id band (see BumpAutoincrementOffset). Only
    // a Standby ever does this, and only once — the flag makes the bump idempotent across restarts so it is
    // never applied twice (which would push ids into a second, overlapping band). Set the moment a node first
    // becomes Standby (startup seed or peer-requested demote).
    public bool IdBandApplied { get; set; }
    // Set once a fresh Standby has successfully pulled and restored the Primary's initial snapshot (bootstrap).
    // Distinguishes "empty because brand new, still needs its first full sync" from "legitimately empty".
    public DateTime? BootstrappedAtUtc { get; set; }

    // Identifies the current lineage of this node's data. Incremental sync is a cursor over an append-only
    // outbox, which only ever describes CHANGES — so a wholesale replacement of the data (a full or section
    // restore) is invisible to it: rows the restore dropped were never written as deletes, and the peer keeps
    // them forever while every health signal reads clean. Rotating this on any such replacement gives the peer
    // a way to notice its cursor now points into a different lineage and re-bootstrap instead of silently
    // diverging. Null on a node that has never been restored.
    public string? DataEpoch { get; set; }

    // The peer DataEpoch this node's data was last aligned to (set when a snapshot is restored from the peer).
    // A pull that reports a different epoch means the peer's data was replaced underneath this node.
    public string? PeerDataEpoch { get; set; }
}

// A Standby attaching to an already-populated Primary pulls one of these once, restores it wholesale, then
// switches to ordinary incremental pulls starting at HighWaterMark. SnapshotJson is the SAME StoreSnapshot
// wire format the backup/restore flow already uses — no new serialization surface.
public sealed record ClusterSnapshotResponse(string SnapshotJson, long HighWaterMark, string? DataEpoch = null);

// One media file as advertised by the Primary's manifest: its category folder, opaque filename, size and
// SHA-256. The Standby downloads only the files it is missing or whose checksum differs, and never deletes.
public sealed record ClusterMediaEntry(string Category, string Name, long Size, string Sha256);

public sealed record ClusterMediaManifest(IReadOnlyList<ClusterMediaEntry> Files);

// Node-to-node request body for pulling a single media file (POST so the body — not a query string — is what
// the HMAC signs, exactly like every other cluster call).
public sealed record ClusterMediaFileInput(string Category, string Name);

// One row of the outbox: a single entity write, in the order it happened locally. The peer asks for every
// row with Id greater than its own last-applied cursor — that comparison IS the whole incremental-sync
// protocol (see ClusterSyncService).
public sealed record SyncOutboxEntry(long Id, string EntityTable, long EntityId, string Op, string? DataJson, string CreatedAtUtc);

// The node-to-node pull response: the caller's new outbox entries, plus enough of THIS node's own state
// (role, high-water mark) for the caller to detect a live Primary/Primary conflict and know how far behind
// it still is — without either side needing a second round-trip.
public sealed record ClusterSyncPullResponse(IReadOnlyList<SyncOutboxEntry> Entries, string Role, long HighWaterMark, string? DataEpoch = null);

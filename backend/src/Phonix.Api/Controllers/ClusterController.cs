using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Models;
using Phonix.Api.Security;
using Phonix.Api.Services;

namespace Phonix.Api.Controllers;

// What the Cluster Management admin page renders: current role, peer reachability/last contact, sync
// progress, and health, all read lock-free off the live ClusterSyncService instance.
public record ClusterStatusDto(
    string Role, bool ClusterEnabled, string NodeId, string? PeerUrl, bool PeerReachable,
    DateTime? LastSyncUtc, DateTime? LastPeerContactUtc, long PendingCount, long DeadLetterCount);

public record ClusterPullInput(long Since);

[ApiController]
[Route("api/cluster")]
public class ClusterController : ControllerBase
{
    private readonly IClusterSyncService _cluster;
    public ClusterController(IClusterSyncService cluster) => _cluster = cluster;

    // ── Admin-facing: the Cluster Management page ───────────────────────────────────────────────────────
    [Authorize(Roles = nameof(UserRole.Admin))]
    [HttpGet("status")]
    public ClusterStatusDto Status() => new(
        _cluster.Role.ToString(), _cluster.Role != ClusterRole.Standalone, _cluster.NodeId, _cluster.PeerUrl,
        _cluster.PeerReachable, _cluster.LastSyncUtc, _cluster.LastPeerContactUtc, _cluster.PendingCount, _cluster.DeadLetterCount);

    [Authorize(Roles = nameof(UserRole.Admin))]
    [HttpPost("promote")]
    public async Task<IActionResult> Promote()
    {
        var (ok, error) = await _cluster.PromoteAsync();
        return ok ? Ok() : BadRequest(error);
    }

    [Authorize(Roles = nameof(UserRole.Admin))]
    [HttpPost("recover")]
    public async Task<IActionResult> Recover()
    {
        var (ok, error) = await _cluster.StartRecoveryAsync();
        return ok ? Ok() : BadRequest(error);
    }

    [Authorize(Roles = nameof(UserRole.Admin))]
    [HttpPost("resync")]
    public async Task<IActionResult> Resync()
    {
        var (ok, error) = await _cluster.ResyncNowAsync();
        return ok ? Ok() : BadRequest(error);
    }

    // Admin-triggered initial sync: attach this fresh Standby to an already-populated Primary (Fix 3).
    [Authorize(Roles = nameof(UserRole.Admin))]
    [HttpPost("bootstrap")]
    public async Task<IActionResult> Bootstrap()
    {
        var (ok, error) = await _cluster.BootstrapFromPrimaryAsync();
        return ok ? Ok() : BadRequest(error);
    }

    // ── Node-to-node: the peer calling THIS server, authenticated by PHONIX_CLUSTER_SECRET, never by a
    // staff session. 404s entirely when clustering isn't configured (ClusterPeerAuthAttribute) — a
    // standalone install exposes nothing new here. ──────────────────────────────────────────────────────
    [ClusterPeerAuth]
    [HttpPost("sync/pull")]
    public ClusterSyncPullResponse Pull(ClusterPullInput input) => _cluster.HandlePull(input.Since);

    [ClusterPeerAuth]
    [HttpPost("sync/demote")]
    public IActionResult Demote()
    {
        _cluster.HandleDemote();
        return Ok();
    }

    // Fix 3: a fresh Standby pulls one full snapshot here to seed itself before switching to incremental pulls.
    [ClusterPeerAuth]
    [HttpPost("sync/snapshot")]
    public ClusterSnapshotResponse Snapshot() => _cluster.HandleSnapshotRequest();

    // Fix 4: the media manifest (every file + checksum) and single-file transfer, both peer-authenticated.
    [ClusterPeerAuth]
    [HttpPost("media/manifest")]
    public ClusterMediaManifest MediaManifest() => _cluster.HandleMediaManifest();

    [ClusterPeerAuth]
    [HttpPost("media/file")]
    public IActionResult MediaFile(ClusterMediaFileInput input)
    {
        var bytes = _cluster.HandleMediaFile(input.Category, input.Name);
        return bytes is null ? NotFound() : File(bytes, "application/octet-stream");
    }
}

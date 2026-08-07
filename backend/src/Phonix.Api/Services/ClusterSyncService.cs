using System.Text;
using System.Text.Json;
using Phonix.Api.Data;
using Phonix.Api.Models;

namespace Phonix.Api.Services;

// Business-continuity clustering: one Primary, one Standby, incremental outbox-based sync between them (see
// SqliteDataStore.Cluster.cs — this service never touches business tables directly, only the outbox/apply
// surface). Registered the same dual way as UsdRateService/ServerMetricsCollector: a singleton the
// ClusterController reads live state from, ALSO run as the hosted background loop. Entirely inert
// (ExecuteAsync returns immediately) unless PHONIX_CLUSTER_MODE is "primary" or "standby".
public interface IClusterSyncService
{
    ClusterRole Role { get; }
    string NodeId { get; }
    string? PeerUrl { get; }
    DateTime? LastSyncUtc { get; }
    DateTime? LastPeerContactUtc { get; }
    long PendingCount { get; }
    bool PeerReachable { get; }
    long DeadLetterCount { get; }

    // Full persisted state (failover/promote/demote history, data epoch, id-band flag) for the detailed
    // report on the admin panel — read straight from the store, same as DeadLetterCount above.
    ClusterState GetStateSnapshot();
    IReadOnlyList<ClusterEvent> RecentEvents { get; }

    // Admin-triggered actions (see ClusterController). Each returns (ok, error) rather than throwing —
    // these are ordinary "the operator clicked a button" outcomes, not exceptional failures.
    Task<(bool Ok, string? Error)> PromoteAsync();
    Task<(bool Ok, string? Error)> StartRecoveryAsync();
    Task<(bool Ok, string? Error)> ResyncNowAsync();
    Task<(bool Ok, string? Error)> BootstrapFromPrimaryAsync();
    // Admin-panel config apply: enable clustering (mode) and/or update peer URL / rotate the HMAC secret,
    // live, without a restart. `mode` is only honored while this node is still Standalone.
    Task<(bool Ok, string? Error)> UpdateConfigAsync(string? mode, string? peerUrl, string? secret);
    // Manual override for a node whose role was set wrong (e.g. meant to be Primary but was configured as
    // Standby): sets Role directly, live, WITHOUT the promote/demote peer handshake PromoteAsync/HandleDemote
    // use. Deliberately unsafe if misused — the caller (admin panel) must warn about split-brain risk before
    // calling this, since unlike Promote it never confirms the peer isn't also Primary.
    Task<(bool Ok, string? Error)> ForceSetRoleAsync(string? mode);

    // Called by ClusterPeerAuthAttribute the moment any node-to-node request passes HMAC verification. Only
    // the peer holds the secret, so a verified request is proof the peer was alive and able to reach this
    // node — a liveness signal that survives a link which is blocked in one direction only.
    void NotePeerInboundContact();

    // Node-to-node actions (see ClusterController's HMAC-gated routes).
    ClusterSyncPullResponse HandlePull(long since);
    void HandleDemote();
    ClusterSnapshotResponse HandleSnapshotRequest();
    ClusterMediaManifest HandleMediaManifest();
    byte[]? HandleMediaFile(string category, string name);
}

public sealed class ClusterSyncService : BackgroundService, IClusterSyncService
{
    private readonly SqliteDataStore _store;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITelegramAlertSender _alerts;
    private readonly IFileStorageService _files;
    private readonly ILogger<ClusterSyncService> _logger;

    // Mutable now: an admin can enable clustering, change the peer URL, or rotate the secret live from the
    // panel (UpdateConfigAsync), so these can no longer be readonly-from-env-at-boot.
    private volatile bool _clusterEnabled;
    private volatile string? _configuredPeerUrl;
    private readonly string _nodeId;
    private readonly ClusterRole? _seedRole;
    private readonly int _syncIntervalSeconds;
    private readonly int _failoverGraceSeconds;
    private readonly int _mediaSyncIntervalSeconds;
    private readonly bool _allowInsecurePeer;
    private readonly string[] _witnessUrls;

    // A dead-lettered event is retried this many times before it is left permanently parked (and surfaced in
    // the cluster status) rather than retried forever — one poison event must never wedge the whole cluster.
    private const int MaxDeadLetterRetries = 5;
    // The snapshot is the whole database and a media file can be several megabytes; both cross the same link
    // the 7-second control calls use, so they get their own budget rather than the control-call deadline.
    private static readonly TimeSpan BulkTransferTimeout = TimeSpan.FromMinutes(10);
    private const long StandbyIdBandOffset = SqliteDataStore.StandbyIdBandOffset;

    private long _lastMediaSyncTicks;
    private long _lastWitnessOkTicks;
    private long _lastInboundPeerContactTicks;

    // Rolling in-memory diagnostic trail for the admin panel's "log رویدادها" section (GET /api/cluster/events).
    // Bounded so a flapping peer can never grow this unbounded; deliberately not persisted (see ClusterEvent).
    private const int MaxRecentEvents = 50;
    private readonly object _eventsLock = new();
    private readonly LinkedList<ClusterEvent> _events = new();

    private void RecordEvent(string level, string message)
    {
        lock (_eventsLock)
        {
            _events.AddFirst(new ClusterEvent(DateTime.UtcNow, level, message));
            while (_events.Count > MaxRecentEvents) _events.RemoveLast();
        }
    }

    public IReadOnlyList<ClusterEvent> RecentEvents
    {
        get { lock (_eventsLock) return _events.ToList(); }
    }

    // Lock-free published state: the write-gate middleware reads Role on every mutating request, so it must
    // never wait on a SQLite round-trip. Longs carry ticks/enum-as-int via Interlocked; a plain lock guards
    // the rare read-modify-write against the store (promote/demote/failover), which already serializes
    // through SQLite's own IMMEDIATE transaction underneath.
    private long _roleValue;
    private long _lastSyncTicks;
    private long _lastPeerContactTicks;
    private long _pendingCount;
    private int _consecutiveFailures;
    private readonly object _transitionLock = new();

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public ClusterSyncService(SqliteDataStore store, IHttpClientFactory httpClientFactory,
        ITelegramAlertSender alerts, IFileStorageService files, ILogger<ClusterSyncService> logger)
    {
        _store = store;
        _httpClientFactory = httpClientFactory;
        _alerts = alerts;
        _files = files;
        _logger = logger;

        var mode = Environment.GetEnvironmentVariable("PHONIX_CLUSTER_MODE")?.Trim().ToLowerInvariant();
        _clusterEnabled = mode is "primary" or "standby";
        _seedRole = mode switch { "primary" => ClusterRole.Primary, "standby" => ClusterRole.Standby, _ => null };
        _configuredPeerUrl = Environment.GetEnvironmentVariable("PHONIX_CLUSTER_PEER")?.TrimEnd('/');
        _nodeId = Environment.GetEnvironmentVariable("PHONIX_NODE_ID")?.Trim() ?? "";
        _syncIntervalSeconds = int.TryParse(Environment.GetEnvironmentVariable("PHONIX_CLUSTER_SYNC_INTERVAL_SECONDS"), out var si) && si > 0 ? si : 7;
        _failoverGraceSeconds = int.TryParse(Environment.GetEnvironmentVariable("PHONIX_CLUSTER_FAILOVER_GRACE_SECONDS"), out var fg) && fg > 0 ? fg : 90;
        _mediaSyncIntervalSeconds = int.TryParse(Environment.GetEnvironmentVariable("PHONIX_CLUSTER_MEDIA_SYNC_INTERVAL_SECONDS"), out var ms) && ms > 0 ? ms : 60;
        // Escape hatch for local/dev two-node testing over plain HTTP. Production MUST leave this unset — the
        // startup guard (ValidatePeerTransport) refuses to run a cluster over unencrypted HTTP otherwise.
        _allowInsecurePeer = string.Equals(Environment.GetEnvironmentVariable("PHONIX_CLUSTER_ALLOW_INSECURE"), "true", StringComparison.OrdinalIgnoreCase);
        // Independent reference points used to tell "the peer died" apart from "I lost my own connectivity".
        _witnessUrls = (Environment.GetEnvironmentVariable("PHONIX_CLUSTER_WITNESS_URLS")
                ?? "https://cloudflare.com/cdn-cgi/trace,https://www.google.com/generate_204")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Never treat "just booted, haven't contacted the peer yet" as "the peer has been down for ages".
        _lastPeerContactTicks = DateTime.UtcNow.Ticks;
    }

    // Fix 6: reject a plaintext-HTTP peer before the cluster ever talks to it. HMAC authenticates the peer but
    // does nothing for confidentiality — outbox payloads (orders, user data) and the initial snapshot travel
    // over this link, so the transport itself must be TLS. Throws a clear, actionable error at startup (which
    // aborts host boot) rather than silently syncing sensitive data in the clear.
    private void ValidatePeerTransport()
    {
        if (string.IsNullOrWhiteSpace(_configuredPeerUrl)) return;
        if (!Uri.TryCreate(_configuredPeerUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"PHONIX_CLUSTER_PEER ('{_configuredPeerUrl}') is not a valid absolute URL.");
        if (uri.Scheme == Uri.UriSchemeHttps) return;
        if (_allowInsecurePeer)
        {
            _logger.LogWarning("PHONIX_CLUSTER_PEER uses plain HTTP and PHONIX_CLUSTER_ALLOW_INSECURE=true — cluster traffic is UNENCRYPTED. Never do this in production.");
            return;
        }
        throw new InvalidOperationException(
            $"PHONIX_CLUSTER_PEER must use HTTPS in production (got '{uri.Scheme}://…'). Cluster sync carries orders, user data and the initial snapshot, so the link must be encrypted. " +
            "Use an https:// peer URL (a TLS reverse proxy or VPN-fronted endpoint). For local testing only, set PHONIX_CLUSTER_ALLOW_INSECURE=true.");
    }

    // Runs before the background loop and aborts host startup on a misconfiguration (Fix 6 transport guard).
    // Only applies to an env-configured peer at boot — a peer URL set later from the admin panel is validated
    // synchronously inside UpdateConfigAsync instead, since by then the host is already up.
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        if (_clusterEnabled) ValidatePeerTransport();
        return base.StartAsync(cancellationToken);
    }

    public ClusterRole Role => (ClusterRole)Interlocked.Read(ref _roleValue);
    public string NodeId => _nodeId;
    public string? PeerUrl => _configuredPeerUrl;
    public DateTime? LastSyncUtc => Interlocked.Read(ref _lastSyncTicks) is var t && t > 0 ? new DateTime(t, DateTimeKind.Utc) : null;
    public DateTime? LastPeerContactUtc => new DateTime(Interlocked.Read(ref _lastPeerContactTicks), DateTimeKind.Utc);
    public long PendingCount => Interlocked.Read(ref _pendingCount);
    public bool PeerReachable => Interlocked.CompareExchange(ref _consecutiveFailures, 0, 0) == 0;
    public long DeadLetterCount => _clusterEnabled ? _store.GetDeadLetterCount() : 0;
    public ClusterState GetStateSnapshot() => _store.GetClusterState();

    private void SetRoleCache(ClusterRole role) => Interlocked.Exchange(ref _roleValue, (long)role);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Persisted admin-panel overrides (UpdateConfigAsync) always take over from the env vars here on, so
        // a config set from the panel survives a restart the same way a promote/demote already does.
        var initialState = _store.GetClusterState();
        if (!string.IsNullOrWhiteSpace(initialState.PeerUrl)) _configuredPeerUrl = initialState.PeerUrl;
        if (!string.IsNullOrWhiteSpace(initialState.Secret)) ClusterAuth.SetRuntimeSecret(initialState.Secret);
        if (initialState.Role != ClusterRole.Standalone) _clusterEnabled = true;

        // Loops forever (rather than returning early on Standalone) so that a live UpdateConfigAsync call can
        // flip _clusterEnabled to true later and have this same loop pick clustering up without a restart.
        var initialized = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_clusterEnabled)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(_syncIntervalSeconds), stoppingToken); }
                catch (OperationCanceledException) { }
                continue;
            }

            if (!initialized)
            {
                initialized = true;
                var state = _store.GetClusterState();
                if (state.Role == ClusterRole.Standalone && _seedRole is { } seed)
                {
                    // First boot with clustering configured: seed once from the env var. Every transition after
                    // this is driven by this service (auto-failover) or the admin panel (promote/recover/config),
                    // never the env var again — exactly like PHONIX_OWNER_USERNAME/PASSWORD only seed the owner.
                    state.Role = seed;
                    _store.SetClusterState(state);
                }
                SetRoleCache(state.Role);

                // Fix 1: a Standby MUST reserve its disjoint id band before it can accept (or replay) any write,
                // so its inserts can never collide with the Primary's during a partition. Applied once, ever.
                if (state.Role == ClusterRole.Standby && _store.EnsureStandbyIdBand())
                    _logger.LogInformation("Standby id band reserved (autoincrement offset +{Offset}).", StandbyIdBandOffset);

                // Fix 3: a fresh Standby attaching to an already-populated Primary can't converge from the
                // incremental outbox alone — it pulls one full snapshot first. Auto-runs when this node is a
                // never-bootstrapped Standby with a peer configured; already-bootstrapped nodes skip straight
                // to incremental sync.
                if (state.Role == ClusterRole.Standby && state.BootstrappedAtUtc is null
                    && !string.IsNullOrWhiteSpace(_configuredPeerUrl))
                {
                    var (ok, err) = await BootstrapFromPrimaryAsync();
                    if (!ok) _logger.LogWarning("Initial Standby bootstrap from Primary did not complete: {Error}", err);
                }
            }

            try { await SyncOnceAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogWarning(ex, "Cluster sync cycle failed"); }
            try { await SyncMediaIfDueAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogWarning(ex, "Cluster media sync cycle failed"); }
            try { await Task.Delay(TimeSpan.FromSeconds(_syncIntervalSeconds), stoppingToken); }
            catch (OperationCanceledException) { }
        }
    }

    private async Task SyncOnceAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_configuredPeerUrl)) return;

        var state = _store.GetClusterState();
        var response = await CallPeerAsync<ClusterSyncPullResponse>(HttpMethod.Post, "/api/cluster/sync/pull",
            JsonSerializer.Serialize(new { since = state.LastAppliedCursor }, JsonOpts), ct);

        if (response is null)
        {
            await RecordContactFailureAsync(ct);
            return;
        }

        Interlocked.Exchange(ref _lastPeerContactTicks, DateTime.UtcNow.Ticks);
        Interlocked.Exchange(ref _consecutiveFailures, 0);

        // The peer's data was replaced wholesale (restored) since this node last aligned with it. Applying
        // its outbox from here would converge on the restored rows while keeping every row the restore
        // deleted — a divergence nothing else reports, because the cursor stays caught up and the dead-letter
        // queue stays empty. Take a fresh full snapshot instead; that is the only operation that also removes.
        if (state.Role is ClusterRole.Standby or ClusterRole.Recovering
            && response.DataEpoch is not null && state.PeerDataEpoch is not null
            && !string.Equals(response.DataEpoch, state.PeerDataEpoch, StringComparison.Ordinal))
        {
            _logger.LogWarning("Peer data was restored (epoch changed) — re-bootstrapping from its snapshot instead of replaying the outbox.");
            var (rebootstrapped, error) = await BootstrapFromPrimaryAsync();
            if (rebootstrapped)
            {
                _ = _alerts.SendAlertAsync("♻️ داده‌های سرور اصلی بازیابی شد — این سرور به‌طور خودکار همگام‌سازی کامل انجام داد.");
                return;
            }
            _logger.LogWarning("Re-bootstrap after peer restore failed: {Error}", error);
            return; // don't advance the cursor into a lineage this node no longer matches
        }

        // First observation of a peer epoch (fresh upgrade, or a node bootstrapped before epochs existed):
        // adopt it without re-syncing, so rolling this out doesn't trigger a snapshot on every node at once.
        if (response.DataEpoch is not null && state.PeerDataEpoch is null)
        {
            state.PeerDataEpoch = response.DataEpoch;
            _store.SetClusterState(state);
        }

        var cursor = state.LastAppliedCursor;
        foreach (var entry in response.Entries)
        {
            // Fix 5: isolate each event. A single poison entry (bad payload, transient constraint violation) is
            // parked in the dead-letter queue and retried later — the cursor STILL advances past it, so one bad
            // event can never wedge every future change behind it.
            try { _store.ApplyRemoteOp(entry); }
            catch (Exception ex)
            {
                _store.RecordSyncFailure(entry, ex.Message);
                _logger.LogWarning(ex, "Cluster sync: entry {OutboxId} ({Table}#{EntityId}) failed to apply — dead-lettered.",
                    entry.Id, entry.EntityTable, entry.EntityId);
                RecordEvent("error", $"رویداد #{entry.Id} ({entry.EntityTable}) اعمال نشد و به Dead-letter منتقل شد: {ex.Message}");
            }
            cursor = entry.Id;
        }
        if (response.Entries.Count > 0)
        {
            state.LastAppliedCursor = cursor;
            _store.SetClusterState(state);
        }
        Interlocked.Exchange(ref _lastSyncTicks, DateTime.UtcNow.Ticks);
        Interlocked.Exchange(ref _pendingCount, Math.Max(0, response.HighWaterMark - cursor));

        RetryDeadLetters();

        // Live split-brain detection: I claim Primary AND my peer claims Primary too. Only the side whose
        // Primary status is "legitimate" (never auto-failed-over) self-demotes — the auto-failover side must
        // keep serving uninterrupted, per the automatic-failover/manual-failback requirement.
        if (state.Role == ClusterRole.Primary && response.Role == nameof(ClusterRole.Primary) && state.LastFailoverAtUtc is null)
        {
            lock (_transitionLock)
            {
                state = _store.GetClusterState();
                if (state.Role != ClusterRole.Primary) return;
                state.Role = ClusterRole.Recovering;
                state.LastDemotedAtUtc = DateTime.UtcNow;
                _store.SetClusterState(state);
                SetRoleCache(ClusterRole.Recovering);
            }
            _logger.LogWarning(
                "Cluster conflict: both nodes claim Primary — demoting this node to Recovering. This node always loses that argument (its Primary status never came from a failover), so re-promoting it while the peer still claims Primary will just repeat this. Fix the peer's role instead.");
            RecordEvent("warning",
                "تداخل خوشه: هر دو سرور Primary اعلام شدند — این سرور به Recovering منتقل شد. توجه: در این تداخل همیشه همین سرور عقب می‌نشیند، پس ترفیع دوباره‌ی آن تا وقتی سرور مقابل هم Primary است نتیجه‌ای ندارد و همین چرخه تکرار می‌شود؛ نقش سرور مقابل را اصلاح کنید.");
            _ = _alerts.SendAlertAsync("⚠️ هر دو سرور خوشه Primary اعلام شدند — این سرور به Recovering منتقل شد. تا وقتی نقش سرور مقابل اصلاح نشود، ترفیع دستی این سرور دوباره به همین حالت برمی‌گردد.");
        }
    }

    // Confirms this node still has working connectivity of its own, by reaching something unrelated to the
    // peer. Returns null when no witness has ever answered since boot — that means the guard itself is
    // unusable here (every witness blocked, no egress at all), and the caller must not let it stand in for a
    // real answer, or failover would be disabled forever on such a host.
    private async Task<bool?> HasOwnConnectivityAsync(CancellationToken ct)
    {
        if (_witnessUrls.Length == 0) return null;

        foreach (var url in _witnessUrls)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(8);
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                Interlocked.Exchange(ref _lastWitnessOkTicks, DateTime.UtcNow.Ticks);
                return true;
            }
            catch { /* try the next witness */ }
        }

        // Nothing answered. Only meaningful if a witness HAS answered at some point — otherwise this host
        // simply cannot reach any of them and the signal carries no information.
        return Interlocked.Read(ref _lastWitnessOkTicks) > 0 ? false : null;
    }

    public void NotePeerInboundContact() =>
        Interlocked.Exchange(ref _lastInboundPeerContactTicks, DateTime.UtcNow.Ticks);

    private async Task RecordContactFailureAsync(CancellationToken ct)
    {
        var failures = Interlocked.Increment(ref _consecutiveFailures);
        if (failures < 3) return; // ignore a single blip — a real outage keeps failing across several cycles

        var lastContact = new DateTime(Interlocked.Read(ref _lastPeerContactTicks), DateTimeKind.Utc);
        if (DateTime.UtcNow - lastContact < TimeSpan.FromSeconds(_failoverGraceSeconds)) return;

        // Outbound silence alone does not mean the peer is down: on a link that is filtered in ONE direction
        // (this node cannot reach the peer, while the peer still reaches this node fine) the peer keeps
        // serving customers the whole time. Promoting here produces two Primaries, and the peer — whose own
        // Primary status is the legitimate, never-failed-over one — then demotes itself, so the cluster
        // oscillates for as long as the filtering lasts. A verified inbound peer request is positive proof
        // the peer is alive, and only the peer can produce one (it is HMAC-signed with the shared secret),
        // so it outranks our own inability to call out. The witness check below cannot see this case at all:
        // it asks "is MY internet down", and on a one-way block the answer is a truthful "no".
        var lastInbound = new DateTime(Interlocked.Read(ref _lastInboundPeerContactTicks), DateTimeKind.Utc);
        if (DateTime.UtcNow - lastInbound < TimeSpan.FromSeconds(_failoverGraceSeconds))
        {
            _logger.LogWarning(
                "Cannot reach the peer, but it is still reaching this node (last verified request {Seconds:F0}s ago) — the link is blocked one way, not the peer down. Staying Standby.",
                (DateTime.UtcNow - lastInbound).TotalSeconds);
            RecordEvent("warning",
                "ارتباط خروجی با سرور مقابل قطع است، اما آن سرور همچنان به این سرور درخواست می‌فرستد — یعنی مسیر فقط یک‌طرفه بسته است و سرور مقابل سالم است. ترفیع خودکار انجام نشد.");
            return;
        }

        lock (_transitionLock)
        {
            var state = _store.GetClusterState();
            if (state.Role != ClusterRole.Standby) return; // only a Standby auto-promotes; Primary/Recovering never auto-transition here

            // A node that has never completed a bootstrap holds no copy of the Primary's data, so promoting it
            // would put an empty (or seed-only) server in charge of live traffic. That is worse than staying
            // read-only. It also removes a real setup hazard: configuring the Standby before the Primary used
            // to make the new node promote itself after the grace period, leaving two Primaries the moment the
            // real one came online.
            if (state.BootstrappedAtUtc is null)
            {
                _logger.LogWarning(
                    "Peer unreachable for over {Seconds}s, but this node has never bootstrapped from a Primary — staying read-only instead of promoting.",
                    _failoverGraceSeconds);
                return;
            }
        }

        // "I cannot reach the peer" and "the peer is down" are not the same statement, and on a link that can
        // be cut or filtered wholesale they come apart badly: the Primary keeps serving customers while this
        // node, seeing only silence, declares itself Primary too. The peer is then demoted the moment the link
        // returns, and whichever side loses that argument loses its writes with it. So before promoting, this
        // node has to establish that the silence is not simply its own isolation.
        var connectivity = await HasOwnConnectivityAsync(ct);
        if (connectivity == false)
        {
            _logger.LogWarning(
                "Peer unreachable for over {Seconds}s, but no witness is reachable either — this node is isolated, not the Primary down. Staying read-only.",
                _failoverGraceSeconds);
            RecordEvent("warning", "قطع اتصال با سرور مقابل، اما اینترنت این سرور هم قطع بود — ترفیع خودکار انجام نشد.");
            _ = _alerts.SendAlertAsync("🌐 ارتباط این سرور با اینترنت قطع است — ترفیع خودکار انجام نشد تا هر دو سرور هم‌زمان Primary نشوند.");
            return;
        }
        if (connectivity is null)
            _logger.LogWarning("No cluster witness has ever been reachable from this node — promoting without an isolation check. Set PHONIX_CLUSTER_WITNESS_URLS to a host this server can reach.");

        lock (_transitionLock)
        {
            // Re-read under the lock: the witness probe above is an await, so the role could have changed
            // (a peer-requested demote, an operator action) while it was in flight.
            var state = _store.GetClusterState();
            if (state.Role != ClusterRole.Standby || state.BootstrappedAtUtc is null) return;

            state.Role = ClusterRole.Primary;
            state.LastFailoverAtUtc = DateTime.UtcNow;
            _store.SetClusterState(state);
            SetRoleCache(ClusterRole.Primary);
        }
        _logger.LogWarning("Peer unreachable for over {Seconds}s — auto-promoting this node to Primary.", _failoverGraceSeconds);
        RecordEvent("critical", $"سرور مقابل بیش از {_failoverGraceSeconds} ثانیه در دسترس نبود — این سرور به‌طور خودکار Primary شد (failover).");
        _ = _alerts.SendAlertAsync("🔴 سرور اصلی خوشه در دسترس نیست — این سرور به‌طور خودکار Primary شد.");
    }

    // Reattempts dead-lettered events on a best-effort basis (Fix 5). A success clears the entry; a repeated
    // failure bumps its retry counter until it hits the cap, after which it stays parked (and counted in the
    // cluster status) instead of being retried forever.
    private void RetryDeadLetters()
    {
        foreach (var entry in _store.GetRetryableDeadLetters(MaxDeadLetterRetries))
        {
            try
            {
                _store.ApplyRemoteOp(entry);
                _store.ClearSyncFailure(entry.Id);
                _logger.LogInformation("Cluster sync: dead-lettered entry {OutboxId} applied on retry.", entry.Id);
            }
            catch (Exception ex)
            {
                _store.RecordSyncFailure(entry, ex.Message);
            }
        }
    }

    // ── Media file synchronization (Fix 4) ───────────────────────────────────────────────────────────────
    // Pulls the peer's media manifest and downloads only the files this node is missing (or whose checksum
    // differs). Independent of the DB sync path and of business logic — a failure here never affects data
    // replication. Never deletes: a file present locally but absent from the manifest is simply left alone.
    private async Task SyncMediaIfDueAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_configuredPeerUrl)) return;
        var last = new DateTime(Interlocked.Read(ref _lastMediaSyncTicks), DateTimeKind.Utc);
        if (DateTime.UtcNow - last < TimeSpan.FromSeconds(_mediaSyncIntervalSeconds)) return;
        Interlocked.Exchange(ref _lastMediaSyncTicks, DateTime.UtcNow.Ticks);
        await PullMediaAsync(ct);
    }

    private async Task<int> PullMediaAsync(CancellationToken ct)
    {
        var manifest = await CallPeerAsync<ClusterMediaManifest>(HttpMethod.Post, "/api/cluster/media/manifest", "{}", ct);
        if (manifest is null) return 0;

        var local = _files.ListMediaForSync()
            .ToDictionary(e => (e.Category, e.Name), e => e.Sha256);
        var pulled = 0;
        foreach (var entry in manifest.Files)
        {
            if (local.TryGetValue((entry.Category, entry.Name), out var localHash)
                && string.Equals(localHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                continue; // already have an identical copy — incremental: skip

            var body = JsonSerializer.Serialize(new ClusterMediaFileInput(entry.Category, entry.Name), JsonOpts);
            using var response = await SendSignedAsync(HttpMethod.Post, "/api/cluster/media/file", body, ct, BulkTransferTimeout);
            if (response is null || !response.IsSuccessStatusCode) continue;
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            // The integrity gate inside WriteRawFromSync rejects the file if the bytes don't hash to what the
            // manifest advertised, so a corrupt transfer is never persisted.
            if (_files.WriteRawFromSync(entry.Category, entry.Name, bytes, entry.Sha256)) pulled++;
        }
        if (pulled > 0) _logger.LogInformation("Cluster media sync: pulled {Count} file(s) from peer.", pulled);
        return pulled;
    }

    // ── Admin-triggered actions ──────────────────────────────────────────────────────────────────────────

    // Fix 3: attach this (Standby) node to an already-populated Primary. Idempotent and safe to re-run: it
    // pulls one full snapshot, restores it wholesale, pins the sync cursor to the Primary's high-water mark,
    // reserves the Standby id band, then pulls all media. After this the ordinary incremental loop keeps it
    // current. Only a Standby may bootstrap — a Primary bootstrapping would clobber live production data.
    public async Task<(bool Ok, string? Error)> BootstrapFromPrimaryAsync()
    {
        if (!_clusterEnabled) return (false, "خوشه‌سازی روی این سرور فعال نیست.");
        if (string.IsNullOrWhiteSpace(_configuredPeerUrl)) return (false, "آدرس سرور مقابل تنظیم نشده است.");
        var state = _store.GetClusterState();
        if (state.Role != ClusterRole.Standby)
            return (false, "بوت‌استرپ فقط روی سرور Standby ممکن است (برای جلوگیری از بازنویسی داده‌های Primary).");

        var snapshot = await CallPeerAsync<ClusterSnapshotResponse>(HttpMethod.Post, "/api/cluster/sync/snapshot", "{}",
            CancellationToken.None, BulkTransferTimeout);
        if (snapshot is null)
            return (false, "دریافت اسنپ‌شات از سرور مقابل ناموفق بود — اتصال و تنظیمات را بررسی کنید.");

        var parsed = _store.DeserializeSnapshot(snapshot.SnapshotJson);
        if (parsed is null)
            return (false, "اسنپ‌شات دریافتی نامعتبر بود.");

        _store.RestoreFromPeerSnapshot(parsed, snapshot.HighWaterMark, snapshot.DataEpoch);
        _store.EnsureStandbyIdBand();

        var mediaCount = await PullMediaAsync(CancellationToken.None);
        Interlocked.Exchange(ref _lastSyncTicks, DateTime.UtcNow.Ticks);

        _logger.LogInformation("Standby bootstrap complete: restored snapshot (cursor {Cursor}), pulled {Media} media file(s).",
            snapshot.HighWaterMark, mediaCount);
        RecordEvent("info", $"راه‌اندازی اولیه (bootstrap) از Primary کامل شد — {mediaCount} فایل رسانه دریافت شد.");
        _ = _alerts.SendAlertAsync("✅ سرور Standby با موفقیت از Primary راه‌اندازی اولیه (bootstrap) شد.");
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> PromoteAsync()
    {
        if (!_clusterEnabled) return (false, "خوشه‌سازی روی این سرور فعال نیست.");
        var state = _store.GetClusterState();
        if (state.Role != ClusterRole.Recovering)
            return (false, "ترفیع فقط از حالت Recovering ممکن است.");
        if (PendingCount > 0)
            return (false, "همگام‌سازی هنوز کامل نشده است؛ صبر کنید یا «همگام‌سازی دستی» را بزنید.");

        var demoted = await CallPeerOkAsync(HttpMethod.Post, "/api/cluster/sync/demote", "{}", CancellationToken.None);
        if (!demoted)
            return (false, "سرور مقابل در دسترس نیست یا تأیید نکرد — برای جلوگیری از دو Primary همزمان، ترفیع انجام نشد.");

        lock (_transitionLock)
        {
            state = _store.GetClusterState();
            state.Role = ClusterRole.Primary;
            state.LastFailoverAtUtc = null;
            state.LastPromotedAtUtc = DateTime.UtcNow;
            _store.SetClusterState(state);
            SetRoleCache(ClusterRole.Primary);
        }
        _logger.LogInformation("Manually promoted to Primary.");
        RecordEvent("info", "این سرور به‌صورت دستی از پنل ادمین به Primary ترفیع یافت.");
        _ = _alerts.SendAlertAsync("✅ این سرور به‌صورت دستی Primary شد.");
        return (true, null);
    }

    public Task<(bool Ok, string? Error)> StartRecoveryAsync()
    {
        if (!_clusterEnabled) return Task.FromResult((false, (string?)"خوشه‌سازی روی این سرور فعال نیست."));
        lock (_transitionLock)
        {
            var state = _store.GetClusterState();
            state.Role = ClusterRole.Recovering;
            _store.SetClusterState(state);
            SetRoleCache(ClusterRole.Recovering);
        }
        _logger.LogInformation("Manually entered Recovering state.");
        RecordEvent("info", "این سرور به‌صورت دستی از پنل ادمین وارد حالت Recovering شد.");
        return Task.FromResult((true, (string?)null));
    }

    public async Task<(bool Ok, string? Error)> ResyncNowAsync()
    {
        if (!_clusterEnabled) return (false, "خوشه‌سازی روی این سرور فعال نیست.");
        await SyncOnceAsync(CancellationToken.None);
        return (true, null);
    }

    // Admin-panel config apply (no terminal, no restart): enable clustering by picking a mode while still
    // Standalone, and/or set or rotate the peer URL / HMAC secret on a running node. Mirrors the same
    // "explicit admin action persists to ClusterState and wins from now on" pattern as Promote/Recover.
    public Task<(bool Ok, string? Error)> UpdateConfigAsync(string? mode, string? peerUrl, string? secret)
    {
        var normalizedMode = mode?.Trim().ToLowerInvariant();
        if (normalizedMode is not null && normalizedMode is not ("primary" or "standby"))
            return Task.FromResult((false, (string?)"حالت باید Primary یا Standby باشد."));

        var trimmedPeer = string.IsNullOrWhiteSpace(peerUrl) ? null : peerUrl.Trim().TrimEnd('/');
        if (trimmedPeer is not null)
        {
            if (!Uri.TryCreate(trimmedPeer, UriKind.Absolute, out var uri))
                return Task.FromResult((false, (string?)"آدرس سرور مقابل معتبر نیست."));
            if (uri.Scheme != Uri.UriSchemeHttps && !_allowInsecurePeer)
                return Task.FromResult((false, (string?)
                    "آدرس سرور مقابل باید HTTPS باشد (برای تست محلی می‌توانید PHONIX_CLUSTER_ALLOW_INSECURE را فعال کنید)."));
        }
        var trimmedSecret = string.IsNullOrWhiteSpace(secret) ? null : secret.Trim();

        bool enabledNow;
        lock (_transitionLock)
        {
            var state = _store.GetClusterState();
            if (normalizedMode is not null)
            {
                if (state.Role != ClusterRole.Standalone)
                    return Task.FromResult((false, (string?)"حالت خوشه فقط زمانی قابل تنظیم است که سرور در حالت Standalone باشد؛ برای تغییر نقش از ترفیع/بازیابی استفاده کنید."));
                state.Role = normalizedMode == "primary" ? ClusterRole.Primary : ClusterRole.Standby;
                SetRoleCache(state.Role);
            }
            if (trimmedPeer is not null)
            {
                state.PeerUrl = trimmedPeer;
                _configuredPeerUrl = trimmedPeer;
            }
            if (trimmedSecret is not null)
            {
                state.Secret = trimmedSecret;
                ClusterAuth.SetRuntimeSecret(trimmedSecret);
            }
            _store.SetClusterState(state);
            _clusterEnabled = state.Role != ClusterRole.Standalone;
            enabledNow = _clusterEnabled;
        }

        _logger.LogInformation("Cluster config updated from admin panel (mode={Mode}, peerUrl changed={PeerChanged}, secret rotated={SecretChanged}).",
            normalizedMode ?? "unchanged", trimmedPeer is not null, trimmedSecret is not null);
        RecordEvent("info", $"تنظیمات خوشه از پنل ادمین به‌روزرسانی شد" +
            (normalizedMode is not null ? $" — حالت: {normalizedMode}" : "") +
            (trimmedPeer is not null ? $" — آدرس سرور مقابل تغییر کرد" : "") +
            (trimmedSecret is not null ? " — کلید امنیتی چرخانده شد" : "") + ".");
        return Task.FromResult((true, enabledNow ? null
            : (string?)"تنظیمات ذخیره شد؛ خوشه هنوز فعال نیست — یک حالت (Primary/Standby) انتخاب کنید تا فعال شود."));
    }

    // Manual role correction (Fix: an admin who set the wrong mode at enable-time had no way back — Promote
    // only works from Recovering and StartRecovery only from Primary, so a plain Standby was stuck). This
    // bypasses that handshake entirely, so it must only ever be reached through an admin panel confirmation
    // that spells out the split-brain risk.
    public Task<(bool Ok, string? Error)> ForceSetRoleAsync(string? mode)
    {
        if (!_clusterEnabled) return Task.FromResult((false, (string?)"خوشه‌سازی روی این سرور فعال نیست."));
        var normalized = mode?.Trim().ToLowerInvariant();
        if (normalized is not ("primary" or "standby"))
            return Task.FromResult((false, (string?)"حالت باید Primary یا Standby باشد."));
        var newRole = normalized == "primary" ? ClusterRole.Primary : ClusterRole.Standby;

        lock (_transitionLock)
        {
            var state = _store.GetClusterState();
            if (state.Role == newRole) return Task.FromResult((true, (string?)null));

            if (newRole == ClusterRole.Primary) { state.LastFailoverAtUtc = null; state.LastPromotedAtUtc = DateTime.UtcNow; }
            else state.LastDemotedAtUtc = DateTime.UtcNow;
            state.Role = newRole;
            _store.SetClusterState(state);
            SetRoleCache(newRole);
        }
        // Same id-band requirement as HandleDemote — a node newly forced into Standby must still reserve its
        // disjoint autoincrement range before accepting any write.
        if (newRole == ClusterRole.Standby) _store.EnsureStandbyIdBand();

        _logger.LogWarning("Cluster role force-set to {Role} from admin panel (manual override, no peer handshake).", newRole);
        RecordEvent("warning", $"نقش این سرور به‌صورت دستی و بدون هماهنگی با سرور مقابل به {newRole} تغییر کرد.");
        return Task.FromResult((true, (string?)null));
    }

    // ── Node-to-node actions (called from ClusterController behind ClusterPeerAuthAttribute) ──────────────

    // How long consumed outbox history is kept after the peer confirms it, and how far the peer's cursor must
    // advance before that acknowledgement is worth a write. The pull runs every few seconds, so recording every
    // single one would turn a read into a write on the busiest path in the cluster.
    private static readonly TimeSpan OutboxRetention = TimeSpan.FromHours(24);
    private const long PeerAckWriteThreshold = 200;

    public ClusterSyncPullResponse HandlePull(long since)
    {
        var state = _store.GetClusterState();
        var entries = _store.GetOutboxSince(since);

        // Asking for everything after `since` is the peer stating it has applied everything up to it. That is
        // the only signal that lets this node retire replicated history without risking the peer's cursor.
        if (since > state.PeerAckCursor + PeerAckWriteThreshold)
        {
            lock (_transitionLock)
            {
                state = _store.GetClusterState();
                if (since > state.PeerAckCursor)
                {
                    state.PeerAckCursor = since;
                    _store.SetClusterState(state);
                }
            }
            var pruned = _store.PruneOutboxUpTo(state.PeerAckCursor, OutboxRetention);
            if (pruned > 0)
                _logger.LogInformation("Cluster outbox: pruned {Count} entries the peer has already applied.", pruned);
        }

        return new ClusterSyncPullResponse(entries, state.Role.ToString(), _store.GetOutboxHighWaterMark(), state.DataEpoch);
    }

    public void HandleDemote()
    {
        lock (_transitionLock)
        {
            var state = _store.GetClusterState();
            state.Role = ClusterRole.Standby;
            state.LastFailoverAtUtc = null;
            state.LastDemotedAtUtc = DateTime.UtcNow;
            _store.SetClusterState(state);
            SetRoleCache(ClusterRole.Standby);
        }
        // Fix 1: a node becoming Standby must hold a disjoint id band before it can accept any (auth) write.
        _store.EnsureStandbyIdBand();
        _logger.LogInformation("Demoted to Standby at the peer's request (it is being promoted to Primary).");
        RecordEvent("info", "این سرور به درخواست سرور مقابل به Standby تنزل یافت.");
        _ = _alerts.SendAlertAsync("ℹ️ این سرور به Standby تنزل یافت (سرور مقابل Primary شد).");
    }

    // Full-snapshot handler: a fresh Standby calls this once to seed itself (Fix 3). Returns the SAME
    // StoreSnapshot wire format the backup flow uses, plus this node's outbox high-water mark so the caller
    // can pin its incremental cursor to exactly where the snapshot ends.
    public ClusterSnapshotResponse HandleSnapshotRequest() =>
        new(_store.SerializeSnapshot(), _store.GetOutboxHighWaterMark(), _store.GetClusterState().DataEpoch);

    // Media manifest handler (Fix 4): advertises every uploaded file with its checksum for the peer to diff.
    public ClusterMediaManifest HandleMediaManifest() =>
        new(_files.ListMediaForSync().Select(e => new ClusterMediaEntry(e.Category, e.Name, e.Size, e.Sha256)).ToList());

    // Single-file handler (Fix 4): streams one media file's raw bytes, or null (→ 404) when it doesn't exist.
    public byte[]? HandleMediaFile(string category, string name) => _files.ReadRawForSync(category, name);

    // ── Shared signed-request plumbing ───────────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage?> SendSignedAsync(HttpMethod method, string path, string body, CancellationToken ct,
        TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(_configuredPeerUrl)) return null;
        var signed = ClusterAuth.SignRequest(method.Method, path, body);
        if (signed is null) return null; // PHONIX_CLUSTER_SECRET not configured — never call unsigned

        try
        {
            var client = _httpClientFactory.CreateClient();
            // Control calls (a cursor pull, a demote, a manifest) answer in milliseconds, and a short timeout
            // is what makes a dead peer show up as unreachable quickly. Bulk transfers — the initial snapshot
            // and each media file — are whole payloads over an intercontinental link, where the peer answers
            // immediately but the body takes far longer than any control call ever would. Holding both to the
            // same deadline made bootstrap impossible on a slow link: the Primary served the snapshot in
            // 274ms and the Standby then hung up mid-download, reporting it as a connection problem.
            client.Timeout = timeout ?? TimeSpan.FromSeconds(10);
            using var request = new HttpRequestMessage(method, _configuredPeerUrl + path)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add(ClusterAuth.TimestampHeader, signed.Value.Timestamp);
            request.Headers.Add(ClusterAuth.SignatureHeader, signed.Value.Signature);
            return await client.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Cluster peer call to {Path} failed", path);
            return null;
        }
    }

    private async Task<T?> CallPeerAsync<T>(HttpMethod method, string path, string body, CancellationToken ct,
        TimeSpan? timeout = null)
    {
        using var response = await SendSignedAsync(method, path, body, ct, timeout);
        if (response is null || !response.IsSuccessStatusCode) return default;
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<T>(json, JsonOpts);
    }

    private async Task<bool> CallPeerOkAsync(HttpMethod method, string path, string body, CancellationToken ct)
    {
        using var response = await SendSignedAsync(method, path, body, ct);
        return response is not null && response.IsSuccessStatusCode;
    }
}

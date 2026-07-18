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

    // Admin-triggered actions (see ClusterController). Each returns (ok, error) rather than throwing —
    // these are ordinary "the operator clicked a button" outcomes, not exceptional failures.
    Task<(bool Ok, string? Error)> PromoteAsync();
    Task<(bool Ok, string? Error)> StartRecoveryAsync();
    Task<(bool Ok, string? Error)> ResyncNowAsync();
    Task<(bool Ok, string? Error)> BootstrapFromPrimaryAsync();

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

    private readonly bool _clusterEnabled;
    private readonly string? _configuredPeerUrl;
    private readonly string _nodeId;
    private readonly ClusterRole? _seedRole;
    private readonly int _syncIntervalSeconds;
    private readonly int _failoverGraceSeconds;
    private readonly int _mediaSyncIntervalSeconds;
    private readonly bool _allowInsecurePeer;

    // A dead-lettered event is retried this many times before it is left permanently parked (and surfaced in
    // the cluster status) rather than retried forever — one poison event must never wedge the whole cluster.
    private const int MaxDeadLetterRetries = 5;
    private const long StandbyIdBandOffset = SqliteDataStore.StandbyIdBandOffset;

    private long _lastMediaSyncTicks;

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

    private void SetRoleCache(ClusterRole role) => Interlocked.Exchange(ref _roleValue, (long)role);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_clusterEnabled) return; // standalone: this loop never runs at all

        var state = _store.GetClusterState();
        if (state.Role == ClusterRole.Standalone && _seedRole is { } seed)
        {
            // First boot with clustering configured: seed once from the env var. Every transition after this
            // is driven by this service (auto-failover) or the admin panel (promote/recover), never the env
            // var again — exactly like PHONIX_OWNER_USERNAME/PASSWORD only seed the initial owner account.
            state.Role = seed;
            _store.SetClusterState(state);
        }
        SetRoleCache(state.Role);

        // Fix 1: a Standby MUST reserve its disjoint id band before it can accept (or replay) any write, so its
        // inserts can never collide with the Primary's during a partition. Idempotent — applied once, ever.
        if (state.Role == ClusterRole.Standby && _store.EnsureStandbyIdBand())
            _logger.LogInformation("Standby id band reserved (autoincrement offset +{Offset}).", StandbyIdBandOffset);

        // Fix 3: a fresh Standby attaching to an already-populated Primary can't converge from the incremental
        // outbox alone — it pulls one full snapshot first. Auto-runs when this node is a never-bootstrapped,
        // empty Standby; a non-empty or already-bootstrapped node skips straight to incremental sync.
        // Deliberately not gated on an empty store. A real install is never empty by this point — startup
        // applies the owner account from the environment — so requiring emptiness meant the initial
        // bootstrap never ran outside tests. What makes this safe is the pair of conditions that remain:
        // the operator explicitly configured this node as Standby, and it has never bootstrapped before.
        if (state.Role == ClusterRole.Standby && state.BootstrappedAtUtc is null
            && !string.IsNullOrWhiteSpace(_configuredPeerUrl))
        {
            var (ok, err) = await BootstrapFromPrimaryAsync();
            if (!ok) _logger.LogWarning("Initial Standby bootstrap from Primary did not complete: {Error}", err);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
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
            RecordContactFailure();
            return;
        }

        Interlocked.Exchange(ref _lastPeerContactTicks, DateTime.UtcNow.Ticks);
        Interlocked.Exchange(ref _consecutiveFailures, 0);

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
            _logger.LogWarning("Cluster conflict: both nodes claim Primary — demoting this node to Recovering.");
            _ = _alerts.SendAlertAsync("⚠️ هر دو سرور خوشه Primary اعلام شدند — این سرور به حالت Recovering منتقل شد و باید دستی ترفیع بگیرد.");
        }
    }

    private void RecordContactFailure()
    {
        var failures = Interlocked.Increment(ref _consecutiveFailures);
        if (failures < 3) return; // ignore a single blip — a real outage keeps failing across several cycles

        var lastContact = new DateTime(Interlocked.Read(ref _lastPeerContactTicks), DateTimeKind.Utc);
        if (DateTime.UtcNow - lastContact < TimeSpan.FromSeconds(_failoverGraceSeconds)) return;

        lock (_transitionLock)
        {
            var state = _store.GetClusterState();
            if (state.Role != ClusterRole.Standby) return; // only a Standby auto-promotes; Primary/Recovering never auto-transition here
            state.Role = ClusterRole.Primary;
            state.LastFailoverAtUtc = DateTime.UtcNow;
            _store.SetClusterState(state);
            SetRoleCache(ClusterRole.Primary);
        }
        _logger.LogWarning("Peer unreachable for over {Seconds}s — auto-promoting this node to Primary.", _failoverGraceSeconds);
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
            using var response = await SendSignedAsync(HttpMethod.Post, "/api/cluster/media/file", body, ct);
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

        var snapshot = await CallPeerAsync<ClusterSnapshotResponse>(HttpMethod.Post, "/api/cluster/sync/snapshot", "{}", CancellationToken.None);
        if (snapshot is null)
            return (false, "دریافت اسنپ‌شات از سرور مقابل ناموفق بود — اتصال و تنظیمات را بررسی کنید.");

        var parsed = _store.DeserializeSnapshot(snapshot.SnapshotJson);
        if (parsed is null)
            return (false, "اسنپ‌شات دریافتی نامعتبر بود.");

        _store.RestoreFromPeerSnapshot(parsed, snapshot.HighWaterMark);
        _store.EnsureStandbyIdBand();

        var mediaCount = await PullMediaAsync(CancellationToken.None);
        Interlocked.Exchange(ref _lastSyncTicks, DateTime.UtcNow.Ticks);

        _logger.LogInformation("Standby bootstrap complete: restored snapshot (cursor {Cursor}), pulled {Media} media file(s).",
            snapshot.HighWaterMark, mediaCount);
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
        return Task.FromResult((true, (string?)null));
    }

    public async Task<(bool Ok, string? Error)> ResyncNowAsync()
    {
        if (!_clusterEnabled) return (false, "خوشه‌سازی روی این سرور فعال نیست.");
        await SyncOnceAsync(CancellationToken.None);
        return (true, null);
    }

    // ── Node-to-node actions (called from ClusterController behind ClusterPeerAuthAttribute) ──────────────

    public ClusterSyncPullResponse HandlePull(long since)
    {
        var state = _store.GetClusterState();
        var entries = _store.GetOutboxSince(since);
        return new ClusterSyncPullResponse(entries, state.Role.ToString(), _store.GetOutboxHighWaterMark());
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
        _ = _alerts.SendAlertAsync("ℹ️ این سرور به Standby تنزل یافت (سرور مقابل Primary شد).");
    }

    // Full-snapshot handler: a fresh Standby calls this once to seed itself (Fix 3). Returns the SAME
    // StoreSnapshot wire format the backup flow uses, plus this node's outbox high-water mark so the caller
    // can pin its incremental cursor to exactly where the snapshot ends.
    public ClusterSnapshotResponse HandleSnapshotRequest() =>
        new(_store.SerializeSnapshot(), _store.GetOutboxHighWaterMark());

    // Media manifest handler (Fix 4): advertises every uploaded file with its checksum for the peer to diff.
    public ClusterMediaManifest HandleMediaManifest() =>
        new(_files.ListMediaForSync().Select(e => new ClusterMediaEntry(e.Category, e.Name, e.Size, e.Sha256)).ToList());

    // Single-file handler (Fix 4): streams one media file's raw bytes, or null (→ 404) when it doesn't exist.
    public byte[]? HandleMediaFile(string category, string name) => _files.ReadRawForSync(category, name);

    // ── Shared signed-request plumbing ───────────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage?> SendSignedAsync(HttpMethod method, string path, string body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_configuredPeerUrl)) return null;
        var signed = ClusterAuth.SignRequest(method.Method, path, body);
        if (signed is null) return null; // PHONIX_CLUSTER_SECRET not configured — never call unsigned

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
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

    private async Task<T?> CallPeerAsync<T>(HttpMethod method, string path, string body, CancellationToken ct)
    {
        using var response = await SendSignedAsync(method, path, body, ct);
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

using System.Text.Json;
using System.Text.Json.Serialization;
using Phonix.Api.Models;

namespace Phonix.Api.Data;

// The on-disk shape of the audit trail. Deliberately its OWN file (audit_store.json), separate from the
// main store.json: the audit log is high-volume, append-only, and disposable, so mixing it into the primary
// snapshot would bloat every main-store flush and slow down hot reads/writes. Keeping it isolated means the
// main store is never touched when an audit entry is recorded.
public sealed class AuditSnapshot
{
    public List<AuditLog> Logs { get; set; } = new();
    public int Seq { get; set; }
}

// Dedicated, self-contained persistence for the admin audit trail. Owns its list, its sequence, and its
// file. Registered as a singleton and flushed periodically (see AuditPersistenceWorker), mirroring how the
// main StoreData persists — but with zero coupling to it.
public sealed class AuditStore
{
    // Bounds the file: an audit log is a recent-activity record, not permanent archival storage. Once the
    // cap is hit the oldest entries are trimmed so audit_store.json can't grow without limit.
    private const int MaxRecords = 10_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _gate = new();
    private readonly object _saveGate = new();
    private readonly List<AuditLog> _logs = new();
    private int _seq;

    // O(1) dirty tracking. Record() is the ONLY mutation path, so a monotonic version bumped there fully
    // describes the dirty state. The periodic flush compares versions and returns immediately when nothing
    // new arrived, so the expensive WriteIndented serialization no longer runs on every idle 10s tick.
    private long _version;
    private long _savedVersion;   // 0 == "matches what we loaded / an empty trail"; no spurious first write

    private readonly string _filePath;

    public AuditStore()
    {
        _filePath = Environment.GetEnvironmentVariable("PHONIX_AUDIT_FILE")
            ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "audit_store.json");
        TryLoad();
    }

    // Appends one entry. Not flushed synchronously (audit writes are frequent and non-financial); the
    // periodic worker persists the change. Trims oldest-first when the cap is exceeded.
    public void Record(AuditLog log)
    {
        lock (_gate)
        {
            log.Id = ++_seq;
            log.Timestamp = DateTime.UtcNow;
            _logs.Add(log);
            if (_logs.Count > MaxRecords)
                _logs.RemoveRange(0, _logs.Count - MaxRecords);
            _version++;
        }
    }

    // Paged + filtered read, newest first. `to` is exclusive (the controller pushes a date-only upper bound
    // to the next day's start so a single-day filter captures the whole day).
    public (IReadOnlyList<AuditLog> Items, int Total) GetAuditLogs(
        string? search, AuditAction? action, DateTime? from, DateTime? to, int page, int pageSize)
    {
        lock (_gate)
        {
            IEnumerable<AuditLog> q = _logs;

            if (action is AuditAction a) q = q.Where(l => l.ActionType == a);
            if (from is DateTime f) q = q.Where(l => l.Timestamp >= f);
            if (to is DateTime t) q = q.Where(l => l.Timestamp < t);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                q = q.Where(l =>
                    l.Entity.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    l.ActorName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    l.Path.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    l.Ip.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    (l.EntityId ?? "").Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            // Id is monotonic with insertion, so descending id == newest first (no Timestamp tie-break needed).
            var ordered = q.OrderByDescending(l => l.Id).ToList();
            var total = ordered.Count;

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);
            var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            return (items, total);
        }
    }

    private void TryLoad()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var json = File.ReadAllText(_filePath);
            var snapshot = JsonSerializer.Deserialize<AuditSnapshot>(json, JsonOptions);
            if (snapshot is null) return;
            lock (_gate)
            {
                _logs.Clear();
                _logs.AddRange(snapshot.Logs);
                _seq = snapshot.Seq;
                // Loaded state matches disk; keep saved == current so the first flush is a no-op.
                _savedVersion = _version;
            }
        }
        catch
        {
            // A corrupt/unreadable audit file must never take the app down — start with an empty trail.
        }
    }

    // Periodic flush: O(1) when nothing new was recorded, otherwise a single atomic write.
    public void SaveIfChanged()
    {
        lock (_saveGate)
        {
            long version;
            AuditSnapshot snapshot;
            lock (_gate)
            {
                if (_version == _savedVersion) return;   // idle fast-path: no copy, no serialize
                version = _version;
                snapshot = new AuditSnapshot { Logs = _logs.ToList(), Seq = _seq };
            }
            WriteAtomic(JsonSerializer.Serialize(snapshot, JsonOptions));
            _savedVersion = version;
        }
    }

    // Unconditional flush (used on shutdown).
    public void Save()
    {
        lock (_saveGate)
        {
            long version;
            AuditSnapshot snapshot;
            lock (_gate)
            {
                version = _version;
                snapshot = new AuditSnapshot { Logs = _logs.ToList(), Seq = _seq };
            }
            WriteAtomic(JsonSerializer.Serialize(snapshot, JsonOptions));
            _savedVersion = version;
        }
    }

    // Writes to a unique temp file then atomically swaps it in, so a crash mid-write can never leave a
    // half-written audit_store.json.
    private void WriteAtomic(string json)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tmp, json);
            File.Move(tmp, _filePath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tmp)) { try { File.Delete(tmp); } catch { /* best-effort cleanup */ } }
            throw;
        }
    }
}

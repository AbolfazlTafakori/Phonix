using System.Text.Json;
using System.Text.Json.Serialization;
using Phonix.Api.Models;

namespace Phonix.Api.Data;

// The on-disk shape of the sent-email log.
public sealed class EmailLogSnapshot
{
    public List<SentEmail> Sent { get; set; } = new();
    public int Seq { get; set; }
}

// Dedicated persistence for the record of outbound email, deliberately its OWN file (email_log.json) rather
// than a table in the main store — the same reasoning that keeps the audit trail separate.
//
// A send happens on almost every meaningful action (verification, delivery, receipts, renewal reminders), so
// this is high-volume, append-only and disposable. Folding it into the main store would bloat every snapshot,
// enlarge every backup, and put operational noise onto the cluster's replication link. Keeping it here means
// recording an email never touches the main store at all, and the backup/restore and Primary/Standby paths
// carry exactly what they carried before.
public sealed class EmailLogStore
{
    // Bounds the file the same way the audit trail is bounded: this answers "what did we send recently?",
    // not "what did we ever send". Oldest entries are trimmed once the cap is reached.
    private const int MaxRecords = 10_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _gate = new();
    private readonly object _saveGate = new();
    private readonly List<SentEmail> _sent = new();
    private int _seq;

    // O(1) dirty tracking: Record() is the only mutation, so a version bumped there fully describes the
    // dirty state and an idle tick costs nothing.
    private long _version;
    private long _savedVersion;

    private readonly string _filePath;

    public EmailLogStore()
    {
        _filePath = Environment.GetEnvironmentVariable("PHONIX_EMAIL_LOG_FILE")
            ?? Phonix.Api.PersistentPaths.Combine("email_log.json");
        TryLoad();
    }

    // Appends one attempt. Never throws: failing to RECORD a send must not fail the send itself.
    public void Record(string to, string subject, bool success, string? error = null)
    {
        try
        {
            lock (_gate)
            {
                _sent.Add(new SentEmail
                {
                    Id = ++_seq,
                    To = to ?? "",
                    Subject = subject ?? "",
                    SentAtUtc = DateTime.UtcNow,
                    Success = success,
                    Error = string.IsNullOrWhiteSpace(error) ? null : error,
                });
                if (_sent.Count > MaxRecords)
                    _sent.RemoveRange(0, _sent.Count - MaxRecords);
                _version++;
            }
        }
        catch
        {
            // Bookkeeping is never worth breaking a delivery over.
        }
    }

    // Paged + filtered read, newest first. `to` is exclusive so a single-day filter captures the whole day.
    public (IReadOnlyList<SentEmail> Items, int Total) Get(
        string? search, bool? success, DateTime? from, DateTime? to, int page, int pageSize)
    {
        lock (_gate)
        {
            IEnumerable<SentEmail> q = _sent;

            if (success is bool s) q = q.Where(e => e.Success == s);
            if (from is DateTime f) q = q.Where(e => e.SentAtUtc >= f);
            if (to is DateTime t) q = q.Where(e => e.SentAtUtc < t);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                q = q.Where(e =>
                    e.To.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    e.Subject.Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            // Id is monotonic with insertion, so descending id is newest first.
            var ordered = q.OrderByDescending(e => e.Id).ToList();
            var total = ordered.Count;

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);
            return (ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList(), total);
        }
    }

    // How many of the recent attempts failed — the number worth putting in front of an admin.
    public int FailedCount()
    {
        lock (_gate) return _sent.Count(e => !e.Success);
    }

    private void TryLoad()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var snapshot = JsonSerializer.Deserialize<EmailLogSnapshot>(File.ReadAllText(_filePath), JsonOptions);
            if (snapshot is null) return;
            lock (_gate)
            {
                _sent.Clear();
                _sent.AddRange(snapshot.Sent);
                _seq = snapshot.Seq;
                _savedVersion = _version; // loaded state matches disk; first flush is a no-op
            }
        }
        catch
        {
            // A corrupt log must never take the app down — start empty.
        }
    }

    // Periodic flush: O(1) when nothing new arrived, otherwise one atomic write.
    public void SaveIfChanged()
    {
        lock (_saveGate)
        {
            long version;
            EmailLogSnapshot snapshot;
            lock (_gate)
            {
                if (_version == _savedVersion) return;
                version = _version;
                snapshot = new EmailLogSnapshot { Sent = _sent.ToList(), Seq = _seq };
            }
            WriteAtomic(JsonSerializer.Serialize(snapshot, JsonOptions));
            _savedVersion = version;
        }
    }

    // Unconditional flush (shutdown).
    public void Save()
    {
        lock (_saveGate)
        {
            long version;
            EmailLogSnapshot snapshot;
            lock (_gate)
            {
                version = _version;
                snapshot = new EmailLogSnapshot { Sent = _sent.ToList(), Seq = _seq };
            }
            WriteAtomic(JsonSerializer.Serialize(snapshot, JsonOptions));
            _savedVersion = version;
        }
    }

    // Temp file then atomic swap, so a crash mid-write can never leave a half-written log.
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
        }
    }
}

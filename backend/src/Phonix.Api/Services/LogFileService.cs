namespace Phonix.Api.Services;

public sealed record LogFileEntry(string Name, long SizeBytes, DateTime LastModifiedUtc);

public sealed record LogTailResult(int TotalMatches, IReadOnlyList<string> Lines);

// Read-only access to the Serilog output directory. The directory root is fixed at registration time, and
// every request (list, download, view) is resolved through ResolveForDownload, which rejects anything that
// isn't a bare allowed-extension file living directly inside the root — the single choke point against path
// traversal.
public sealed class LogFileService
{
    // Upper bound on lines returned by a single view request, so "all" can never produce an unbounded payload.
    public const int MaxTailLines = 2000;

    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".log", ".json", ".txt" };

    private readonly string _root;

    public LogFileService(string logDirectory) => _root = Path.GetFullPath(logDirectory);

    public IReadOnlyList<LogFileEntry> List()
    {
        if (!Directory.Exists(_root)) return Array.Empty<LogFileEntry>();
        return new DirectoryInfo(_root)
            .EnumerateFiles()
            .Where(f => AllowedExtensions.Contains(f.Extension))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => new LogFileEntry(f.Name, f.Length, f.LastWriteTimeUtc))
            .ToList();
    }

    // Resolves a client-supplied file name to an absolute path inside the log root, or null if it escapes
    // the directory, carries any path component, isn't an allowed extension, or doesn't exist.
    public string? ResolveForDownload(string? requestedName)
    {
        if (string.IsNullOrWhiteSpace(requestedName)) return null;

        // a legitimate request is a bare file name; reject anything carrying directory components.
        var name = Path.GetFileName(requestedName);
        if (!string.Equals(name, requestedName, StringComparison.Ordinal)) return null;
        if (!AllowedExtensions.Contains(Path.GetExtension(name))) return null;

        var fullPath = Path.GetFullPath(Path.Combine(_root, name));
        var rootPrefix = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, StringComparison.Ordinal)) return null;

        return File.Exists(fullPath) ? fullPath : null;
    }

    // Returns the last `limit` lines of a log file (newest first), optionally filtered by a case-insensitive
    // substring. Streams the file with a bounded buffer so a large log never loads fully into memory. Null
    // when the file can't be safely resolved.
    public LogTailResult? Tail(string? requestedName, int limit, string? search)
    {
        var path = ResolveForDownload(requestedName);
        if (path is null) return null;

        limit = Math.Clamp(limit, 1, MaxTailLines);
        var term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        var window = new Queue<string>(limit);
        var total = 0;
        foreach (var line in ReadLines(path))
        {
            if (term is not null && line.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0) continue;
            total++;
            window.Enqueue(line);
            if (window.Count > limit) window.Dequeue();
        }

        var newestFirst = window.Reverse().ToList();
        return new LogTailResult(total, newestFirst);
    }

    // Streams lines with shared read/write access so an actively-written Serilog file can still be read.
    private static IEnumerable<string> ReadLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = reader.ReadLine()) is not null) yield return line;
    }
}

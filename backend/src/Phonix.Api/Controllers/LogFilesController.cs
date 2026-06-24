using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Models;
using Phonix.Api.Services;

namespace Phonix.Api.Controllers;

public record LogFileDto(string Name, long SizeBytes, string LastModifiedUtc);
public record LogLineDto(string Timestamp, string Level, string Message, string Raw);
public record LogViewDto(string Name, int TotalMatches, int Returned, IReadOnlyList<LogLineDto> Lines);

// Admin-only access to the on-disk Serilog files: list what's available, view/search the contents without
// downloading, download a single file, or download every file as one archive. Strictly Admin (the
// audit-logs/DevOps tier) since the logs expose request and security detail.
[ApiController]
[Route("api/admin/logs")]
[Authorize(Roles = nameof(UserRole.Admin))]
public partial class LogFilesController : ControllerBase
{
    private readonly LogFileService _logs;
    public LogFilesController(LogFileService logs) => _logs = logs;

    [HttpGet]
    public IEnumerable<LogFileDto> List() =>
        _logs.List().Select(f => new LogFileDto(f.Name, f.SizeBytes, f.LastModifiedUtc.ToString("o")));

    // Last `tail` entries of one file (newest first), optionally filtered. tail <= 0 means "all" (capped).
    [HttpGet("view")]
    public ActionResult<LogViewDto> View([FromQuery] string name, [FromQuery] int tail = 100, [FromQuery] string? search = null)
    {
        var limit = tail <= 0 ? LogFileService.MaxTailLines : tail;
        var result = _logs.Tail(name, limit, search);
        if (result is null) return NotFound();

        var lines = result.Lines.Select(ParseClef).ToList();
        return new LogViewDto(name, result.TotalMatches, lines.Count, lines);
    }

    [HttpGet("download")]
    public IActionResult Download([FromQuery] string name)
    {
        var path = _logs.ResolveForDownload(name);
        if (path is null) return NotFound();

        // Serilog keeps the active file open with shared access, so read with FileShare.ReadWrite to allow
        // downloading today's log while it is still being written.
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return File(stream, "application/octet-stream", Path.GetFileName(path));
    }

    // Packs every available log file into a single zip and streams it back. The archive is assembled in a
    // temp file (file I/O may be synchronous, unlike the Kestrel response body, whose synchronous central-
    // directory write ZipArchive.Dispose would otherwise attempt) and removed automatically once streamed.
    [HttpGet("download-all")]
    public IActionResult DownloadAll()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"phonix-logs-{Guid.NewGuid():N}.zip");
        using (var zipStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
        {
            foreach (var file in _logs.List())
            {
                var path = _logs.ResolveForDownload(file.Name);
                if (path is null) continue;

                var entry = archive.CreateEntry(file.Name, CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                source.CopyTo(entryStream);
            }
        }

        var result = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        return File(result, "application/zip", "phonix-logs.zip");
    }

    // Parses one Serilog CompactJson (CLEF) line into a display row, rendering the message template against
    // its properties. A line that isn't valid CLEF is surfaced verbatim so nothing is ever hidden.
    private static LogLineDto ParseClef(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            var timestamp = root.TryGetProperty("@t", out var t) ? t.GetString() ?? "" : "";
            var level = root.TryGetProperty("@l", out var l) ? l.GetString() ?? "Information" : "Information";
            var template = root.TryGetProperty("@mt", out var mt) ? mt.GetString() ?? "" : "";

            var message = RenderTemplate(template, root);
            if (root.TryGetProperty("@x", out var x) && x.GetString() is { Length: > 0 } exception)
                message = string.IsNullOrEmpty(message) ? exception : $"{message}\n{exception}";

            return new LogLineDto(timestamp, level, message, raw);
        }
        catch
        {
            return new LogLineDto("", "", raw, raw);
        }
    }

    private static string RenderTemplate(string template, JsonElement root)
    {
        if (string.IsNullOrEmpty(template)) return "";
        return TemplateToken().Replace(template, match =>
        {
            var name = match.Groups["name"].Value;
            if (!root.TryGetProperty(name, out var value)) return match.Value;
            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText();
        });
    }

    [GeneratedRegex(@"\{[@$]?(?<name>\w+)(?:[,:][^}]*)?\}")]
    private static partial Regex TemplateToken();
}

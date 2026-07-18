using System.IO.Compression;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using SkiaSharp;

namespace Phonix.Api.Services;

// Disk-backed implementation of IFileStorageService. Files live under App_Data/ProtectedUploads/<category>
// (outside wwwroot — there is no static-file middleware for this path), and are streamed back only by the
// KYC/Cards download endpoints after an OwnsOrStaff check.
public sealed partial class LocalFileStorageService : IFileStorageService
{
    // <ownerId>__<32 hex chars>.<image ext>. The owner is embedded so the download endpoint can authorize
    // against it; the random middle makes ids unguessable; the fixed shape blocks path-traversal in ids.
    [GeneratedRegex(@"^(?<owner>\d{1,9})__[0-9a-f]{32}\.(?<ext>jpg|jpeg|png|webp)$")]
    private static partial Regex IdPattern();

    private const long MaxBytes = 6 * 1024 * 1024; // 6 MB per image

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
    };

    // The only categories that may be written/read, so a caller can never reach an arbitrary subfolder.
    // "avatars" holds public images (profile pictures, site/admin imagery) served anonymously.
    private static readonly HashSet<string> Categories = new(StringComparer.Ordinal) { "kyc", "cards", "receipts", "avatars" };

    private const string PublicCategory = "avatars";

    private readonly string _root;

    public LocalFileStorageService()
    {
        // Default co-locates uploaded media with store.json (see PersistentPaths) so it survives a native
        // redeploy. The old default tied this to AppContext.BaseDirectory — a per-release folder on the
        // systemd deploy — which is why uploaded images disappeared on every deploy.
        _root = Environment.GetEnvironmentVariable("PHONIX_UPLOADS_DIR")
            ?? Phonix.Api.PersistentPaths.Combine("ProtectedUploads");
        Directory.CreateDirectory(_root);
    }

    public async Task<FileSaveResult> SaveAsync(int ownerId, string category, IFormFile? file, CancellationToken ct = default)
    {
        if (ownerId <= 0) return new FileSaveResult(null, "کاربر نامعتبر است.");
        if (!Categories.Contains(category)) return new FileSaveResult(null, "نوع فایل نامعتبر است.");
        if (file is null || file.Length == 0) return new FileSaveResult(null, "فایلی انتخاب نشده است.");
        if (file.Length > MaxBytes) return new FileSaveResult(null, "حجم تصویر نباید بیشتر از ۶ مگابایت باشد.");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!ContentTypes.ContainsKey(ext)) return new FileSaveResult(null, "فقط تصویر JPG، PNG یا WebP مجاز است.");

        var id = $"{ownerId}__{Guid.NewGuid():N}{ext}";
        var dir = Path.Combine(_root, category);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, id);

        try
        {
            await WriteReencodedAsync(file, ext, path, ct);
        }
        catch (Exception)
        {
            if (File.Exists(path)) { try { File.Delete(path); } catch { /* best-effort cleanup */ } }
            // a failure here means the bytes were not a decodable image — reject rather than store anything.
            return new FileSaveResult(null, "تصویر نامعتبر است یا قابل پردازش نیست.");
        }
        return new FileSaveResult(id, null);
    }

    // Public-image counterpart of SaveAsync. Unlike protected uploads it does NOT gate on the input file
    // extension: any image SkiaSharp can decode (JPEG, PNG, WebP, GIF, BMP, …) is accepted and always
    // re-encoded to WebP. That normalizes the output, strips metadata, and neutralizes any non-image payload
    // smuggled in, so "every extension the user might pick" works while only one safe format is ever stored.
    public async Task<FileSaveResult> SavePublicImageAsync(int ownerId, IFormFile? file, CancellationToken ct = default)
    {
        if (ownerId <= 0) return new FileSaveResult(null, "کاربر نامعتبر است.");
        if (file is null || file.Length == 0) return new FileSaveResult(null, "فایلی انتخاب نشده است.");
        if (file.Length > MaxBytes) return new FileSaveResult(null, "حجم تصویر نباید بیشتر از ۶ مگابایت باشد.");

        const string ext = ".webp";
        var id = $"{ownerId}__{Guid.NewGuid():N}{ext}";
        var dir = Path.Combine(_root, PublicCategory);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, id);

        try
        {
            await WriteReencodedAsync(file, ext, path, ct);
        }
        catch (Exception)
        {
            if (File.Exists(path)) { try { File.Delete(path); } catch { /* best-effort cleanup */ } }
            return new FileSaveResult(null, "تصویر نامعتبر است یا قابل پردازش نیست.");
        }
        return new FileSaveResult(id, null);
    }

    // Decodes the upload and re-encodes it from raw pixels, which drops all metadata (EXIF geolocation,
    // ICC, etc.) and neutralizes any non-image payload smuggled into the file — nothing the user supplied
    // is written to disk verbatim. Oversized images are clamped so a decoded bitmap can't exhaust memory.
    // Throws when the bytes aren't a valid image, so the caller can reject the upload.
    private static async Task WriteReencodedAsync(IFormFile file, string ext, string path, CancellationToken ct)
    {
        byte[] bytes;
        await using (var input = file.OpenReadStream())
        using (var buffer = new MemoryStream())
        {
            await input.CopyToAsync(buffer, ct);
            bytes = buffer.ToArray();
        }

        using var decoded = SKBitmap.Decode(bytes)
            ?? throw new InvalidDataException("Unsupported or corrupt image.");

        const int maxEdge = 2400;
        SKBitmap? resized = null;
        var bitmap = decoded;
        if (decoded.Width > maxEdge || decoded.Height > maxEdge)
        {
            var scale = (double)maxEdge / Math.Max(decoded.Width, decoded.Height);
            var info = new SKImageInfo(
                Math.Max(1, (int)Math.Round(decoded.Width * scale)),
                Math.Max(1, (int)Math.Round(decoded.Height * scale)));
            resized = decoded.Resize(info, SKFilterQuality.High);
            if (resized is not null) bitmap = resized;
        }

        try
        {
            var format = ext switch
            {
                ".png" => SKEncodedImageFormat.Png,
                ".webp" => SKEncodedImageFormat.Webp,
                _ => SKEncodedImageFormat.Jpeg,
            };
            var quality = format == SKEncodedImageFormat.Png ? 100 : 88;
            using var data = bitmap.Encode(format, quality)
                ?? throw new InvalidDataException("Image could not be encoded.");

            await using var dest = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            data.SaveTo(dest);
        }
        finally
        {
            resized?.Dispose();
        }
    }

    public StoredFile? Open(string category, string id)
    {
        if (!Categories.Contains(category)) return null;
        if (string.IsNullOrWhiteSpace(id) || !IdPattern().IsMatch(id)) return null;

        var dir = Path.GetFullPath(Path.Combine(_root, category));
        var path = Path.GetFullPath(Path.Combine(dir, id));
        // defence in depth: the resolved path must stay inside the category directory.
        if (!path.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return null;
        if (!File.Exists(path)) return null;

        var contentType = ContentTypes.TryGetValue(Path.GetExtension(path), out var ct) ? ct : "application/octet-stream";
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new StoredFile(stream, contentType, id);
    }

    public void DeletePublicImageByUrl(string? urlOrId, int? requireOwner = null)
    {
        var id = ExtractId(urlOrId);
        if (id is null) return;
        // ownership guard: only remove an image whose id encodes the expected owner, so a user can't trigger
        // deletion of an image they merely referenced (e.g. a product photo set as their avatar) but don't own.
        if (requireOwner is int owner && OwnerOf(id) != owner) return;
        DeleteFromPublicFolder(id);
    }

    public void DeletePublicImageIfUnreferenced(string? urlOrId, string storeSnapshotJson)
    {
        var id = ExtractId(urlOrId);
        if (id is null) return;
        // Still referenced somewhere (another product, a showcase card, a plan's tutorial media, an avatar…)?
        // Keep it. The id is a 32-hex GUID + owner prefix, so a substring hit is an unambiguous reference.
        if (!string.IsNullOrEmpty(storeSnapshotJson) && storeSnapshotJson.Contains(id, StringComparison.Ordinal))
            return;
        DeleteFromPublicFolder(id);
    }

    public int SweepPublicOrphans(string storeSnapshotJson, TimeSpan minAge)
    {
        var deleted = 0;
        try
        {
            var dir = Path.Combine(_root, PublicCategory);
            if (!Directory.Exists(dir)) return 0;
            var cutoffUtc = DateTime.UtcNow - minAge;

            foreach (var path in Directory.EnumerateFiles(dir))
            {
                try
                {
                    var name = Path.GetFileName(path);
                    if (!IdPattern().IsMatch(name)) continue;                 // only our id-shaped files
                    if (File.GetLastWriteTimeUtc(path) > cutoffUtc) continue; // too new — maybe mid-upload
                    if (!string.IsNullOrEmpty(storeSnapshotJson)
                        && storeSnapshotJson.Contains(name, StringComparison.Ordinal)) continue; // referenced
                    File.Delete(path);
                    deleted++;
                }
                catch { /* best-effort per file: one bad file never aborts the sweep */ }
            }
        }
        catch { /* enumeration failure must never throw out of a cleanup */ }
        return deleted;
    }

    // Best-effort removal of an id from the public folder, with the same path-containment guard as Open().
    private void DeleteFromPublicFolder(string id)
    {
        try
        {
            var dir = Path.GetFullPath(Path.Combine(_root, PublicCategory));
            var path = Path.GetFullPath(Path.Combine(dir, id));
            if (!path.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return;
            File.Delete(path); // a missing file is a silent no-op
        }
        catch { /* best-effort: a leaked file must never crash a request */ }
    }

    // Pulls the storage id out of a stored avatar value, accepting both the relative URL we hand the client
    // ("/api/upload/<id>") and a bare id. Returns null when the result isn't a well-formed, safe id.
    private static string? ExtractId(string? urlOrId)
    {
        if (string.IsNullOrWhiteSpace(urlOrId)) return null;
        var candidate = urlOrId.Trim();
        var q = candidate.IndexOfAny(new[] { '?', '#' }); // drop any query/fragment
        if (q >= 0) candidate = candidate[..q];
        var slash = candidate.LastIndexOf('/');
        if (slash >= 0) candidate = candidate[(slash + 1)..];
        return IdPattern().IsMatch(candidate) ? candidate : null;
    }

    public byte[] ArchivePublicMedia() => ArchiveCategories(new[] { "avatars" }, "");
    public byte[] ArchiveSensitiveMedia() => ArchiveCategories(new[] { "kyc", "cards", "receipts" }, "");

    // A single complete archive: the full store.json plus every uploaded file (media/<category>/...).
    public byte[] ArchiveFull(string storeJson)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("store.json", CompressionLevel.Fastest);
            using (var es = entry.Open())
            using (var sw = new StreamWriter(es))
                sw.Write(storeJson);
            foreach (var category in Categories) AddCategoryToZip(zip, category, "media/");
        }
        return ms.ToArray();
    }

    private byte[] ArchiveCategories(string[] categories, string prefix)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var category in categories) AddCategoryToZip(zip, category, prefix);
        return ms.ToArray();
    }

    private void AddCategoryToZip(ZipArchive zip, string category, string prefix)
    {
        var dir = Path.Combine(_root, category);
        if (!Directory.Exists(dir)) return;
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            var entry = zip.CreateEntry($"{prefix}{category}/{Path.GetFileName(file)}", CompressionLevel.Fastest);
            using var es = entry.Open();
            using var fs = File.OpenRead(file);
            fs.CopyTo(es);
        }
    }

    // Extracts uploaded files from a backup zip back into the uploads root, hardened against Zip-Slip: only
    // files whose final path stays inside an allowed category folder are written; everything else is skipped.
    // Returns the number of files restored.
    public int ExtractMediaArchive(byte[] zipBytes)
    {
        var restored = 0;
        using var ms = new MemoryStream(zipBytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue; // directory entry
            var parts = entry.FullName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            var category = parts[^2];
            if (!Categories.Contains(category)) continue; // only known media folders

            var fileName = Path.GetFileName(parts[^1]);
            if (string.IsNullOrEmpty(fileName)) continue;

            var dir = Path.GetFullPath(Path.Combine(_root, category));
            var dest = Path.GetFullPath(Path.Combine(dir, fileName));
            if (!dest.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue; // zip-slip guard

            Directory.CreateDirectory(dir);
            using (var es = entry.Open())
            using (var fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None))
                es.CopyTo(fs);
            restored++;
        }
        return restored;
    }

    public int? OwnerOf(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var m = IdPattern().Match(id);
        return m.Success && int.TryParse(m.Groups["owner"].Value, out var owner) ? owner : null;
    }

    // ── Cluster media sync (Fix 4) ────────────────────────────────────────────────────────────────────
    // Filenames are unguessable GUID-shaped ids that never change once written, so name equality already
    // implies same content; the SHA-256 is the integrity check the receiver verifies before trusting a
    // transferred file. Enumerated across every known category. Best-effort per file; never throws.
    public IReadOnlyList<MediaSyncEntry> ListMediaForSync()
    {
        var result = new List<MediaSyncEntry>();
        foreach (var category in Categories)
        {
            var dir = Path.Combine(_root, category);
            if (!Directory.Exists(dir)) continue;
            foreach (var path in Directory.EnumerateFiles(dir))
            {
                try
                {
                    var name = Path.GetFileName(path);
                    var bytes = File.ReadAllBytes(path);
                    result.Add(new MediaSyncEntry(category, name, bytes.LongLength, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))));
                }
                catch { /* a file being written / removed mid-scan just isn't advertised this cycle */ }
            }
        }
        return result;
    }

    public byte[]? ReadRawForSync(string category, string name)
    {
        if (!Categories.Contains(category)) return null;
        if (string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name) return null; // no separators/traversal
        var dir = Path.GetFullPath(Path.Combine(_root, category));
        var path = Path.GetFullPath(Path.Combine(dir, name));
        if (!path.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return null;
        if (!File.Exists(path)) return null;
        try { return File.ReadAllBytes(path); } catch { return null; }
    }

    public bool WriteRawFromSync(string category, string name, byte[] content, string expectedSha256)
    {
        if (!Categories.Contains(category)) return false;
        if (string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name) return false;
        // Integrity gate: never write bytes that don't hash to what the peer advertised.
        var actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content));
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase)) return false;

        var dir = Path.GetFullPath(Path.Combine(_root, category));
        var path = Path.GetFullPath(Path.Combine(dir, name));
        if (!path.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return false;
        if (File.Exists(path)) return false; // immutable ids: already have it, never overwrite, never delete

        Directory.CreateDirectory(dir);
        // Write to a temp file then move, so a crash mid-transfer never leaves a truncated file under a real id.
        var tmp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(tmp, content);
            File.Move(tmp, path);
            return true;
        }
        catch
        {
            if (File.Exists(tmp)) { try { File.Delete(tmp); } catch { /* best-effort */ } }
            return false;
        }
    }
}

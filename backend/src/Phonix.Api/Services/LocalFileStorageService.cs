using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

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
    private static readonly HashSet<string> Categories = new(StringComparer.Ordinal) { "kyc", "cards", "receipts" };

    private readonly string _root;

    public LocalFileStorageService()
    {
        _root = Environment.GetEnvironmentVariable("PHONIX_UPLOADS_DIR")
            ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "ProtectedUploads");
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
            await using var dest = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await file.CopyToAsync(dest, ct);
        }
        catch (Exception)
        {
            if (File.Exists(path)) { try { File.Delete(path); } catch { /* best-effort cleanup */ } }
            return new FileSaveResult(null, "ذخیره فایل ناموفق بود.");
        }
        return new FileSaveResult(id, null);
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

    public int? OwnerOf(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var m = IdPattern().Match(id);
        return m.Success && int.TryParse(m.Groups["owner"].Value, out var owner) ? owner : null;
    }
}

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

    public int? OwnerOf(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var m = IdPattern().Match(id);
        return m.Success && int.TryParse(m.Groups["owner"].Value, out var owner) ? owner : null;
    }
}

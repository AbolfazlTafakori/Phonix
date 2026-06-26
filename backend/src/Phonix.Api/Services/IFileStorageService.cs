using Microsoft.AspNetCore.Http;

namespace Phonix.Api.Services;

public sealed record FileSaveResult(string? Id, string? Error);
public sealed record StoredFile(Stream Content, string ContentType, string DownloadName);
public sealed record FileUploadResult(string Id);

// Stores user-uploaded identity images (KYC national-IDs, selfies, bank-card photos) as raw files in a
// directory OUTSIDE the web root, so nothing is ever served statically. Stored files are only returned
// through an authenticated, ownership-checked controller endpoint.
public interface IFileStorageService
{
    // Persists an uploaded image for an owner under a category, returning an opaque storage id that encodes
    // the owner (for authorization) plus a random component (so ids can't be enumerated), or an error.
    Task<FileSaveResult> SaveAsync(int ownerId, string category, IFormFile? file, CancellationToken ct = default);

    // Persists a PUBLIC image (e.g. avatars, site/admin imagery). Accepts any decodable image format and
    // always re-encodes to a single normalized output, so the input file extension is irrelevant. The
    // returned id is streamed back from an anonymous endpoint (these images are not access-controlled).
    Task<FileSaveResult> SavePublicImageAsync(int ownerId, IFormFile? file, CancellationToken ct = default);

    // Opens a stored file for streaming, or null when the id is malformed or the file is missing.
    StoredFile? Open(string category, string id);

    // The owner id encoded in a storage id, or null when the id is malformed.
    int? OwnerOf(string id);

    // Zips the public images (avatars, product/banner/blog images) for a manual media backup.
    byte[] ArchivePublicMedia();

    // Zips the sensitive identity/financial documents (KYC, bank cards, receipts) for a manual media backup.
    byte[] ArchiveSensitiveMedia();

    // A single complete archive: full store.json + every uploaded file (under media/<category>/).
    byte[] ArchiveFull(string storeJson);

    // Restores uploaded files from a backup zip (Zip-Slip hardened). Returns the number of files written.
    int ExtractMediaArchive(byte[] zipBytes);
}

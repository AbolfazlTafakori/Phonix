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

    // Opens a stored file for streaming, or null when the id is malformed or the file is missing.
    StoredFile? Open(string category, string id);

    // The owner id encoded in a storage id, or null when the id is malformed.
    int? OwnerOf(string id);
}

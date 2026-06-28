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

    // Best-effort delete of a stored PUBLIC image (e.g. a now-orphaned avatar) given its stored value —
    // either the relative URL handed to the client ("/api/upload/<id>") or a bare id. NEVER throws and does
    // no more than a single local filesystem call; an already-missing file is a silent no-op. When
    // requireOwner is supplied, the file is deleted ONLY if its id encodes that owner, so a user can't cause
    // deletion of an image they merely referenced (e.g. a product photo set as their avatar) but don't own.
    void DeletePublicImageByUrl(string? urlOrId, int? requireOwner = null);

    // Best-effort delete of an orphaned public image, but ONLY when its id is no longer referenced anywhere
    // in the supplied store snapshot (pass StoreData.SerializeSnapshot()). This is the SAFE variant for ADMIN
    // images (product photos, banners, showcase logos, plan tutorial media…), which — unlike a personal
    // avatar — may legitimately be shared across several entities; an id still present in the snapshot is
    // kept. NEVER throws; does no more than a single local filesystem call.
    void DeletePublicImageIfUnreferenced(string? urlOrId, string storeSnapshotJson);

    // One-shot reclamation: deletes every file in the public-image folder that is BOTH (a) older than minAge
    // — so a file still being wired into the store (uploaded, URL not yet saved) is never swept — AND (b) not
    // referenced anywhere in the store snapshot. Returns the number of files deleted. Best-effort per file;
    // never throws.
    int SweepPublicOrphans(string storeSnapshotJson, TimeSpan minAge);

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

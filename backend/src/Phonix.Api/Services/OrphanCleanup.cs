using Phonix.Api.Data;

namespace Phonix.Api.Services;

// Fire-and-forget, reference-guarded cleanup of public images that an admin update/delete just orphaned.
// Used for ADMIN imagery (products, banners, showcase) where the owner-guard used for avatars doesn't apply,
// because the same image id can legitimately be shared across entities. Each candidate is removed only if it
// is no longer referenced anywhere in the store, so a shared image is never deleted.
public static class OrphanCleanup
{
    // Queues deletion of the given (pre-mutation) image URLs. Returns immediately; all work — including
    // serializing the post-mutation store snapshot — runs off the request thread. The underlying delete
    // never throws, so the dispatched task can never fault (no unobserved exceptions, no thread-pool churn
    // beyond a single short-lived work item).
    public static void Queue(IFileStorageService files, IDataStore store, params string?[] oldUrls)
    {
        if (oldUrls is null) return;
        var candidates = oldUrls.Where(u => !string.IsNullOrEmpty(u)).Distinct().ToArray();
        if (candidates.Length == 0) return;

        _ = Task.Run(() =>
        {
            // Captured AFTER the mutation completed (the store call returned synchronously), so the snapshot
            // reflects the new state: a genuinely replaced/removed image is no longer referenced and is freed,
            // while an id still in use elsewhere is kept.
            var snapshot = store.SerializeSnapshot();
            foreach (var url in candidates) files.DeletePublicImageIfUnreferenced(url, snapshot);
        });
    }
}

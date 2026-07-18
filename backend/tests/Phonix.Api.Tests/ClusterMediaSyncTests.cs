using System.Security.Cryptography;
using Phonix.Api.Services;
using Xunit;

namespace Phonix.Api.Tests;

// Fix 4: media-file synchronization on LocalFileStorageService — the manifest a Standby diffs against, the
// checksum integrity gate, and the "never overwrite / never delete" guarantee. Runs the two roots (a mock
// "Primary" and "Standby" uploads dir) through the exact code path the cluster sync loop uses.
public sealed class ClusterMediaSyncTests
{
    private static LocalFileStorageService StoreWithRoot(out string root)
    {
        root = Path.Combine(Path.GetTempPath(), "phonix-media-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("PHONIX_UPLOADS_DIR", root);
        return new LocalFileStorageService();
    }

    private static string PlaceFile(string root, string category, string name, byte[] bytes)
    {
        var dir = Path.Combine(root, category);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, bytes);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    [Fact]
    public void Manifest_lists_files_with_checksums_and_standby_pulls_only_what_it_is_missing()
    {
        var primary = StoreWithRoot(out var primaryRoot);
        var bytesA = new byte[] { 1, 2, 3, 4, 5 };
        var bytesB = new byte[] { 9, 8, 7 };
        var hashA = PlaceFile(primaryRoot, "avatars", "1__" + new string('a', 32) + ".webp", bytesA);
        var hashB = PlaceFile(primaryRoot, "kyc", "2__" + new string('b', 32) + ".jpg", bytesB);

        var manifest = primary.ListMediaForSync();
        Assert.Equal(2, manifest.Count);
        Assert.Contains(manifest, e => e.Category == "avatars" && e.Sha256 == hashA && e.Size == bytesA.Length);
        Assert.Contains(manifest, e => e.Category == "kyc" && e.Sha256 == hashB);

        // A fresh Standby has neither file — it pulls both, verified by checksum, then a re-run is a no-op.
        var standby = StoreWithRoot(out _);
        var pulled = 0;
        foreach (var e in manifest)
        {
            var raw = primary.ReadRawForSync(e.Category, e.Name);
            Assert.NotNull(raw);
            if (standby.WriteRawFromSync(e.Category, e.Name, raw!, e.Sha256)) pulled++;
        }
        Assert.Equal(2, pulled);
        Assert.Equal(2, standby.ListMediaForSync().Count);

        // Incremental: nothing new to pull on the second pass (the file already exists, same checksum).
        foreach (var e in manifest)
            Assert.False(standby.WriteRawFromSync(e.Category, e.Name, primary.ReadRawForSync(e.Category, e.Name)!, e.Sha256));
    }

    [Fact]
    public void WriteRawFromSync_rejects_a_corrupt_transfer_and_never_overwrites_or_deletes()
    {
        var standby = StoreWithRoot(out var root);
        var good = new byte[] { 10, 20, 30 };
        var name = "3__" + new string('c', 32) + ".png";
        var goodHash = Convert.ToHexString(SHA256.HashData(good));

        // Corrupt bytes whose hash doesn't match the advertised checksum are refused — never written.
        Assert.False(standby.WriteRawFromSync("avatars", name, new byte[] { 99, 99 }, goodHash));
        Assert.Empty(standby.ListMediaForSync());

        // A valid transfer writes exactly once; a second attempt for the same immutable id is a no-op (kept).
        Assert.True(standby.WriteRawFromSync("avatars", name, good, goodHash));
        Assert.False(standby.WriteRawFromSync("avatars", name, good, goodHash));

        // The existing valid file is still present and untouched — sync never deletes a valid local file.
        var stored = File.ReadAllBytes(Path.Combine(root, "avatars", name));
        Assert.Equal(good, stored);

        // Path-traversal / unknown category attempts are rejected.
        Assert.Null(standby.ReadRawForSync("../secrets", name));
        Assert.False(standby.WriteRawFromSync("avatars", "../escape.png", good, goodHash));
    }

    // The peer controls both the category and the file name on every media transfer, and it reaches these
    // methods straight off the wire. A static analyser flags the Path.Combine calls here because the
    // sanitising happens in a guard rather than inline, so the containment is pinned down explicitly:
    // nothing in this set may read or write a single byte outside the uploads root.
    [Theory]
    [InlineData("avatars", "../escape.png")]
    [InlineData("avatars", "../../escape.png")]
    [InlineData("avatars", "..\\escape.png")]
    [InlineData("avatars", "sub/escape.png")]
    [InlineData("avatars", "sub\\escape.png")]
    [InlineData("avatars", "/etc/passwd")]
    [InlineData("avatars", "C:\\Windows\\win.ini")]
    [InlineData("avatars", "")]
    [InlineData("avatars", ".")]
    [InlineData("avatars", "..")]
    [InlineData("../secrets", "a.png")]
    [InlineData("..", "a.png")]
    [InlineData("/etc", "passwd")]
    [InlineData("avatars/../../etc", "passwd")]
    [InlineData("", "a.png")]
    public void Media_sync_never_escapes_the_uploads_root(string category, string name)
    {
        var standby = StoreWithRoot(out var root);
        var payload = new byte[] { 1, 2, 3 };
        var hash = Convert.ToHexString(SHA256.HashData(payload));

        Assert.Null(standby.ReadRawForSync(category, name));
        Assert.False(standby.WriteRawFromSync(category, name, payload, hash));

        // Nothing was created anywhere outside the root — including no stray temp files inside it.
        Assert.Empty(standby.ListMediaForSync());
        Assert.Empty(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories));
    }
}

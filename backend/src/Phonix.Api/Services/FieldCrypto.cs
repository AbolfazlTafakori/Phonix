using System.Security.Cryptography;
using System.Text;

namespace Phonix.Api.Services;

// Always-on AES-256-GCM encryption for sensitive fields at rest (stock account passwords, sensitive checkout
// inputs) — unlike BackupCrypto, this needs no admin-supplied passphrase, so it can never silently degrade to
// plaintext because an operator forgot to set an environment variable. The key is 32 random bytes generated on
// first use and persisted next to the Data Protection key ring (PHONIX_KEYS_DIR, or PersistentPaths' "keys"
// folder) — same durability guarantee, same "only the app's own filesystem access can reach it" trust boundary.
// Output is a single ASCII container so it travels as text: "PHXF1." + base64(nonce|tag|cipher).
public static class FieldCrypto
{
    private const string Prefix = "PHXF1.";
    private const int NonceSize = 12, TagSize = 16, KeySize = 32;
    private const string KeyFileName = "field.key";

    private static readonly Lazy<byte[]> LazyKey = new(LoadOrCreateKey, isThreadSafe: true);

    private static string KeyDir() =>
        Environment.GetEnvironmentVariable("PHONIX_KEYS_DIR") ?? Phonix.Api.PersistentPaths.Combine("keys");

    private static byte[] LoadOrCreateKey()
    {
        var dir = KeyDir();
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, KeyFileName);

        if (File.Exists(path))
        {
            var existing = Convert.FromBase64String(File.ReadAllText(path).Trim());
            if (existing.Length == KeySize) return existing;
            // A corrupt/short key file must never silently fall through to plaintext — refuse to start instead.
            throw new InvalidOperationException($"{path} does not contain a valid {KeySize}-byte key.");
        }

        var key = RandomNumberGenerator.GetBytes(KeySize);
        // Written once, atomically-enough for a single-writer startup path; the containing "keys" folder is
        // already private to the app's own OS user, the same boundary the Data Protection key ring relies on.
        File.WriteAllText(path, Convert.ToBase64String(key));
        return key;
    }

    public static bool LooksEncrypted(string content) => content.StartsWith(Prefix, StringComparison.Ordinal);

    public static string Encrypt(string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(LazyKey.Value, TagSize))
            aes.Encrypt(nonce, plain, cipher, tag);

        var blob = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, blob, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, blob, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, blob, NonceSize + TagSize, cipher.Length);
        return Prefix + Convert.ToBase64String(blob);
    }

    // Returns the decrypted plaintext, or null when the container is malformed or tampered with.
    public static string? Decrypt(string container)
    {
        if (!LooksEncrypted(container)) return null;
        try
        {
            var blob = Convert.FromBase64String(container[Prefix.Length..]);
            if (blob.Length < NonceSize + TagSize) return null;

            var nonce = blob.AsSpan(0, NonceSize).ToArray();
            var tag = blob.AsSpan(NonceSize, TagSize).ToArray();
            var cipher = blob.AsSpan(NonceSize + TagSize).ToArray();

            var plain = new byte[cipher.Length];
            using (var aes = new AesGcm(LazyKey.Value, TagSize))
                aes.Decrypt(nonce, cipher, tag, plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException) { return null; }
        catch (FormatException) { return null; }
    }
}

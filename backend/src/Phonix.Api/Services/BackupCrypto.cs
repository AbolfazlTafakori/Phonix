using System.Security.Cryptography;
using System.Text;

namespace Phonix.Api.Services;

// Encrypts the store backup with AES-256-GCM before it ever leaves the server (download or Telegram). The
// key is derived from the PHONIX_BACKUP_KEY passphrase via PBKDF2; a random salt + nonce are generated per
// file. Output is a single ASCII container so it travels as text: "PHX1." + base64(salt|nonce|tag|cipher).
// When no passphrase is configured, encryption is disabled and the backup is plain JSON (restore handles
// both, so an older plain backup still imports).
public static class BackupCrypto
{
    private const string Prefix = "PHX1.";
    private const int SaltSize = 16, NonceSize = 12, TagSize = 16, KeySize = 32, Iterations = 100_000;

    private static string? Passphrase => Environment.GetEnvironmentVariable("PHONIX_BACKUP_KEY") is { } k && !string.IsNullOrWhiteSpace(k) ? k : null;

    public static bool IsEnabled => Passphrase is not null;

    public static bool LooksEncrypted(string content) => content.StartsWith(Prefix, StringComparison.Ordinal);

    public static string Encrypt(string plaintext)
    {
        var pass = Passphrase ?? throw new InvalidOperationException("PHONIX_BACKUP_KEY is not configured.");
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(pass, salt, Iterations, HashAlgorithmName.SHA256, KeySize);

        var plain = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(key, TagSize))
            aes.Encrypt(nonce, plain, cipher, tag);

        var blob = new byte[SaltSize + NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(salt, 0, blob, 0, SaltSize);
        Buffer.BlockCopy(nonce, 0, blob, SaltSize, NonceSize);
        Buffer.BlockCopy(tag, 0, blob, SaltSize + NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, blob, SaltSize + NonceSize + TagSize, cipher.Length);
        return Prefix + Convert.ToBase64String(blob);
    }

    // Returns the decrypted JSON, or null when the passphrase is missing/wrong or the container is malformed.
    public static string? Decrypt(string container)
    {
        if (!LooksEncrypted(container) || Passphrase is not { } pass) return null;
        try
        {
            var blob = Convert.FromBase64String(container[Prefix.Length..]);
            if (blob.Length < SaltSize + NonceSize + TagSize) return null;

            var salt = blob.AsSpan(0, SaltSize).ToArray();
            var nonce = blob.AsSpan(SaltSize, NonceSize).ToArray();
            var tag = blob.AsSpan(SaltSize + NonceSize, TagSize).ToArray();
            var cipher = blob.AsSpan(SaltSize + NonceSize + TagSize).ToArray();

            var key = Rfc2898DeriveBytes.Pbkdf2(pass, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
            var plain = new byte[cipher.Length];
            using (var aes = new AesGcm(key, TagSize))
                aes.Decrypt(nonce, cipher, tag, plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            return null; // wrong key or tampered payload
        }
        catch (FormatException)
        {
            return null; // not valid base64
        }
    }
}

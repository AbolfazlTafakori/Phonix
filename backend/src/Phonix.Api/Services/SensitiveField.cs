namespace Phonix.Api.Services;

// Encrypts individual sensitive customer inputs (e.g. an account password supplied at checkout) at rest,
// reusing the AES-GCM container from BackupCrypto so they never sit in store.json or a plain backup as
// readable text. When no PHONIX_BACKUP_KEY is configured it degrades to plaintext — same trade-off the
// backup system already makes — so the feature still works on an unconfigured dev box.
public static class SensitiveField
{
    public static string Protect(string value) =>
        BackupCrypto.IsEnabled && !string.IsNullOrEmpty(value) ? BackupCrypto.Encrypt(value) : value;

    public static string Reveal(string value) =>
        BackupCrypto.LooksEncrypted(value) ? BackupCrypto.Decrypt(value) ?? value : value;
}

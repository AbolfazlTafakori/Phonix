namespace Phonix.Api.Services;

// Encrypts individual sensitive customer inputs (e.g. an account password supplied at checkout, a stock
// account's live credentials) at rest via FieldCrypto, so they never sit in store.json/the SQLite DataJson
// column as readable text. Unlike the old scheme this needs no operator-supplied passphrase — FieldCrypto's
// key is generated and persisted on first run — so Protect() ALWAYS encrypts; there is no plaintext fallback.
// Reveal() stays backward-compatible with values written under the old, optional BackupCrypto-keyed scheme
// (only ever produced when PHONIX_BACKUP_KEY happened to be set) and with genuinely legacy plaintext values
// from before this field was encrypted at all — both are returned as-is / decrypted with the matching scheme.
public static class SensitiveField
{
    public static string Protect(string value) =>
        string.IsNullOrEmpty(value) ? value : FieldCrypto.Encrypt(value);

    public static string Reveal(string value)
    {
        if (FieldCrypto.LooksEncrypted(value)) return FieldCrypto.Decrypt(value) ?? value;
        if (BackupCrypto.LooksEncrypted(value)) return BackupCrypto.Decrypt(value) ?? value; // legacy scheme
        return value; // legacy plaintext, predating encryption of this field
    }
}

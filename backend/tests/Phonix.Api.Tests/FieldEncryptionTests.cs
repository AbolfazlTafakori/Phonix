using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Services;
using Xunit;

namespace Phonix.Api.Tests;

// Sensitive fields (stock account passwords, sensitive checkout inputs) must ALWAYS be encrypted at rest —
// unlike BackupCrypto, this never depends on an operator-configured passphrase, so it can't silently degrade
// to plaintext. Reveal must still read back both legacy schemes so already-stored data keeps working.
public class FieldEncryptionTests
{
    [Fact]
    public void Protect_always_encrypts_even_with_no_backup_key_configured()
    {
        var previous = Environment.GetEnvironmentVariable("PHONIX_BACKUP_KEY");
        Environment.SetEnvironmentVariable("PHONIX_BACKUP_KEY", null); // simulate the common unconfigured deploy
        try
        {
            var protectedValue = SensitiveField.Protect("super-secret-password");

            Assert.NotEqual("super-secret-password", protectedValue); // never stored as plaintext
            Assert.True(FieldCrypto.LooksEncrypted(protectedValue));
            Assert.Equal("super-secret-password", SensitiveField.Reveal(protectedValue)); // round-trips
        }
        finally
        {
            Environment.SetEnvironmentVariable("PHONIX_BACKUP_KEY", previous);
        }
    }

    [Fact]
    public void Reveal_still_returns_genuinely_legacy_plaintext_values()
    {
        // A value stored before this field was ever encrypted (no recognizable container prefix) must still
        // display correctly — Reveal must not corrupt or reject it.
        Assert.Equal("old-plain-password", SensitiveField.Reveal("old-plain-password"));
    }

    [Fact]
    public void Newly_added_stock_accounts_never_store_a_plaintext_password()
    {
        var store = TestStore.Create();
        var acc = store.AddStockAccount(new StockAccount
        {
            ProductId = 1, Username = "u@mail.com", Password = SensitiveField.Protect("plain-text-pass"),
            Plan = "P", Capacity = 1, Months = 1,
        });

        var stored = store.GetStockAccount(acc.Id)!.Password;
        Assert.NotEqual("plain-text-pass", stored);
        Assert.True(FieldCrypto.LooksEncrypted(stored));
        Assert.Equal("plain-text-pass", SensitiveField.Reveal(stored));
    }

    [Fact]
    public void Migration_re_encrypts_existing_plaintext_stock_passwords_and_is_idempotent()
    {
        var dir = Path.Combine(Path.GetTempPath(), "phonix-sqlite-tests");
        Directory.CreateDirectory(dir);
        var store = new SqliteDataStore(Path.Combine(dir, Guid.NewGuid() + ".db"));

        // Simulate an account saved before encryption was mandatory: write the password straight through,
        // bypassing SensitiveField.Protect (as the old code path could when PHONIX_BACKUP_KEY was unset).
        var acc = store.AddStockAccount(new StockAccount
        {
            ProductId = 1, Username = "legacy@mail.com", Password = "totally-plain-password",
            Plan = "P", Capacity = 1, Months = 1,
        });
        Assert.Equal("totally-plain-password", store.GetStockAccount(acc.Id)!.Password); // confirms the setup

        var migrated = store.MigratePlaintextStockPasswords();
        Assert.Equal(1, migrated);

        var afterFirst = store.GetStockAccount(acc.Id)!.Password;
        Assert.NotEqual("totally-plain-password", afterFirst);
        Assert.True(FieldCrypto.LooksEncrypted(afterFirst));
        Assert.Equal("totally-plain-password", SensitiveField.Reveal(afterFirst)); // still readable

        // A second run must be a no-op — the password is already encrypted, not double-wrapped.
        Assert.Equal(0, store.MigratePlaintextStockPasswords());
        Assert.Equal(afterFirst, store.GetStockAccount(acc.Id)!.Password);
    }
}

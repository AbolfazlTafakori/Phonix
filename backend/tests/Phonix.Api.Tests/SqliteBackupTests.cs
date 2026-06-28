using Phonix.Api.Data;
using Phonix.Api.Models;
using Xunit;

namespace Phonix.Api.Tests;

// Verifies the SqliteDataStore snapshot/backup bridge: JSON snapshot round-trip, cross-format restore from a
// JSON store.json, and the live VACUUM INTO single-file backup.
public class SqliteBackupTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "phonix-sqlite-tests");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static SqliteDataStore FreshStore() => new(Path.Combine(TempDir(), Guid.NewGuid() + ".db"));

    [Fact]
    public void Snapshot_round_trips_through_serialize_and_load()
    {
        var a = FreshStore();
        a.UpdateSettings(new PricingSettings { VatPercent = 9m, ReferralCommissionPercent = 10m });
        var user = a.RegisterUser(new AppUser { Username = "alice", Name = "Alice", VerificationLevel = 2, Wallet = 123 });
        var product = a.AddProduct(new Product { Name = "Prod", CategoryId = 1, Price = 50_000, Stock = 7, RequiredLevel = 1, IsActive = true });
        var placed = a.PlaceOrder(user, new[] { (product.Id, 1, (int?)null) }, "test", fromWallet: false);
        Assert.Null(placed.Error);

        var json = a.SerializeSnapshot();

        // Restore into a brand-new, empty database.
        var b = FreshStore();
        b.LoadSnapshot(b.DeserializeSnapshot(json)!);

        Assert.Equal("Alice", b.GetUser(user.Id)!.Name);
        Assert.Equal(123, b.GetUser(user.Id)!.Wallet);
        Assert.Equal(6, b.GetProduct(product.Id)!.Stock); // 7 - 1 (the order placed before the snapshot)
        Assert.Single(b.GetOrders());
        Assert.Equal(9m, b.GetSettings().VatPercent);
    }

    [Fact]
    public void Restore_accepts_a_snapshot_produced_by_the_json_store()
    {
        // A real store.json (the JSON implementation, seeded) must restore cleanly into SQLite — proving the
        // two backends share one backup format.
        var jsonStore = TestStore.Create();
        var snapshotJson = jsonStore.SerializeSnapshot();

        var sqlite = FreshStore();
        sqlite.LoadSnapshot(sqlite.DeserializeSnapshot(snapshotJson)!);

        var seeded = sqlite.GetUser(1); // seed: user 1 = ali
        Assert.NotNull(seeded);
        Assert.Equal("ali", seeded!.Username);
        Assert.True(sqlite.GetUsers().Count >= 6); // the seed creates six users
    }

    [Fact]
    public void BackupToFile_produces_a_consistent_db_usable_while_live()
    {
        var live = FreshStore();
        live.RegisterUser(new AppUser { Username = "bob", Name = "Bob" });
        live.AddProduct(new Product { Name = "X", CategoryId = 1, Price = 1000, Stock = 3, RequiredLevel = 1, IsActive = true });

        var dest = Path.Combine(TempDir(), Guid.NewGuid() + "-backup.db");
        var written = live.BackupToFile(dest);

        Assert.True(File.Exists(written));

        // Open the backed-up file as its own store — proves it's a complete, consistent database, not a
        // half-written copy, and that no app downtime/restore step is needed beyond opening the file.
        var restored = new SqliteDataStore(written);
        Assert.Equal("Bob", restored.GetUserByUsername("bob")!.Name);
        Assert.Single(restored.GetProducts());
    }
}

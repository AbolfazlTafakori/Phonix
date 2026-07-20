using Phonix.Api.Data;

namespace Phonix.Api.Tests;

internal static class TestStore
{
    // A fresh, seeded SQLite store on a unique temp file — the same implementation the application runs on, so
    // a passing test says something about what actually ships. Tests never touch real data and never see each
    // other's mutations.
    public static IDataStore Create() => Create(out _);

    // Hands back the database file too, for the tests that reopen a store to prove a write survived a restart.
    public static IDataStore Create(out string dbPath)
    {
        var dir = Path.Combine(Path.GetTempPath(), "phonix-tests");
        Directory.CreateDirectory(dir);
        dbPath = Path.Combine(dir, Guid.NewGuid() + ".db");
        return TestSeed.Apply(new SqliteDataStore(dbPath));
    }

    // Reopens an existing database — a "restart" against the same file.
    public static IDataStore Reopen(string dbPath) => new SqliteDataStore(dbPath);
}

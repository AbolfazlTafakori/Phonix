using Phonix.Api.Data;

namespace Phonix.Api.Tests;

internal static class TestStore
{
    // A fresh, seeded store backed by a unique temp file so tests never touch real data and
    // never see each other's mutations.
    public static StoreData Create()
    {
        var path = Path.Combine(Path.GetTempPath(), "phonix-tests", Guid.NewGuid() + ".json");
        Environment.SetEnvironmentVariable("PHONIX_DATA_FILE", path);
        return new StoreData();
    }
}

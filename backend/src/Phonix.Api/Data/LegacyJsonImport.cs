using System.Text.Json;
using System.Text.Json.Serialization;

namespace Phonix.Api.Data;

// The one-time import of a pre-SQLite install's store.json.
//
// That file was always a serialized StoreSnapshot, which is the same shape LoadSnapshot already restores — so
// reading it needs nothing more than the matching serializer options. The whole JSON store implementation used
// to be dragged in just to perform this read; keeping the format knowledge here is what let it go.
public static class LegacyJsonImport
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    // Where a legacy install kept its data. Same environment variable the JSON store honoured.
    public static string FilePath =>
        Environment.GetEnvironmentVariable("PHONIX_DATA_FILE") is { Length: > 0 } p
            ? p
            : Path.Combine(AppContext.BaseDirectory, "store.json");

    // The snapshot held in a legacy file, or null when there is nothing to import (no file, unreadable, or
    // not a snapshot). Never throws: a failed import must not stop the application from starting.
    public static StoreSnapshot? Read(out string path)
    {
        path = FilePath;
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<StoreSnapshot>(File.ReadAllText(path), Options);
        }
        catch
        {
            return null;
        }
    }
}

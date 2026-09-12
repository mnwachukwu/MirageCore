using Mirage.Shared.Records;
using System.Text.Json;

namespace Mirage.Client.Core.Cache;

/// <summary>
/// Persists maps as JSON files under <c>{directory}/map{N}.json</c>.
/// Keeps an in-memory revision index so revision checks don't require disk I/O.
/// The directory is supplied by the caller (a per-user writable location) rather than resolved
/// here, so this cache makes no assumption about the process's working directory.
///
/// <para>Entries are stamped with <see cref="FormatVersion"/> and the whole cache is dropped when it
/// changes. A map's <c>revision</c> tracks what an AUTHOR changed, so it cannot catch a change to what the
/// file MEANS — and the record converters fall back rather than throw, so a renamed tile type would turn
/// every door in the cache into open floor with nothing to report it.</para>
/// </summary>
public sealed class DiskMapCache : IMapCache
{
    /// <summary>Bump whenever a record's on-disk SHAPE changes in a way an old cached file would be
    /// misread under: a renamed or removed enum member, a retyped field, a changed default. Adding a field
    /// needs no bump — an absent one already reads as its default.</summary>
    public const int FormatVersion = 2;

    private const string VersionFileName = "format.txt";

    private readonly string _directory;
    private readonly Dictionary<int, int> _revisions = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public DiskMapCache(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
        DropCacheIfStale();
        IndexExistingRevisions();
    }

    public int GetCachedRevision(int mapNum) =>
        _revisions.TryGetValue(mapNum, out int rev) ? rev : -1;

    public async Task<MapRecord?> LoadAsync(int mapNum)
    {
        string path = MapPath(mapNum);
        if (!File.Exists(path)) return null;
        try
        {
            await using var fs = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<MapRecord>(fs, Options);
        }
        catch { return null; }
    }

    public async Task SaveAsync(int mapNum, MapRecord map)
    {
        string path = MapPath(mapNum);
        await using var fs = File.Create(path);
        await JsonSerializer.SerializeAsync(fs, map, Options);
        _revisions[mapNum] = map.Revision;
    }

    private string MapPath(int mapNum) =>
        Path.Combine(_directory, $"map{mapNum}.json");

    // A cache written under a different format is not repaired, it is discarded: every entry is re-fetched
    // on demand, so the cost of being wrong here is one refill and the cost of being clever is a subtly
    // wrong map.
    private void DropCacheIfStale()
    {
        string marker = Path.Combine(_directory, VersionFileName);
        try
        {
            if (File.Exists(marker) &&
                int.TryParse(File.ReadAllText(marker).Trim(), out int found) &&
                found == FormatVersion)
                return;

            foreach (string file in Directory.GetFiles(_directory, "map*.json"))
                File.Delete(file);

            File.WriteAllText(marker, FormatVersion.ToString());
        }
        catch { /* an unreadable cache directory is a cache miss, not a failure to start */ }
    }

    private void IndexExistingRevisions()
    {
        foreach (string file in Directory.GetFiles(_directory, "map*.json"))
        {
            try
            {
                using var fs = File.OpenRead(file);
                using var doc = JsonDocument.Parse(fs);
                if (!doc.RootElement.TryGetProperty("revision", out var rev)) continue;
                string name = Path.GetFileNameWithoutExtension(file);
                if (int.TryParse(name.AsSpan(3), out int mapNum))
                    _revisions[mapNum] = rev.GetInt32();
            }
            catch { /* corrupt cache entry — skip */ }
        }
    }
}

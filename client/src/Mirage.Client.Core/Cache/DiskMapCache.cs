using Mirage.Shared.Records;
using System.Text.Json;

namespace Mirage.Client.Core.Cache;

/// <summary>
/// Persists maps as JSON files under <c>{directory}/map{N}.json</c>.
/// Keeps an in-memory revision index so revision checks don't require disk I/O.
/// The directory is supplied by the caller (a per-user writable location) rather than resolved
/// here, so this cache makes no assumption about the process's working directory.
/// </summary>
public sealed class DiskMapCache : IMapCache
{
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

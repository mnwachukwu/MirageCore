using Mirage.Shared.Records;

namespace Mirage.Client.Core.Cache;

public interface IMapCache
{
    /// <summary>Returns the cached revision for <paramref name="mapNum"/>, or -1 if not cached.</summary>
    int GetCachedRevision(int mapNum);

    Task<MapRecord?> LoadAsync(int mapNum);
    Task SaveAsync(int mapNum, MapRecord map);
}

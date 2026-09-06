using CommunityToolkit.Mvvm.ComponentModel;
using Mirage.Shared;
using Mirage.Shared.Records;

namespace Mirage.Editor.ViewModels;

/// <summary>How many maps and how many tiles a sheet is painted on, across the whole world.</summary>
/// <param name="Maps">Maps with at least one cell on this sheet.</param>
/// <param name="Tiles">Layer cells on this sheet, counted across every plane and stack.</param>
public readonly record struct SheetUsage(int Maps, int Tiles);

/// <summary>What the world is actually painted with, per tile sheet.</summary>
public sealed partial class MapEditorViewModel : ObservableObject
{
    /// <summary>
    /// Maps and tiles per sheet, for every map that can be read.
    ///
    /// <para>Deleting a sheet is the one action here that cannot be taken back by renaming something, and
    /// the editor has never been able to say what a sheet is holding up. Nothing validates a sheet
    /// reference anywhere in the codebase: a map painted with a sheet that is gone draws blank tiles, and
    /// in the game those tiles also start casting full-square shadows rather than their own silhouette.</para>
    ///
    /// <para>One pass over the world for every sheet at once, rather than a pass per sheet. Offline that is
    /// an in-memory walk; online it can only see maps already fetched, which is what
    /// <paramref name="readableMaps"/> reports back.</para>
    /// </summary>
    /// <param name="readableMaps">Set to how many maps were counted, and <paramref name="totalMaps"/> to
    /// how many exist — equal offline, and not equal online until everything has been fetched.</param>
    /// <param name="totalMaps">How many map slots the world has.</param>
    public IReadOnlyDictionary<int, SheetUsage> ScanSheetUsage(out int readableMaps, out int totalMaps)
    {
        var mapCounts = new Dictionary<int, int>();
        var tileCounts = new Dictionary<int, int>();
        readableMaps = 0;

        var all = AllReadableMaps(out totalMaps);
        foreach (var map in all)
        {
            readableMaps++;
            var here = new HashSet<int>();
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    var tile = map.Tile[x, y];
                    CountSheets(tile.Ground, here, tileCounts);
                    CountSheets(tile.Fringe, here, tileCounts);
                    CountSheets(tile.Canopy, here, tileCounts);
                }
            }
            foreach (int sheet in here)
                mapCounts[sheet] = mapCounts.GetValueOrDefault(sheet) + 1;
        }

        var usage = new Dictionary<int, SheetUsage>();
        foreach (int sheet in mapCounts.Keys)
            usage[sheet] = new SheetUsage(mapCounts[sheet], tileCounts.GetValueOrDefault(sheet));
        return usage;
    }

    private static void CountSheets(ReadOnlySpan<int> layers, HashSet<int> onThisMap, Dictionary<int, int> tiles)
    {
        foreach (int cell in layers)
        {
            if (LayerCell.IsEmpty(cell)) continue;
            int sheet = LayerCell.Sheet(cell);
            onThisMap.Add(sheet);
            tiles[sheet] = tiles.GetValueOrDefault(sheet) + 1;
        }
    }

    // Offline every map is resident. Online only the rows already fetched carry real tiles — an unloaded
    // row holds a placeholder whose grid is not the map's, so counting it would invent usage.
    private IEnumerable<MapRecord> AllReadableMaps(out int totalMaps)
    {
        if (!_data.IsOnline)
        {
            var offline = _data.OfflineMaps;
            totalMaps = Math.Max(0, offline.Length - 1);
            return offline.Skip(1).Where(m => m is not null)!;
        }

        totalMaps = Maps.Count;
        return Maps.Where(r => r.IsLoaded).Select(r => r.Record);
    }
}

using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Linq;

namespace Mirage.Editor.Tests.ViewModels;

/// <summary>
/// What a tile sheet is holding up, before somebody deletes it.
///
/// <para>Nothing in the codebase validates a sheet reference. A map painted with a sheet that is gone draws
/// blank tiles, and in the game those tiles also begin casting a full-square shadow instead of their own
/// silhouette — so the damage is quiet and it is not confined to the editor. The count is the only thing
/// that turns "delete this sheet?" into a decision rather than a guess.</para>
/// </summary>
[TestFixture]
public class SheetUsageTests
{
    // The offline store is filled by reflection, the way every other fixture here does it.
    private static MapEditorViewModel EditorWith(params MapRecord[] maps)
    {
        var data = new EditorDataService();
        // Slot 0 is unused, matching the offline store: map numbers are 1-based.
        var offline = new MapRecord[maps.Length + 1];
        offline[0] = new MapRecord { Name = "(none)" };
        for (int i = 0; i < maps.Length; i++) offline[i + 1] = maps[i];
        typeof(EditorDataService).GetProperty(nameof(EditorDataService.OfflineMaps))!
            .SetValue(data, offline);
        return new MapEditorViewModel(data, new EditorConnection());
    }

    private static MapRecord MapPainted(params (int X, int Y, int Sheet, int Tile)[] cells)
    {
        var map = new MapRecord();
        foreach (var (x, y, sheet, tile) in cells)
            map.Tile[x, y] = map.Tile[x, y].WithCell(LayerType.Ground, 0, LayerCell.Pack(tile, sheet, anim: false));
        return map;
    }

    /// <summary>Maps and tiles are counted separately: one map painted forty times with a sheet is one map
    /// at risk, and forty cells that would go blank. Both numbers matter to the decision.</summary>
    [Test]
    public void MapsAndTilesAreCountedSeparately()
    {
        var vm = EditorWith(
            MapPainted((0, 0, 3, 1), (1, 0, 3, 2), (2, 0, 3, 3)),
            MapPainted((0, 0, 3, 1)),
            MapPainted((0, 0, 7, 1)));

        var usage = vm.ScanSheetUsage(out int readable, out int total);

        Assert.That(usage[3], Is.EqualTo(new SheetUsage(Maps: 2, Tiles: 4)));
        Assert.That(usage[7], Is.EqualTo(new SheetUsage(Maps: 1, Tiles: 1)));
        Assert.That((readable, total), Is.EqualTo((3, 3)));
    }

    /// <summary>A sheet nothing uses is absent from the result, which is what lets the manager offer a
    /// clean delete rather than warning about every sheet equally.</summary>
    [Test]
    public void AnUnusedSheetIsNotReported()
    {
        var vm = EditorWith(MapPainted((0, 0, 1, 1)));

        Assert.That(vm.ScanSheetUsage(out _, out _).ContainsKey(2), Is.False);
    }

    /// <summary>Empty cells count for nothing. Every map is mostly empty, so counting them would report
    /// sheet 0 as used by the entire world at all times.</summary>
    [Test]
    public void EmptyCellsAreNotUsage()
    {
        var vm = EditorWith(new MapRecord());

        Assert.That(vm.ScanSheetUsage(out int readable, out _), Is.Empty);
        Assert.That(readable, Is.EqualTo(1), "the map was read, it just had nothing on it");
    }

    /// <summary>All three planes are counted. Reading only the ground would under-report a sheet used
    /// exclusively for roofs or bridge decks, and those are exactly the sheets somebody forgets about.</summary>
    [Test]
    public void EveryPlaneIsCounted()
    {
        var map = new MapRecord();
        map.Tile[0, 0] = map.Tile[0, 0]
            .WithCell(LayerType.Ground, 0, LayerCell.Pack(1, 4, anim: false))
            .WithCell(LayerType.Fringe, 0, LayerCell.Pack(1, 5, anim: false))
            .WithCell(LayerType.Canopy, 0, LayerCell.Pack(1, 6, anim: false));

        var usage = EditorWith(map).ScanSheetUsage(out _, out _);

        Assert.That(usage.Keys.OrderBy(k => k), Is.EqualTo(new[] { 4, 5, 6 }));
    }

    /// <summary>Offline the count covers the whole world, so the two figures agree and the manager can say
    /// so plainly instead of hedging.</summary>
    [Test]
    public void OfflineEveryMapIsReadable()
    {
        var vm = EditorWith(MapPainted((0, 0, 1, 1)), new MapRecord(), new MapRecord());

        vm.ScanSheetUsage(out int readable, out int total);

        Assert.That(readable, Is.EqualTo(total));
    }
}

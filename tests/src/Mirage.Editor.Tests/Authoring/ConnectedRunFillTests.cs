using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Linq;

namespace Mirage.Editor.Tests.Authoring;

/// <summary>
/// Editing one tile of a run edits the run.
///
/// <para>A warp cluster, a wall, a row of plates: authored as a group, and almost always edited as one.
/// The fill grows from the clicked tile across every touching tile carrying the same attribute.</para>
///
/// <para>It is inert while a dialog is laying a NEW attribute, and that is the point of the guard — a run
/// grown from open ground is every open tile on the map, which is never what anyone meant.</para>
/// </summary>
[TestFixture]
public class ConnectedRunFillTests
{
    private static void Set(EditorDataService data, string prop, object value) =>
        typeof(EditorDataService).GetProperty(prop)!.SetValue(data, value);

    private static (MapEditorViewModel vm, MapRecord map) Build()
    {
        var data = new EditorDataService();
        var map = new MapRecord { Name = "Yard" };
        Set(data, nameof(EditorDataService.OfflineMaps), new[] { new MapRecord { Name = "(none)" }, map });
        Set(data, nameof(EditorDataService.OfflineNpcs), new[] { new NpcRecord() });
        var vm = new MapEditorViewModel(data, new EditorConnection());
        vm.LoadOffline();
        vm.SelectedMap = vm.Maps.First(m => m.Index == 1);
        vm.SelectedMode = EditorMode.Attribute;
        return (vm, map);
    }

    // An L of walls, plus one that only touches it at a corner.
    private static void LayWalls(MapRecord map)
    {
        foreach (var (x, y) in new[] { (2, 2), (3, 2), (4, 2), (4, 3), (4, 4) })
            map.EditTile(x, y, t => t with { Type = TileType.Blocked });
        map.EditTile(5, 5, t => t with { Type = TileType.Blocked });      // diagonal from (4,4): a separate wall
    }

    [Test]
    public void EditingOneWall_WithoutTheFill_ChangesOnlyThatWall()
    {
        var (vm, map) = Build();
        LayWalls(map);
        vm.SelectedAttributeTool = AttributeTool.Blocked;
        vm.FillRun = false;

        vm.TileClicked(new TileClick(3, 2, false, false));
        vm.BlockedBlocksLight = false;
        vm.ConfirmBlockedCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(map.Tile[3, 2].BlocksLight, Is.False, "the clicked wall took the change");
            Assert.That(map.Tile[2, 2].BlocksLight, Is.True, "its neighbor did not");
        });
    }

    [Test]
    public void EditingOneWall_WithTheFill_ChangesTheWholeRun()
    {
        var (vm, map) = Build();
        LayWalls(map);
        vm.SelectedAttributeTool = AttributeTool.Blocked;
        vm.FillRun = true;

        vm.TileClicked(new TileClick(3, 2, false, false));
        vm.BlockedBlocksLight = false;
        vm.ConfirmBlockedCommand.Execute(null);

        Assert.Multiple(() =>
        {
            foreach (var (x, y) in new[] { (2, 2), (3, 2), (4, 2), (4, 3), (4, 4) })
                Assert.That(map.Tile[x, y].BlocksLight, Is.False, $"({x},{y}) is part of the run");
            Assert.That(map.Tile[5, 5].BlocksLight, Is.True,
                "a wall touching only at a corner is a separate wall");
        });
    }

    /// <summary>Laying a wall is instant and takes the defaults. Almost every wall stops everything, so a
    /// dialog there would only ever be confirmed.</summary>
    [Test]
    public void LayingAWall_IsInstantAndStopsEverything()
    {
        var (vm, map) = Build();
        vm.SelectedAttributeTool = AttributeTool.Blocked;
        vm.FillRun = true;

        vm.TileClicked(new TileClick(7, 7, false, false));

        int walls = 0;
        for (int x = 0; x <= Constants.MaxMapX; x++)
            for (int y = 0; y <= Constants.MaxMapY; y++)
                if (map.Tile[x, y].Type == TileType.Blocked) walls++;

        Assert.Multiple(() =>
        {
            Assert.That(vm.ShowBlockedDialog, Is.False, "no dialog for a plain wall");
            Assert.That(walls, Is.EqualTo(1), "one wall placed, not a map full of them");
            Assert.That(map.Tile[7, 7].BlocksLight, Is.True);
            Assert.That(map.Tile[7, 7].BlocksSight, Is.True);
        });
    }

    /// <summary>The guard on the fill itself: a warp laid on open ground has no run to follow, so it
    /// cannot flood every walkable tile on the map.</summary>
    [Test]
    public void LayingANewAttribute_HasNoRunToFollow()
    {
        var (vm, map) = Build();
        vm.SelectedAttributeTool = AttributeTool.Warp;
        vm.FillRun = true;

        vm.TileClicked(new TileClick(7, 7, false, false));

        Assert.That(vm.CanFillRun, Is.False, "open ground offers no run");

        vm.WarpMapNum = 5;
        vm.ConfirmWarpCommand.Execute(null);

        int warps = 0;
        for (int x = 0; x <= Constants.MaxMapX; x++)
            for (int y = 0; y <= Constants.MaxMapY; y++)
                if (map.Tile[x, y].Type == TileType.Warp) warps++;

        Assert.That(warps, Is.EqualTo(1), "one warp placed, not a map full of them");
    }

    [Test]
    public void ClickingAnExistingAttribute_OffersTheRun()
    {
        var (vm, map) = Build();
        LayWalls(map);
        vm.SelectedAttributeTool = AttributeTool.Blocked;

        vm.TileClicked(new TileClick(3, 2, false, false));

        Assert.That(vm.CanFillRun, Is.True);
    }

    /// <summary>Every dialog attribute uses it, not just walls: a warp cluster is the case that asked for
    /// it in the first place.</summary>
    [Test]
    public void AWarpCluster_TakesANewDestinationTogether()
    {
        var (vm, map) = Build();
        foreach (var (x, y) in new[] { (1, 1), (2, 1), (1, 2) })
        {
            map.EditTile(x, y, t => t with { Type = TileType.Warp });
            map.EditTile(x, y, t => t with { WarpMap = 4 });
        }
        vm.SelectedAttributeTool = AttributeTool.Warp;
        vm.FillRun = true;

        vm.TileClicked(new TileClick(1, 1, false, false));
        vm.WarpMapNum = 9;
        vm.WarpX = 3;
        vm.WarpY = 8;
        vm.ConfirmWarpCommand.Execute(null);

        Assert.Multiple(() =>
        {
            foreach (var (x, y) in new[] { (1, 1), (2, 1), (1, 2) })
            {
                Assert.That(map.Tile[x, y].WarpMap, Is.EqualTo((short)9), $"({x},{y}) took the new map");
                Assert.That(map.Tile[x, y].WarpX, Is.EqualTo((short)3));
                Assert.That(map.Tile[x, y].WarpY, Is.EqualTo((short)8));
            }
        });
    }

    /// <summary>The run is one attribute's, not every attribute's: a wall beside a warp is not part of the
    /// warp's run.</summary>
    [Test]
    public void TheRun_StopsAtADifferentAttribute()
    {
        var (vm, map) = Build();
        map.EditTile(1, 1, t => t with { Type = TileType.Warp });
        map.EditTile(2, 1, t => t with { Type = TileType.Warp });
        map.EditTile(3, 1, t => t with { Type = TileType.Blocked });
        vm.SelectedAttributeTool = AttributeTool.Warp;
        vm.FillRun = true;

        vm.TileClicked(new TileClick(1, 1, false, false));
        vm.WarpMapNum = 6;
        vm.ConfirmWarpCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(map.Tile[2, 1].WarpMap, Is.EqualTo((short)6), "the warp beside it is in the run");
            Assert.That(map.Tile[3, 1].Type, Is.EqualTo(TileType.Blocked), "the wall is untouched");
        });
    }

    /// <summary>Undo takes the whole run back, not just the tile that was clicked.</summary>
    [Test]
    public void Undo_TakesTheWholeRunBack()
    {
        var (vm, map) = Build();
        LayWalls(map);
        vm.SelectedAttributeTool = AttributeTool.Blocked;
        vm.FillRun = true;

        vm.TileClicked(new TileClick(3, 2, false, false));
        vm.BlockedBlocksLight = false;
        vm.ConfirmBlockedCommand.Execute(null);
        vm.UndoCommand.Execute(null);

        Assert.Multiple(() =>
        {
            foreach (var (x, y) in new[] { (2, 2), (3, 2), (4, 2), (4, 3), (4, 4) })
                Assert.That(map.Tile[x, y].BlocksLight, Is.True, $"({x},{y}) went back");
        });
    }
}

using Mirage.Editor.Localization;
using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Linq;

namespace Mirage.Editor.Tests.Authoring;

/// <summary>
/// Uniform two-plane authoring (post-pivot): attributes are placed on a chosen logical layer
/// (SelectedAttributeLayer) — Ground writes the inline attribute, Fringe writes FringeAttr — and the LayerRamp
/// is the sole connector, occupying BOTH planes (no other attribute may share its tile; a ramp needs a clear tile).
/// </summary>
[TestFixture]
public class FringeAuthoringTests
{
    private static void Set(EditorDataService data, string prop, object value) =>
        typeof(EditorDataService).GetProperty(prop)!.SetValue(data, value);

    // Offline VM in Attribute mode on a blank "Bridge" map (index 1), ready to place.
    private static (MapEditorViewModel vm, MapRecord map) Build()
    {
        var data = new EditorDataService();
        var map = new MapRecord { Name = "Bridge" };
        Set(data, nameof(EditorDataService.OfflineMaps), new[] { new MapRecord { Name = "(none)" }, map });
        Set(data, nameof(EditorDataService.OfflineNpcs), new[] { new NpcRecord() });
        var vm = new MapEditorViewModel(data, new EditorConnection());
        vm.LoadOffline();
        vm.SelectedMap = vm.Maps.First(m => m.Index == 1);
        vm.SelectedMode = EditorMode.Attribute;
        return (vm, map);
    }

    // ── Layer-specific attributes ───────────────────────────────────────────────

    [Test]
    public void BlockedTool_OnFringeLayer_WritesFringeAttr_LeavesGroundUntouched()
    {
        var (vm, map) = Build();
        vm.SelectedAttributeTool = AttributeTool.Blocked;
        vm.SelectedAttributeLayer = WorldLayer.Fringe;

        vm.TileClicked(new TileClick(4, 4, false, false));

        Assert.Multiple(() =>
        {
            Assert.That(map.Tile[4, 4].FringeAttr, Is.Not.Null, "a fringe wall lands on the FringeAttr sub-record");
            Assert.That(map.Tile[4, 4].FringeAttr!.Type, Is.EqualTo(TileType.Blocked));
            Assert.That(map.Tile[4, 4].Type, Is.EqualTo(TileType.Walkable), "the GROUND plane is untouched");
        });
    }

    [Test]
    public void BlockedTool_OnGroundLayer_WritesInlineType_LeavesFringeUntouched()
    {
        var (vm, map) = Build();
        vm.SelectedAttributeTool = AttributeTool.Blocked;
        vm.SelectedAttributeLayer = WorldLayer.Ground;

        vm.TileClicked(new TileClick(4, 4, false, false));

        Assert.Multiple(() =>
        {
            Assert.That(map.Tile[4, 4].Type, Is.EqualTo(TileType.Blocked), "a ground wall is the inline Type");
            Assert.That(map.Tile[4, 4].FringeAttr, Is.Null, "the FRINGE plane is untouched");
        });
    }

    // ── LayerRamp — writes FringeAttr, occupies both planes ──────────────────────

    [Test]
    public void LayerRampTool_WritesRampWithGroundSideDirection()
    {
        var (vm, map) = Build();
        vm.SelectedAttributeTool = AttributeTool.LayerRamp;
        vm.LayerRampDirection = Direction.Left;

        vm.TileClicked(new TileClick(7, 2, false, false));

        Assert.Multiple(() =>
        {
            Assert.That(map.Tile[7, 2].FringeAttr, Is.Not.Null);
            Assert.That(map.Tile[7, 2].FringeAttr!.Type, Is.EqualTo(TileType.LayerRamp));
            Assert.That(map.Tile[7, 2].FringeAttr!.RampGroundSide, Is.EqualTo(Direction.Left), "the ground-side mount direction");
            Assert.That(map.Tile[7, 2].Type, Is.EqualTo(TileType.Walkable));
        });
    }

    [Test]
    public void LayerRamp_RefusesATileThatAlreadyHasAnAttribute()
    {
        var (vm, map) = Build();
        // A ground wall already sits at (3,3).
        vm.SelectedAttributeTool = AttributeTool.Blocked;
        vm.SelectedAttributeLayer = WorldLayer.Ground;
        vm.TileClicked(new TileClick(3, 3, false, false));
        Assume.That(map.Tile[3, 3].Type, Is.EqualTo(TileType.Blocked));

        // A ramp needs a fully-clear tile — it must refuse (3,3).
        vm.SelectedAttributeTool = AttributeTool.LayerRamp;
        vm.TileClicked(new TileClick(3, 3, false, false));

        Assert.Multiple(() =>
        {
            Assert.That(map.Tile[3, 3].FringeAttr, Is.Null, "no ramp lands on an occupied tile");
            Assert.That(map.Tile[3, 3].Type, Is.EqualTo(TileType.Blocked), "the existing attribute is left intact");
        });
    }

    [Test]
    public void RampTile_RefusesOtherAttributes_OnEitherLayer()
    {
        var (vm, map) = Build();
        vm.SelectedAttributeTool = AttributeTool.LayerRamp;
        vm.TileClicked(new TileClick(5, 5, false, false));
        Assume.That(map.Tile[5, 5].FringeAttr!.Type, Is.EqualTo(TileType.LayerRamp));

        // Neither a ground nor a fringe attribute may be written onto the ramp's tile.
        vm.SelectedAttributeTool = AttributeTool.Blocked;
        vm.SelectedAttributeLayer = WorldLayer.Ground;
        vm.TileClicked(new TileClick(5, 5, false, false));
        vm.SelectedAttributeLayer = WorldLayer.Fringe;
        vm.TileClicked(new TileClick(5, 5, false, false));

        Assert.Multiple(() =>
        {
            Assert.That(map.Tile[5, 5].Type, Is.EqualTo(TileType.Walkable), "ground attr refused over a ramp");
            Assert.That(map.Tile[5, 5].FringeAttr!.Type, Is.EqualTo(TileType.LayerRamp), "the ramp still owns the tile on both planes");
        });
    }

    // ── Fringe dialog attributes: Warp + Item author the fringe plane (§1b) ──────

    [Test]
    public void FringeWarp_AuthorsOnTheFringePlane_LeavesGroundUntouched()
    {
        var (vm, map) = Build();
        vm.SelectedAttributeTool = AttributeTool.Warp;
        vm.SelectedAttributeLayer = WorldLayer.Fringe;

        vm.TileClicked(new TileClick(6, 6, false, false));   // opens the warp dialog (no immediate write)
        Assume.That(vm.ShowWarpDialog, Is.True, "a fringe warp click opens the warp dialog");
        vm.WarpMapNum = 1;
        vm.WarpX = 3;
        vm.WarpY = 4;
        vm.ConfirmWarpCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(map.Tile[6, 6].FringeAttr, Is.Not.Null, "the warp lands on the FringeAttr sub-record");
            Assert.That(map.Tile[6, 6].FringeAttr!.Type, Is.EqualTo(TileType.Warp));
            Assert.That(map.Tile[6, 6].FringeAttr!.WarpMap, Is.EqualTo((short)1), "dest map");
            Assert.That(map.Tile[6, 6].FringeAttr!.WarpX, Is.EqualTo((short)3), "dest x");
            Assert.That(map.Tile[6, 6].FringeAttr!.WarpY, Is.EqualTo((short)4), "dest y");
            Assert.That(map.Tile[6, 6].Type, Is.EqualTo(TileType.Walkable), "the GROUND plane is untouched");
        });
    }

    // §1b target-layer: a warp's DEST layer is its own field now, and round-trips through
    // the dialog — so a warp can deliver you onto the fringe deck, independent of the plane it is authored on.
    [Test]
    public void WarpDestLayer_Fringe_RoundTrips()
    {
        var (vm, map) = Build();
        vm.SelectedAttributeTool = AttributeTool.Warp;
        vm.SelectedAttributeLayer = WorldLayer.Ground;   // author the warp on the ground plane

        vm.TileClicked(new TileClick(6, 6, false, false));
        Assume.That(vm.ShowWarpDialog, Is.True);
        vm.WarpMapNum = 1;
        vm.WarpX = 3;
        vm.WarpY = 7;
        vm.WarpDestLayer = WorldLayer.Fringe;  // deliver onto the deck
        vm.ConfirmWarpCommand.Execute(null);

        Assert.That(map.Tile[6, 6].WarpY, Is.EqualTo((short)7), "dest Y");
        Assert.That(map.Tile[6, 6].WarpLayer, Is.EqualTo(WorldLayer.Fringe), "dest layer is its own field");

        // Clobber the fields, then re-open the dialog on the same tile — both re-read from the tile.
        vm.WarpY = 0;
        vm.WarpDestLayer = WorldLayer.Ground;
        vm.TileClicked(new TileClick(6, 6, false, false));
        Assert.Multiple(() =>
        {
            Assert.That(vm.WarpY, Is.EqualTo((short)7), "Y re-reads");
            Assert.That(vm.WarpDestLayer, Is.EqualTo(WorldLayer.Fringe), "dest layer re-reads");
        });
    }

    [Test]
    public void FringeItem_AuthorsOnTheFringePlane()
    {
        var (vm, map) = Build();
        vm.SelectedAttributeTool = AttributeTool.Item;
        vm.SelectedAttributeLayer = WorldLayer.Fringe;

        vm.TileClicked(new TileClick(6, 6, false, false));   // opens the item dialog
        Assume.That(vm.ShowItemDialog, Is.True);
        vm.ItemTileNum = 5;
        vm.ItemTileQuantity = 1;
        vm.ItemTileRespawnSeconds = 30;
        vm.ConfirmItemCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(map.Tile[6, 6].FringeAttr, Is.Not.Null);
            Assert.That(map.Tile[6, 6].FringeAttr!.Type, Is.EqualTo(TileType.Item));
            Assert.That(map.Tile[6, 6].FringeAttr!.ItemNum, Is.EqualTo((short)5), "item number");
            Assert.That(map.Tile[6, 6].FringeAttr!.ItemRespawnSecs, Is.EqualTo((short)30), "respawn seconds");
            Assert.That(map.Tile[6, 6].Type, Is.EqualTo(TileType.Walkable), "the GROUND plane is untouched");
        });
    }

    [Test]
    public void FringeKey_AuthorsADoorOnTheFringePlane()
    {
        var (vm, map) = Build();
        vm.SelectedAttributeTool = AttributeTool.Key;
        vm.SelectedAttributeLayer = WorldLayer.Fringe;

        vm.TileClicked(new TileClick(6, 6, false, false));   // opens the key dialog
        Assume.That(vm.ShowKeyDialog, Is.True);
        vm.KeyItemNum = 3;
        vm.KeyTake = true;
        vm.ConfirmKeyCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(map.Tile[6, 6].FringeAttr, Is.Not.Null, "a fringe door lands on the FringeAttr sub-record");
            Assert.That(map.Tile[6, 6].FringeAttr!.Type, Is.EqualTo(TileType.Key));
            Assert.That(map.Tile[6, 6].FringeAttr!.KeyItemNum, Is.EqualTo((short)3), "required key item");
            Assert.That(map.Tile[6, 6].FringeAttr!.KeyIsConsumed, Is.True, "the take flag");
            Assert.That(map.Tile[6, 6].Type, Is.EqualTo(TileType.Walkable), "the GROUND plane is untouched");
        });
    }

    [Test]
    public void FringeKeyOpen_AuthorsOnTheFringePlane()
    {
        var (vm, map) = Build();
        vm.SelectedAttributeTool = AttributeTool.KeyOpen;
        vm.SelectedAttributeLayer = WorldLayer.Fringe;

        vm.TileClicked(new TileClick(6, 6, false, false));   // opens the key-open dialog
        Assume.That(vm.ShowKeyOpenDialog, Is.True);
        vm.KeyOpenDoorX = 2;
        vm.KeyOpenDoorY = 4;
        vm.ConfirmKeyOpenCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(map.Tile[6, 6].FringeAttr, Is.Not.Null);
            Assert.That(map.Tile[6, 6].FringeAttr!.Type, Is.EqualTo(TileType.KeyOpen));
            Assert.That(map.Tile[6, 6].FringeAttr!.DoorX, Is.EqualTo((short)2), "door x");
            Assert.That(map.Tile[6, 6].FringeAttr!.DoorY, Is.EqualTo((short)4), "door y");
            Assert.That(map.Tile[6, 6].Type, Is.EqualTo(TileType.Walkable), "the GROUND plane is untouched");
        });
    }

    // A KeyOpen's target-door layer is its own field and round-trips through the dialog, so a plate can open a
    // Key door on a DIFFERENT plane than the plate itself.
    [Test]
    public void KeyOpenDoorLayer_Fringe_RoundTrips()
    {
        var (vm, map) = Build();
        vm.SelectedAttributeTool = AttributeTool.KeyOpen;
        vm.SelectedAttributeLayer = WorldLayer.Ground;   // the plate itself is on the ground

        vm.TileClicked(new TileClick(6, 6, false, false));
        Assume.That(vm.ShowKeyOpenDialog, Is.True);
        vm.KeyOpenDoorX = 2;
        vm.KeyOpenDoorY = 4;
        vm.KeyOpenDoorLayer = WorldLayer.Fringe;  // opens a FRINGE door
        vm.ConfirmKeyOpenCommand.Execute(null);

        Assert.That(map.Tile[6, 6].DoorLayer, Is.EqualTo(WorldLayer.Fringe), "DoorLayer carries the target-door layer");

        vm.KeyOpenDoorLayer = WorldLayer.Ground;   // clobber, then re-open on the same tile
        vm.TileClicked(new TileClick(6, 6, false, false));
        Assert.That(vm.KeyOpenDoorLayer, Is.EqualTo(WorldLayer.Fringe), "door layer re-reads");
    }

    // ── Delete action (brush erase) ──────────────────────────────────────────────

    [Test]
    public void DeleteAction_BrushErasesEveryAttributeUnderTheBrush()
    {
        var (vm, map) = Build();
        // Paint a 2×2 block of ground Blocked attributes with a 2×2 brush.
        vm.SelectedAttributeTool = AttributeTool.Blocked;
        vm.SelectedAttributeLayer = WorldLayer.Ground;
        vm.AttributeBrushSizeX = 2;
        vm.AttributeBrushSizeY = 2;
        vm.TileClicked(new TileClick(4, 4, false, false));
        Assume.That(map.Tile[4, 4].Type, Is.EqualTo(TileType.Blocked));
        Assume.That(map.Tile[5, 5].Type, Is.EqualTo(TileType.Blocked));

        // The Delete action, same 2×2 brush, clears them all at once.
        vm.SelectedAction = EditorAction.Delete;
        vm.DeleteAtCommand.Execute((4, 4));

        Assert.Multiple(() =>
        {
            Assert.That(map.Tile[4, 4].Type, Is.EqualTo(TileType.Walkable), "erased (4,4)");
            Assert.That(map.Tile[5, 4].Type, Is.EqualTo(TileType.Walkable), "erased (5,4)");
            Assert.That(map.Tile[4, 5].Type, Is.EqualTo(TileType.Walkable), "erased (4,5)");
            Assert.That(map.Tile[5, 5].Type, Is.EqualTo(TileType.Walkable), "erased (5,5)");
        });
    }

    // ── Erase + undo ─────────────────────────────────────────────────────────────

    [Test]
    public void RightClick_RemovesARamp_FromEitherLayer()
    {
        var (vm, map) = Build();
        vm.SelectedAttributeTool = AttributeTool.LayerRamp;
        vm.TileClicked(new TileClick(3, 3, false, false));
        Assume.That(map.Tile[3, 3].FringeAttr, Is.Not.Null);

        // Right-click while the GROUND layer is active still removes the ramp (it occupies both planes).
        vm.SelectedAttributeTool = AttributeTool.Blocked;
        vm.SelectedAttributeLayer = WorldLayer.Ground;
        vm.TileRightClicked((3, 3));

        Assert.That(map.Tile[3, 3].FringeAttr, Is.Null, "right-click removes the ramp from either layer");
    }

    [Test]
    public void Undo_RestoresAClearedFringeWall()
    {
        var (vm, map) = Build();
        vm.SelectedAttributeTool = AttributeTool.Blocked;
        vm.SelectedAttributeLayer = WorldLayer.Fringe;
        vm.TileClicked(new TileClick(3, 3, false, false));   // place a fringe wall
        vm.TileRightClicked((3, 3));            // clear it
        Assume.That(map.Tile[3, 3].FringeAttr, Is.Null, "precondition: cleared");

        vm.UndoCommand.Execute(null);           // undo the clear

        Assert.Multiple(() =>
        {
            Assert.That(map.Tile[3, 3].FringeAttr, Is.Not.Null, "undo restores the cleared fringe wall");
            Assert.That(map.Tile[3, 3].FringeAttr!.Type, Is.EqualTo(TileType.Blocked));
        });
    }
}

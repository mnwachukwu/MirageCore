using Mirage.Editor.Models;
using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Linq;

namespace Mirage.Editor.Tests.Authoring;

/// <summary>
/// Painting a tile onto a layer that is put away.
///
/// <para>The tile would land correctly and be invisible, which reads as the editor ignoring the click. So
/// nothing is painted until the author says what they meant, and the only two answers are "show the layer
/// and place it" and "cancel" — there is no third that places a tile you cannot see.</para>
///
/// <para>The prompt is per STROKE. Painting is a drag, and a question per cell would be unusable.</para>
/// </summary>
[TestFixture]
public class HiddenLayerPaintTests
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
        vm.SelectedMode = EditorMode.Tile;
        vm.SelectedLayerType = LayerType.Ground;
        vm.SelectedLayerIndex = 1;
        vm.SelectedStamp = new TileStamp(1, 1, new[,] { { 5 } });
        return (vm, map);
    }

    private static int GroundCell(MapRecord map, int x, int y, int layer) => map.Tile[x, y].Ground[layer];

    /// <summary>The control half: with the layer showing, the click paints and nothing is asked. Without
    /// it a gate that blocked every paint would pass every test below.</summary>
    [Test]
    public void AVisibleLayerPaintsWithoutAsking()
    {
        var (vm, map) = Build();

        vm.TileClicked(new TileClick(3, 3, false, false));

        Assert.That(vm.ShowHiddenLayerDialog, Is.False);
        Assert.That(LayerCell.IsEmpty(GroundCell(map, 3, 3, 0)), Is.False);
    }

    /// <summary>The reported shape: the click is refused and the question is asked instead.</summary>
    [Test]
    public void PaintingOntoAHiddenLayerPlacesNothingAndAsks()
    {
        var (vm, map) = Build();
        vm.SetLayerVisible(LayerType.Ground, 0, visible: false);

        vm.TileClicked(new TileClick(3, 3, false, false));

        Assert.That(vm.ShowHiddenLayerDialog, Is.True);
        Assert.That(LayerCell.IsEmpty(GroundCell(map, 3, 3, 0)), Is.True,
            "the tile was placed on a layer nothing on screen would have shown it on");
    }

    /// <summary>Confirming does both halves — the layer comes back AND the refused tile is laid. Turning
    /// the layer on without placing it would make the author paint the same cell twice.</summary>
    [Test]
    public void ConfirmingShowsTheLayerAndLaysTheRefusedTile()
    {
        var (vm, map) = Build();
        vm.SetLayerVisible(LayerType.Ground, 0, visible: false);
        vm.TileClicked(new TileClick(3, 3, false, false));

        vm.ConfirmHiddenLayerCommand.Execute(null);

        Assert.That(vm.ShowHiddenLayerDialog, Is.False);
        Assert.That(vm.LayerVisibility.IsVisible(LayerType.Ground, 0), Is.True);
        Assert.That(LayerCell.IsEmpty(GroundCell(map, 3, 3, 0)), Is.False);
    }

    /// <summary>Cancelling leaves both the map and the layer exactly as they were.</summary>
    [Test]
    public void CancellingChangesNothing()
    {
        var (vm, map) = Build();
        vm.SetLayerVisible(LayerType.Ground, 0, visible: false);
        vm.TileClicked(new TileClick(3, 3, false, false));

        vm.CancelHiddenLayerCommand.Execute(null);

        Assert.That(vm.ShowHiddenLayerDialog, Is.False);
        Assert.That(vm.LayerVisibility.IsVisible(LayerType.Ground, 0), Is.False);
        Assert.That(LayerCell.IsEmpty(GroundCell(map, 3, 3, 0)), Is.True);
    }

    /// <summary>🔴 A stroke asks ONCE. The pointer is still down while the prompt is up, so every cell the
    /// drag crosses would otherwise raise it again — and a confirmation per cell is unusable.</summary>
    [Test]
    public void AStrokeAcrossManyCellsAsksOnlyOnce()
    {
        var (vm, _) = Build();
        vm.SetLayerVisible(LayerType.Ground, 0, visible: false);

        vm.BeginBatch();
        int asked = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.ShowHiddenLayerDialog) && vm.ShowHiddenLayerDialog) asked++;
        };
        for (int x = 0; x < 6; x++)
            vm.TileClicked(new TileClick(x, 2, false, false, Dragging: true));
        vm.CommitBatch();

        Assert.That(asked, Is.EqualTo(1));
    }

    /// <summary>The next gesture is a new question. The flag is per batch, not once per session.</summary>
    [Test]
    public void TheNextStrokeAsksAgain()
    {
        var (vm, _) = Build();
        vm.SetLayerVisible(LayerType.Ground, 0, visible: false);

        vm.BeginBatch();
        vm.TileClicked(new TileClick(1, 1, false, false, Dragging: true));
        vm.CommitBatch();
        vm.CancelHiddenLayerCommand.Execute(null);

        vm.BeginBatch();
        vm.TileClicked(new TileClick(2, 1, false, false, Dragging: true));
        vm.CommitBatch();

        Assert.That(vm.ShowHiddenLayerDialog, Is.True);
    }

    /// <summary>Hiding a layer is a way of looking at a map, not a fact about one: a hidden layer's tiles
    /// stay exactly where they were and stay reachable by everything that reads the record.</summary>
    [Test]
    public void HidingALayerDoesNotTouchTheMap()
    {
        var (vm, map) = Build();
        vm.TileClicked(new TileClick(4, 4, false, false));
        int painted = GroundCell(map, 4, 4, 0);

        vm.SetLayerVisible(LayerType.Ground, 0, visible: false);

        Assert.That(GroundCell(map, 4, 4, 0), Is.EqualTo(painted));
    }

    /// <summary>A hidden layer is still ON the tile, so the hover read-out still reports it — dimmed, not
    /// dropped. Omitting it is how somebody concludes a layer is empty and paints over what is there.</summary>
    [Test]
    public void TheHoverReadoutStillListsAHiddenLayer()
    {
        var (vm, _) = Build();
        vm.TileClicked(new TileClick(4, 4, false, false));
        vm.HoveredX = 4;
        vm.HoveredY = 4;

        vm.SetLayerVisible(LayerType.Ground, 0, visible: false);
        var row = vm.HoveredGroundLayers[0];

        Assert.That(row.TileIndex, Is.EqualTo(5), "the tile is still reported");
        Assert.That(row.IsHidden, Is.True, "and reported as put away");
        Assert.That(row.CellOpacity, Is.LessThan(1.0));
    }
}

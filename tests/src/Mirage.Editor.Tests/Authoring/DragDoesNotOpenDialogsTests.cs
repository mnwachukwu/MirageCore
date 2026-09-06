using Mirage.Editor.Models;
using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Reflection;

namespace Mirage.Editor.Tests.Authoring;

/// <summary>
/// An authoring dialog opens on the PRESS and never on a cell the pointer is dragged across.
///
/// <para>🔴 The two are the same event otherwise, which is the bug: laying a run of walls by dragging
/// crosses tiles that already hold one, and on an existing attribute a click MEANS "edit this" — so the
/// properties dialog fired mid-stroke, once per wall crossed. Matt hit it dragging blocks.</para>
///
/// <para>Every dialog attribute has the same shape, so they are all covered here rather than only the one
/// that was reported.</para>
/// </summary>
[TestFixture]
public class DragDoesNotOpenDialogsTests
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

    /// <summary>🔴 The reported case: a stroke laying walls runs over one already there.</summary>
    [Test]
    public void DraggingOverAnExistingWall_DoesNotOpenItsProperties()
    {
        var (vm, map) = Build();
        map.EditTile(4, 4, t => t with { Type = TileType.Blocked });
        vm.SelectedAttributeTool = AttributeTool.Blocked;

        vm.TileClicked(new TileClick(4, 4, false, false, Dragging: true));

        Assert.That(vm.ShowBlockedDialog, Is.False);
    }

    /// <summary>Pressing on one still does — that is how a wall's exceptions are authored at all.</summary>
    [Test]
    public void PressingOnAnExistingWall_StillOpensItsProperties()
    {
        var (vm, map) = Build();
        map.EditTile(4, 4, t => t with { Type = TileType.Blocked });
        vm.SelectedAttributeTool = AttributeTool.Blocked;

        vm.TileClicked(new TileClick(4, 4, false, false));

        Assert.That(vm.ShowBlockedDialog, Is.True);
    }

    /// <summary>The stroke still has to DO its job — a dragged cell of open ground becomes a wall, because
    /// laying a wall was never a dialog in the first place.</summary>
    [Test]
    public void DraggingOverOpenGround_StillLaysTheWall()
    {
        var (vm, map) = Build();
        vm.SelectedAttributeTool = AttributeTool.Blocked;

        vm.TileClicked(new TileClick(6, 6, false, false, Dragging: true));

        Assert.Multiple(() =>
        {
            Assert.That(map.Tile[6, 6].Type, Is.EqualTo(TileType.Blocked), "the dragged cell is still painted");
            Assert.That(vm.ShowBlockedDialog, Is.False, "and nothing was asked about it");
        });
    }

    /// <summary>Every dialog attribute has the same shape, so none of them may open on a drag — a warp is
    /// the worst of them, since the dialog it opens is asking where the tile leads.</summary>
    [TestCase(AttributeTool.Warp)]
    [TestCase(AttributeTool.Item)]
    [TestCase(AttributeTool.Key)]
    [TestCase(AttributeTool.KeyOpen)]
    public void DraggingWithADialogAttribute_OpensNothing(AttributeTool tool)
    {
        var (vm, _) = Build();
        vm.SelectedAttributeTool = tool;

        vm.TileClicked(new TileClick(5, 5, false, false, Dragging: true));

        Assert.Multiple(() =>
        {
            Assert.That(vm.ShowWarpDialog, Is.False);
            Assert.That(vm.ShowItemDialog, Is.False);
            Assert.That(vm.ShowKeyDialog, Is.False);
            Assert.That(vm.ShowKeyOpenDialog, Is.False);
        });
    }

    /// <summary>A press with one of those still opens its dialog — the drag guard must not disarm authoring.</summary>
    [Test]
    public void PressingWithAWarp_StillOpensTheWarpDialog()
    {
        var (vm, _) = Build();
        vm.SelectedAttributeTool = AttributeTool.Warp;

        vm.TileClicked(new TileClick(5, 5, false, false));

        Assert.That(vm.ShowWarpDialog, Is.True);
    }

    /// <summary>
    /// 🔴 The half the tests above cannot reach: the control has to SAY the cell was dragged.
    ///
    /// <para>Everything above calls the view-model directly, so all of it passes with the flag missing at the
    /// one place that sets it — the interpolated cells the pointer is dragged through. That is the whole bug
    /// returning with a green suite, so the wiring is checked at the source.</para>
    /// </summary>
    [Test]
    public void TheControl_MarksEveryDraggedCellAsDragged()
    {
        string root = typeof(DragDoesNotOpenDialogsTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!;
        string path = Path.Combine(root, "editor", "src", "Mirage.Editor", "Controls", "TileGridControl.Input.cs");
        Assert.That(File.Exists(path), Is.True, $"Input handler not found: {path}");

        var lines = File.ReadAllLines(path);
        var dragged = new List<string>();
        // A bounded window, not a sticky flag: the RIGHT-button stroke walks a Bresenham line too and emits
        // TileRightClicked, so a flag left standing would run on and match the left-button PRESS further down
        // the file — which legitimately carries no Dragging, and the check would fail on correct code.
        int window = 0;
        foreach (string line in lines)
        {
            if (line.Contains("BresenhamLine(")) { window = 12; continue; }
            if (window == 0) continue;
            window--;
            if (line.Contains("TileClicked?.Invoke("))
            {
                dragged.Add(line.Trim());
                window = 0;
            }
        }

        Assert.That(dragged, Is.Not.Empty,
            "No TileClicked emitted from the drag walk — if that moved, teach this test where it went; "
            + "do not delete it.");
        foreach (string line in dragged)
        {
            Assert.That(line, Does.Contain("Dragging: true"),
                "A cell emitted from the drag walk must be marked as dragged, or every authoring dialog "
                + "opens again mid-stroke: " + line);
        }
    }
}

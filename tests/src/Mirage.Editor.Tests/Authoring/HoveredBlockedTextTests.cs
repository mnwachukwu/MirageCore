using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Editor.Tests.Authoring;

/// <summary>
/// What the hover read-out says about a wall.
///
/// <para>🔴 Neither flag is visible on the map. The fringe frame that bounds the upper plane is Blocked with
/// BOTH off — a railing, not rock — and reads exactly like solid rock in the one place you would go to
/// check.</para>
///
/// <para>So it is stated ALWAYS, including when a wall stops nothing. This is the exploded view; saying it
/// only when it deviates from the default would leave the reader inferring from silence.</para>
/// </summary>
[TestFixture]
public class HoveredBlockedTextTests
{
    private static void Set(EditorDataService data, string prop, object value) =>
        typeof(EditorDataService).GetProperty(prop)!.SetValue(data, value);

    private static (MapEditorViewModel vm, MapRecord map) Build()
    {
        var data = new EditorDataService();
        var map = new MapRecord { Name = "Scarp" };
        Set(data, nameof(EditorDataService.OfflineMaps), new[] { new MapRecord { Name = "(none)" }, map });
        Set(data, nameof(EditorDataService.OfflineNpcs), new[] { new NpcRecord() });
        var vm = new MapEditorViewModel(data, new EditorConnection());
        vm.LoadOffline();
        vm.SelectedMap = vm.Maps.First(m => m.Index == 1);
        return (vm, map);
    }

    private static string GroundTextAt(MapEditorViewModel vm, int x, int y)
    {
        vm.HoveredX = x;
        vm.HoveredY = y;
        return vm.HoveredGroundAttributeText;
    }

    /// <summary>🔴 The default is stated too — the point is that a reader never has to infer.</summary>
    [Test]
    public void AWallThatStopsEverything_SaysSo()
    {
        var (vm, map) = Build();
        map.EditTile(3, 3, t => t with { Type = TileType.Blocked, BlocksLight = true, BlocksSight = true });

        string text = GroundTextAt(vm, 3, 3);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("Blocked"));
            Assert.That(text, Does.Contain("stops light and sight"));
        });
    }

    /// <summary>🔴 The case that matters: the fringe frame bounding the upper plane.</summary>
    [Test]
    public void AWallThatStopsNeither_SaysSo()
    {
        var (vm, map) = Build();
        map.EditTile(3, 3, t => t with { Type = TileType.Blocked, BlocksLight = false, BlocksSight = false });

        string text = GroundTextAt(vm, 3, 3);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("Blocked"));
            Assert.That(text, Does.Contain("stops nothing"));
        });
    }

    [Test]
    public void AWallThatStopsOnlyLight_NamesLight()
    {
        var (vm, map) = Build();
        map.EditTile(3, 3, t => t with { Type = TileType.Blocked, BlocksLight = true, BlocksSight = false });

        string text = GroundTextAt(vm, 3, 3);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("stops light"));
            Assert.That(text, Does.Not.Contain("sight"), "sight is not stopped, so it is not listed");
        });
    }

    [Test]
    public void AWallThatStopsOnlySight_NamesSight()
    {
        var (vm, map) = Build();
        map.EditTile(3, 3, t => t with { Type = TileType.Blocked, BlocksLight = false, BlocksSight = true });

        Assert.That(GroundTextAt(vm, 3, 3), Does.Contain("stops sight"));
    }

    /// <summary>Every combination says something — no state is left silent.</summary>
    [TestCase(true, true)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(false, false)]
    public void EveryCombination_IsStated(bool light, bool sight)
    {
        var (vm, map) = Build();
        map.EditTile(3, 3, t => t with { Type = TileType.Blocked, BlocksLight = light, BlocksSight = sight });

        Assert.That(GroundTextAt(vm, 3, 3), Does.Contain("stops"));
    }

    /// <summary>The fringe plane carries its own attribute, and the frame lives there — so the fringe line
    /// has to report the exceptions too, not just the ground one.</summary>
    [Test]
    public void TheFringeLine_ReportsItsOwnExceptions()
    {
        var (vm, map) = Build();
        map.EditTile(4, 4, t => t with
        {
            FringeAttr = new FringeAttr { Type = TileType.Blocked, BlocksLight = false, BlocksSight = false },
        });

        vm.HoveredX = 4;
        vm.HoveredY = 4;

        Assert.That(vm.HoveredFringeAttributeText, Does.Contain("stops nothing"));
    }

    [Test]
    public void AnOpenTile_StillReadsAsCarryingNothing()
    {
        var (vm, _) = Build();

        Assert.That(GroundTextAt(vm, 5, 5), Does.Not.Contain("Blocked"));
    }
}

using Mirage.Editor.Models;
using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Linq;

namespace Mirage.Editor.Tests.Authoring;

/// <summary>
/// Auto-linking, and the asymmetry that made unlinking destructive.
///
/// Linking ASSERTS facts that stay true afterwards, so re-asserting one costs nothing. Unlinking was
/// written as its mirror, and deleted facts the removed edge never implied: clearing a horizontal
/// link walked the grid and wiped the VERTICAL links around it, then recursed from there.
/// </summary>
[TestFixture]
public class MapLinkUnlinkTests
{
    private static MapEditorViewModel BuildOffline(MapRecord[] offlineMaps)
    {
        var data = new EditorDataService();
        typeof(EditorDataService).GetProperty(nameof(EditorDataService.OfflineMaps))!
            .SetValue(data, offlineMaps);
        var vm = new MapEditorViewModel(data, new EditorConnection());
        vm.LoadOffline();
        return vm;
    }

    private static MapRecord Map(string name, int up = 0, int down = 0, int left = 0, int right = 0) =>
        new() { Name = name, Up = up, Down = down, Left = left, Right = right };

    /// <summary>A 2x2 block, fully wired:
    /// <code>
    ///   C(3) D(4)
    ///   A(1) B(2)
    /// </code></summary>
    private static MapRecord[] Block() =>
    [
        Map("(none)"),
        Map("A", up: 3, right: 2),
        Map("B", up: 4, left: 1),
        Map("C", down: 1, right: 4),
        Map("D", down: 2, left: 3),
    ];

    private static MapRowViewModel Row(MapEditorViewModel vm, int index) =>
        vm.Maps.First(m => m.Index == index);

    private static MapRecord Rec(MapEditorViewModel vm, int index) => Row(vm, index).Record;

    [Test]
    public void ClearingALinkRemovesTheBackLink()
    {
        var vm = BuildOffline(Block());
        vm.SelectedMap = Row(vm, 1);

        vm.SelectedMapRight = null;

        Assert.That(Rec(vm, 1).Right, Is.Zero, "the edge the user cleared");
        Assert.That(Rec(vm, 2).Left, Is.Zero, "and B's way home");
    }

    /// <summary>The reported bug. Clearing A.Right walked B.Up to D, D.Left to C, and wiped C.Down —
    /// the vertical adjacency between C and A, which the horizontal edge never implied.</summary>
    [Test]
    public void ClearingAHorizontalLinkLeavesTheVerticalOnesAlone()
    {
        var vm = BuildOffline(Block());
        vm.SelectedMap = Row(vm, 1);

        vm.SelectedMapRight = null;

        Assert.Multiple(() =>
        {
            Assert.That(Rec(vm, 1).Up, Is.EqualTo(3), "A still sits below C");
            Assert.That(Rec(vm, 3).Down, Is.EqualTo(1), "and C still sits above A");
            Assert.That(Rec(vm, 2).Up, Is.EqualTo(4), "B still sits below D");
            Assert.That(Rec(vm, 4).Down, Is.EqualTo(2), "and D still sits above B");
        });
    }

    [Test]
    public void ClearingAVerticalLinkLeavesTheHorizontalOnesAlone()
    {
        var vm = BuildOffline(Block());
        vm.SelectedMap = Row(vm, 1);

        vm.SelectedMapUp = null;

        Assert.Multiple(() =>
        {
            Assert.That(Rec(vm, 1).Up, Is.Zero);
            Assert.That(Rec(vm, 3).Down, Is.Zero);
            Assert.That(Rec(vm, 1).Right, Is.EqualTo(2), "A still sits beside B");
            Assert.That(Rec(vm, 2).Left, Is.EqualTo(1));
            Assert.That(Rec(vm, 3).Right, Is.EqualTo(4), "C still sits beside D");
            Assert.That(Rec(vm, 4).Left, Is.EqualTo(3));
        });
    }

    /// <summary>Only the two maps on the cleared edge are touched, so nothing else is marked dirty
    /// and offered for save.</summary>
    [Test]
    public void ClearingALinkDirtiesOnlyTheTwoMapsOnThatEdge()
    {
        var vm = BuildOffline(Block());
        vm.SelectedMap = Row(vm, 1);

        vm.SelectedMapRight = null;

        Assert.That(vm.Maps.Where(m => m.IsDirty).Select(m => m.Index),
                    Is.EquivalentTo(new[] { 1, 2 }));
    }

    [Test]
    public void LinkingStillFillsInTheBackLink()
    {
        var vm = BuildOffline([Map("(none)"), Map("A"), Map("B")]);
        vm.SelectedMap = Row(vm, 1);

        vm.SelectedMapRight = new NamedEntry(2, "B");

        Assert.That(Rec(vm, 1).Right, Is.EqualTo(2));
        Assert.That(Rec(vm, 2).Left, Is.EqualTo(1), "auto-linking still wires the way home");
    }

    /// <summary>Relink after unlink returns the block to exactly what it was — the pair of operations
    /// is a round trip, which the destructive cascade made impossible.</summary>
    [Test]
    public void UnlinkThenRelinkRestoresTheBlock()
    {
        var vm = BuildOffline(Block());
        vm.SelectedMap = Row(vm, 1);

        vm.SelectedMapRight = null;
        vm.SelectedMapRight = new NamedEntry(2, "B");

        Assert.Multiple(() =>
        {
            Assert.That(Rec(vm, 1).Right, Is.EqualTo(2));
            Assert.That(Rec(vm, 2).Left, Is.EqualTo(1));
            Assert.That(Rec(vm, 1).Up, Is.EqualTo(3));
            Assert.That(Rec(vm, 3).Down, Is.EqualTo(1));
            Assert.That(Rec(vm, 2).Up, Is.EqualTo(4));
            Assert.That(Rec(vm, 4).Down, Is.EqualTo(2));
            Assert.That(Rec(vm, 3).Right, Is.EqualTo(4));
            Assert.That(Rec(vm, 4).Left, Is.EqualTo(3));
        });
    }
}

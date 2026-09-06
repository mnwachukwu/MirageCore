using Mirage.Editor.Models;
using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Editor.Tests.Authoring;

/// <summary>
/// Maps joined by an edge are all one size, and the editor is what holds that true.
///
/// <para>World coordinates run straight across a seam, so a step from a 16x12 map onto a 24x20 one lands
/// somewhere other than where it looks. The rule is enforced from both sides: the resize dialog refuses to
/// resize a map that is linked, and the link pickers refuse a target of a different size. Between them a
/// mismatch has no way in.</para>
/// </summary>
[TestFixture]
public class LinkSizeTests
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

    // Slot 1 is the map being edited; slots 2 and 3 are candidate neighbors.
    private static MapRecord[] World(MapSize center, MapSize match, MapSize other)
    {
        var all = new MapRecord[4];
        for (int i = 0; i < all.Length; i++) all[i] = new MapRecord();
        all[1] = new MapRecord(center.Width, center.Height) { Name = "Center" };
        all[2] = new MapRecord(match.Width, match.Height) { Name = "Same size" };
        all[3] = new MapRecord(other.Width, other.Height) { Name = "Different size" };
        return all;
    }

    private static (MapEditorViewModel Vm, Func<string?> Alert) SceneWithAlert()
    {
        var vm = BuildOffline(World(new MapSize(24, 20), new MapSize(24, 20), new MapSize(16, 12)));
        vm.SelectedMap = vm.Maps.First(m => m.Index == 1);
        string? captured = null;
        vm.ShowAlertAsync = msg => { captured = msg; return Task.CompletedTask; };
        return (vm, () => captured);
    }

    private static NamedEntry Entry(MapEditorViewModel vm, int id) =>
        vm.MapEntries.First(e => e.Id == id);

    [Test]
    public void AMatchingNeighbor_Links()
    {
        var (vm, _) = SceneWithAlert();

        vm.SelectedMapRight = Entry(vm, 2);

        Assert.That(vm.SelectedMap!.Record.Right, Is.EqualTo(2));
    }

    /// <summary>The case that matters: a differently-sized target is refused and the map keeps the link
    /// it already had, rather than being left pointing at something it cannot join.</summary>
    [Test]
    public void ADifferentlySizedNeighbor_IsRefused()
    {
        var (vm, alert) = SceneWithAlert();
        vm.SelectedMapRight = Entry(vm, 2);

        vm.SelectedMapRight = Entry(vm, 3);

        Assert.Multiple(() =>
        {
            Assert.That(vm.SelectedMap!.Record.Right, Is.EqualTo(2), "the refused link must not stick");
            Assert.That(alert(), Is.Not.Null, "the author has to be told why");
            Assert.That(alert(), Does.Contain("24x20").And.Contain("16x12"), "both sizes are named");
        });
    }

    [Test]
    public void ADifferentlySizedNeighbor_IsRefusedOnEveryEdge()
    {
        foreach (var set in new Action<MapEditorViewModel, NamedEntry>[]
                 {
                     (v, e) => v.SelectedMapUp = e,
                     (v, e) => v.SelectedMapDown = e,
                     (v, e) => v.SelectedMapLeft = e,
                     (v, e) => v.SelectedMapRight = e,
                 })
        {
            var (vm, _) = SceneWithAlert();

            set(vm, Entry(vm, 3));

            var r = vm.SelectedMap!.Record;
            Assert.That(r.Up + r.Down + r.Left + r.Right, Is.Zero, "no edge accepts a mismatched target");
        }
    }

    /// <summary>Clearing a link is never refused: an unlinked edge joins nothing, so no size has to agree.</summary>
    [Test]
    public void ClearingALink_IsAlwaysAllowed()
    {
        var (vm, _) = SceneWithAlert();
        vm.SelectedMapRight = Entry(vm, 2);

        vm.SelectedMapRight = null;

        Assert.That(vm.SelectedMap!.Record.Right, Is.Zero);
    }
}

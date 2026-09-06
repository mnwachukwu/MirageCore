using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;
using Mirage.Shared.Records;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Mirage.Editor.Tests.Authoring;

/// <summary>
/// Reproduces the reported bug: Ctrl+Alt+Shift+Click on a neighbor map (NeighborMapClicked) should
/// switch the selected map and load ITS connected properties (Up/Down/Left/Right) correctly.
/// </summary>
[TestFixture]
public class NeighborNavigationTests
{
    // Builds a MapEditorViewModel in offline mode with the given maps installed as OfflineMaps.
    private static MapEditorViewModel BuildOffline(MapRecord[] offlineMaps)
    {
        var data = new EditorDataService();
        // OfflineMaps has a private setter — install the test fixture via reflection.
        typeof(EditorDataService).GetProperty(nameof(EditorDataService.OfflineMaps))!
            .SetValue(data, offlineMaps);
        var vm = new MapEditorViewModel(data, new EditorConnection());
        vm.LoadOffline();
        return vm;
    }

    private static MapRecord Map(string name, int up = 0, int down = 0, int left = 0, int right = 0) =>
        new() { Name = name, Up = up, Down = down, Left = left, Right = right };

    // index 0 is the unused sentinel slot; 1..7 are real maps arranged around center=1.
    private static MapRecord[] SampleWorld() =>
    [
        Map("(none)"),                                  // 0 - unused
        Map("Center", up: 2, down: 3, left: 4, right: 5), // 1
        Map("North", down: 1, left: 6, right: 7),        // 2
        Map("South", up: 1),                             // 3
        Map("West", right: 1),                           // 4
        Map("East", left: 1),                            // 5
        Map("NorthWest", down: 4, right: 2),             // 6
        Map("NorthEast", down: 5, left: 2),              // 7
    ];

    private MapRowViewModel Row(MapEditorViewModel vm, int index) =>
        vm.Maps.First(m => m.Index == index);

    [Test]
    public void Setup_SelectsCenter_ConnectedPropertiesMatch()
    {
        var vm = BuildOffline(SampleWorld());
        vm.SelectedMap = Row(vm, 1);

        Assert.Multiple(() =>
        {
            Assert.That(vm.MapUp, Is.EqualTo(2));
            Assert.That(vm.MapDown, Is.EqualTo(3));
            Assert.That(vm.MapLeft, Is.EqualTo(4));
            Assert.That(vm.MapRight, Is.EqualTo(5));
            Assert.That(vm.NeighborMapUp?.Name, Is.EqualTo("North"));
            Assert.That(vm.NeighborMapDown?.Name, Is.EqualTo("South"));
            Assert.That(vm.NeighborMapLeft?.Name, Is.EqualTo("West"));
            Assert.That(vm.NeighborMapRight?.Name, Is.EqualTo("East"));
        });
    }

    [Test]
    public void NeighborClick_Up_SwitchesMapAndLoadsConnectedProperties()
    {
        var vm = BuildOffline(SampleWorld());
        vm.SelectedMap = Row(vm, 1);

        vm.NeighborMapClickedCommand.Execute(NeighborCell.Up);

        Assert.Multiple(() =>
        {
            Assert.That(vm.SelectedMap!.Index, Is.EqualTo(2), "should navigate to the North map");
            // North (map 2): Up=0, Down=1, Left=6, Right=7
            Assert.That(vm.MapUp, Is.EqualTo(0));
            Assert.That(vm.MapDown, Is.EqualTo(1));
            Assert.That(vm.MapLeft, Is.EqualTo(6));
            Assert.That(vm.MapRight, Is.EqualTo(7));
            Assert.That(vm.SelectedMapDown?.Id, Is.EqualTo(1));
            Assert.That(vm.SelectedMapLeft?.Id, Is.EqualTo(6));
            Assert.That(vm.SelectedMapRight?.Id, Is.EqualTo(7));
            Assert.That(vm.NeighborMapDown?.Name, Is.EqualTo("Center"));
            Assert.That(vm.NeighborMapLeft?.Name, Is.EqualTo("NorthWest"));
            Assert.That(vm.NeighborMapRight?.Name, Is.EqualTo("NorthEast"));
        });
    }

    [Test]
    public void NeighborClick_UpLeftDiagonal_SwitchesToDiagonalMap()
    {
        var vm = BuildOffline(SampleWorld());
        vm.SelectedMap = Row(vm, 1);

        // Up-left diagonal of Center resolves via Up(2).Left = 6 (NorthWest).
        Assert.That(vm.NeighborMapUpLeft?.Name, Is.EqualTo("NorthWest"));

        vm.NeighborMapClickedCommand.Execute(NeighborCell.UpLeft);

        Assert.That(vm.SelectedMap!.Index, Is.EqualTo(6));
        Assert.That(vm.MapRight, Is.EqualTo(2));
        Assert.That(vm.MapDown, Is.EqualTo(4));
    }

    // Navigation must resolve by id, not by record reference: after a row's record is re-installed (a
    // distinct object with equal data, as LoadRecord does), the destination must still be found.
    [Test]
    public void NeighborClick_AfterRecordReinstalled_StillNavigates()
    {
        var world = SampleWorld();
        var vm = BuildOffline(world);
        vm.SelectedMap = Row(vm, 1);

        // Replace the row's record with an equivalent-but-distinct object; a ReferenceEquals-based
        // lookup would fail to match this and silently refuse to navigate.
        var freshNorth = Map("North", down: 1, left: 6, right: 7);
        Row(vm, 2).LoadRecord(freshNorth);

        vm.NeighborMapClickedCommand.Execute(NeighborCell.Up);

        Assert.That(vm.SelectedMap!.Index, Is.EqualTo(2),
            "neighbor-click must navigate even if the row's record was re-installed");
    }

    // Online: a linked neighbor whose record hasn't been fetched yet renders as a black cell. Clicking it
    // must still switch to that map (id-based) so the normal load path fills in its details; the old
    // record-reference lookup returned null for an unloaded neighbor and did nothing.
    [Test]
    public void NeighborClick_ToUnloadedNeighbor_Online_Navigates()
    {
        var data = new EditorDataService();
        typeof(EditorDataService).GetProperty(nameof(EditorDataService.OfflineMaps))!
            .SetValue(data, new MapRecord[SampleWorld().Length]);
        var names = System.Linq.Enumerable.Range(1, SampleWorld().Length - 1)
            .Select(i => new Mirage.Shared.Protocol.Packets.EditorDataPacket.NameEntry(i, SampleWorld()[i].Name))
            .ToArray();
        data.LoadOnline(new Mirage.Shared.Protocol.Packets.EditorDataPacket { Maps = names, Items = names });
        var vm = new MapEditorViewModel(data, new EditorConnection());
        vm.LoadOnline();
        // Load ONLY the center; its North neighbor (map 2) stays unloaded (IsLoaded == false).
        Row(vm, 1).LoadRecord(Map("Center", up: 2, down: 3, left: 4, right: 5));
        vm.SelectedMap = Row(vm, 1);
        Assert.That(Row(vm, 2).IsLoaded, Is.False, "precondition: North is not loaded");
        Assert.That(vm.NeighborMapUp, Is.Null, "precondition: unloaded neighbor resolves to null (black cell)");

        vm.NeighborMapClickedCommand.Execute(NeighborCell.Up);

        Assert.That(vm.SelectedMap!.Index, Is.EqualTo(2),
            "clicking an unloaded linked neighbor must still navigate to it");
    }
}

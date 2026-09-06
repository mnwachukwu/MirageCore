using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Editor.Tests.Authoring;

/// <summary>
/// Copying a map, and the three things that make it different from copying any other record.
///
/// <para><b>The neighbor links cannot come along.</b> Every link is half of a pair: map 2 says "up is 3"
/// only because map 3 says "down is 2". A copy that kept them would claim an adjacency the other side has
/// never heard of, and the map editor walks those links to render and to navigate — so the copy would sit
/// inside somebody else's neighborhood.</para>
///
/// <para><b>The revision belongs to the SLOT.</b> Clients cache map data per slot number and compare
/// revisions to decide whether what they hold is stale. A copy carrying the source's revision into a slot
/// that had a higher one tells every client its old cached tiles are still current.</para>
///
/// <para><b>"Empty" has to mean empty.</b> A map can carry a fully painted layout and no name at all, and
/// the list still labels that slot "(empty)". Picking copy targets by name alone would overwrite it.</para>
/// </summary>
[TestFixture]
public class MapCopyTests
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

    private static MapRecord Authored() => new()
    {
        Name = "Fenn's Clearing",
        DisplayName = "Fenn's Clearing",
        Revision = 4,
        Up = 3, Down = 5, Left = 6, Right = 7,
        BootMap = 12, BootX = 4, BootY = 5,
        MapGroup = 2,
        Music = 9,
    };

    private static MapRecord[] World(params MapRecord[] authored)
    {
        var all = new MapRecord[authored.Length + 3];
        for (int i = 0; i < all.Length; i++) all[i] = new MapRecord();
        for (int i = 0; i < authored.Length; i++) all[i + 1] = authored[i];
        return all;
    }

    private static MapRowViewModel Row(MapEditorViewModel vm, int index) => vm.Maps.First(m => m.Index == index);

    [Test]
    public void CopiedMap_DropsItsNeighborLinks()
    {
        var vm = BuildOffline(World(Authored()));
        vm.SelectedMap = Row(vm, 1);

        vm.CopyMapCommand.Execute(null);

        var copy = Row(vm, 2).Record;
        Assert.Multiple(() =>
        {
            Assert.That(copy.Up, Is.Zero);
            Assert.That(copy.Down, Is.Zero);
            Assert.That(copy.Left, Is.Zero);
            Assert.That(copy.Right, Is.Zero);
        });
    }

    /// <summary>Everything that is a PROPERTY of the map rather than an edge of the neighbor graph comes
    /// along — otherwise the copy is not a starting point, it is a blank with a name.</summary>
    [Test]
    public void CopiedMap_KeepsItsBootPointGroupAndContent()
    {
        var vm = BuildOffline(World(Authored()));
        vm.SelectedMap = Row(vm, 1);

        vm.CopyMapCommand.Execute(null);

        var copy = Row(vm, 2).Record;
        Assert.Multiple(() =>
        {
            Assert.That(copy.Name, Is.EqualTo("Fenn's Clearing (Copy)"));
            Assert.That(copy.DisplayName, Is.EqualTo("Fenn's Clearing"));
            Assert.That(copy.BootMap, Is.EqualTo(12));
            Assert.That(copy.BootX, Is.EqualTo(4));
            Assert.That(copy.BootY, Is.EqualTo(5));
            Assert.That(copy.MapGroup, Is.EqualTo(2));
            Assert.That(copy.Music, Is.EqualTo(9));
        });
    }

    /// <summary>Revision counts saves of THIS slot, and a copy has never been saved — the first save takes
    /// it to 1. Carrying the source's number over would claim a save history the slot does not have.
    /// <para>Safe because clients compare revisions for EQUALITY, not order: <c>cachedRev == p.Revision</c>
    /// in ClientPacketHandler.Maps. A number lower than the one a client holds still reads as "not what I
    /// cached" and refetches.</para></summary>
    [Test]
    public void CopiedMap_StartsAtRevisionZero()
    {
        // Slot 2 has been saved 30 times; the source has been saved 4.
        var world = World(Authored(), new MapRecord { Revision = 30 });
        var vm = BuildOffline(world);
        vm.SelectedMap = Row(vm, 1);

        // Slot 2 carries a revision but no name or content, so it is still a legitimate target.
        vm.CopyMapCommand.Execute(null);

        Assert.That(Row(vm, 2).Record.Revision, Is.Zero);
    }

    // ── When Copy is offered at all ───────────────────────────────────────────

    [Test]
    public void CopyIsUnavailable_WithNoMapOpen()
    {
        var vm = BuildOffline(World(Authored()));
        vm.SelectedMap = null;

        Assert.That(vm.CanCopyMap, Is.False);
    }

    /// <summary>Copying a blank slot would just spend another slot to hold a second nothing.</summary>
    [Test]
    public void CopyIsUnavailable_WhenTheOpenMapIsBlank()
    {
        var vm = BuildOffline(World(Authored()));
        vm.SelectedMap = Row(vm, 2);   // an untouched slot

        Assert.Multiple(() =>
        {
            Assert.That(vm.CanCopyMap, Is.False);
            Assert.That(vm.CopyMapTooltip, Does.Contain("empty").IgnoreCase,
                "the disabled button has to say why");
        });
    }

    [Test]
    public void CopyIsAvailable_ForAnAuthoredMap()
    {
        var vm = BuildOffline(World(Authored()));
        vm.SelectedMap = Row(vm, 1);

        Assert.That(vm.CanCopyMap, Is.True);
    }

    [Test]
    public void CopiedMap_ArrivesDirtyAndSelected()
    {
        var vm = BuildOffline(World(Authored()));
        vm.SelectedMap = Row(vm, 1);

        vm.CopyMapCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(Row(vm, 2).IsDirty, Is.True);
            Assert.That(vm.SelectedMap, Is.SameAs(Row(vm, 2)));
            Assert.That(Row(vm, 1).IsDirty, Is.False, "copying reads the original, it does not edit it");
        });
    }

    [Test]
    public void CopiedMap_IsDeep_SoPaintingItCannotReachTheOriginal()
    {
        var source = Authored();
        source.EditTile(0, 0, t => t.WithCell(LayerType.Ground, 0, LayerCell.Pack(5, 0, false)));
        var vm = BuildOffline(World(source));
        vm.SelectedMap = Row(vm, 1);

        vm.CopyMapCommand.Execute(null);
        Row(vm, 2).Record.EditTile(0, 0, t => t.WithCell(LayerType.Ground, 0, LayerCell.Pack(99, 0, false)));

        Assert.That(Row(vm, 1).Record.Tile[0, 0].Ground[0], Is.EqualTo(LayerCell.Pack(5, 0, false)));
    }

    /// <summary>The trap the name-only test would walk into: a painted map nobody bothered to name still
    /// reads as "(empty)" in the list, and overwriting it would be silent.</summary>
    [Test]
    public void APaintedMapWithNoName_IsNotACopyTarget()
    {
        var unnamedButPainted = new MapRecord();
        unnamedButPainted.EditTile(3, 3, t => t.WithCell(LayerType.Ground, 0, LayerCell.Pack(7, 0, false)));
        var vm = BuildOffline(World(Authored(), unnamedButPainted));
        vm.SelectedMap = Row(vm, 1);

        vm.CopyMapCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(Row(vm, 2).Record.Tile[3, 3].Ground[0], Is.EqualTo(LayerCell.Pack(7, 0, false)),
                "slot 2 held authored work and had to be skipped");
            Assert.That(Row(vm, 3).Record.Name, Is.EqualTo("Fenn's Clearing (Copy)"));
        });
    }

    [Test]
    public void AMapHoldingOnlyAnNpcSpawn_IsNotACopyTarget()
    {
        var unnamedWithNpc = new MapRecord();
        unnamedWithNpc.Npcs.Add(new MapNpcEntry(12, null, null));
        var vm = BuildOffline(World(Authored(), unnamedWithNpc));
        vm.SelectedMap = Row(vm, 1);

        vm.CopyMapCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(Row(vm, 2).Record.Npcs, Has.Count.EqualTo(1));
            Assert.That(Row(vm, 3).Record.Name, Is.EqualTo("Fenn's Clearing (Copy)"));
        });
    }
}

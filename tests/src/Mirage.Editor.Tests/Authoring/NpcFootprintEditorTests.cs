using Mirage.Editor.Localization;
using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;

namespace Mirage.Editor.Tests.Authoring;

/// <summary>
/// Size-aware NPC placement, editor side. Two behaviors:
///  C1 — bidirectional footprint collision: a placed NPC reserves its whole SxS footprint, so the attribute
///       tools refuse to write a tile attribute under it (the reverse — pinning onto an attribute tile — is
///       already blocked by MapNpcPlacement.ValidatePin's all-Walkable rule, covered by MapNpcPlacementTests).
///  C2 — resize re-prompt: when an NPC's Size changes live, every LOADED map that PINS that NPC is flagged
///       dirty (a resize can invalidate a prior-valid pin) and the online size cache is refreshed so the
///       overlay + validation stop reading the stale connect-time snapshot.
/// The NPC id doubles as nothing here; sizes are set explicitly on the records / online cache.
/// </summary>
[TestFixture]
public class NpcFootprintEditorTests
{
    private static void Set(EditorDataService data, string prop, object value) =>
        typeof(EditorDataService).GetProperty(prop)!.SetValue(data, value);

    // Offline VM with the given maps + npcs installed; optional online NPC-size cache (leaves IsOnline false —
    // that keys off OnlineItems — so map selection/loading stays synchronous like the other editor VM tests).
    private static (MapEditorViewModel vm, EditorDataService data) Build(
        MapRecord[] maps, NpcRecord[] npcs, int[]? onlineSizes = null)
    {
        var data = new EditorDataService();
        Set(data, nameof(EditorDataService.OfflineMaps), maps);
        Set(data, nameof(EditorDataService.OfflineNpcs), npcs);
        if (onlineSizes is not null) Set(data, nameof(EditorDataService.OnlineNpcSizes), onlineSizes);
        var vm = new MapEditorViewModel(data, new EditorConnection());
        vm.LoadOffline();
        return (vm, data);
    }

    private static MapRowViewModel Row(MapEditorViewModel vm, int index) =>
        vm.Maps.First(m => m.Index == index);

    // A 3-entry NPC table where NPC #2 is 2x2. Index 0/1 are the unused sentinel + a 1x1.
    private static NpcRecord[] Npcs2x2() =>
    [
        new NpcRecord(),
        new NpcRecord { Size = 1 },
        new NpcRecord { Size = 2 },   // #2 = 2x2 footprint
    ];

    // ── C1: attribute placement collides with a pinned footprint ────────────────

    [Test]
    public void AttributePlacement_OnPinnedFootprint_IsBlocked()
    {
        // NPC #2 (2x2) pinned at (5,5) covers (5,5)-(6,6). Clicking the Blocked tool on (6,6) must be refused.
        var map = new MapRecord { Name = "Pinned" };
        map.Npcs.Add(new MapNpcEntry(2, 5, 5));
        var (vm, _) = Build([new MapRecord { Name = "(none)" }, map], Npcs2x2());

        vm.SelectedMap = Row(vm, 1);
        vm.SelectedMode = EditorMode.Attribute;
        vm.SelectedAttributeTool = AttributeTool.Blocked;

        vm.TileClicked(new TileClick(6, 6, false, false));

        Assert.Multiple(() =>
        {
            Assert.That(map.Tile[6, 6].Type, Is.EqualTo(TileType.Walkable),
                "a tile under a placed NPC's footprint must stay Walkable - the attribute is refused");
            Assert.That(vm.StatusMessage,
                Is.EqualTo(EditorStrings.Get(EditorStrings.MapEditorStatus_AttrUnderNpc)),
                "the block must report why it was refused");
        });
    }

    [Test]
    public void AttributePlacement_OffPinnedFootprint_IsAllowed()
    {
        // Same 2x2 pin at (5,5); (8,8) is clear of the footprint, so the attribute writes normally.
        var map = new MapRecord { Name = "Pinned" };
        map.Npcs.Add(new MapNpcEntry(2, 5, 5));
        var (vm, _) = Build([new MapRecord { Name = "(none)" }, map], Npcs2x2());

        vm.SelectedMap = Row(vm, 1);
        vm.SelectedMode = EditorMode.Attribute;
        vm.SelectedAttributeTool = AttributeTool.Blocked;

        vm.TileClicked(new TileClick(8, 8, false, false));

        Assert.That(map.Tile[8, 8].Type, Is.EqualTo(TileType.Blocked),
            "a tile clear of every footprint accepts the attribute as usual");
    }

    // ── C2: an NPC resize re-prompts only the maps that pin it ──────────────────

    [Test]
    public void NpcResize_DirtiesOnlyPinningMaps_AndRefreshesSizeCache()
    {
        // 1 pins #2, 2 pins #3, 3 has no pins, 4 references #2 but UNPINNED (spawns randomly - a resize
        // can't invalidate it, so it must stay clean).
        var m1 = new MapRecord { Name = "PinsTwo" }; m1.Npcs.Add(new MapNpcEntry(2, 5, 5));
        var m2 = new MapRecord { Name = "PinsThree" }; m2.Npcs.Add(new MapNpcEntry(3, 5, 5));
        var m3 = new MapRecord { Name = "NoPins" };
        var m4 = new MapRecord { Name = "TwoUnpinned" }; m4.Npcs.Add(new MapNpcEntry(2, null, null));

        var sizes = new int[RecordLimits.Default.Npcs + 1];
        sizes[2] = 1;
        sizes[3] = 1;
        var (vm, data) = Build([new MapRecord { Name = "(none)" }, m1, m2, m3, m4], Npcs2x2(), sizes);

        Assume.That(vm.Maps.Any(m => m.IsDirty), Is.False, "precondition: freshly loaded maps are clean");

        vm.OnNpcLiveUpdated(2, 3);   // NPC #2 grew to 3x3

        Assert.Multiple(() =>
        {
            Assert.That(Row(vm, 1).IsDirty, Is.True, "the map that pins the resized NPC is re-prompted");
            Assert.That(Row(vm, 2).IsDirty, Is.False, "a map pinning a DIFFERENT NPC is untouched");
            Assert.That(Row(vm, 3).IsDirty, Is.False, "a map with no pins is untouched");
            Assert.That(Row(vm, 4).IsDirty, Is.False, "an UNPINNED reference spawns randomly - not re-prompted");
            Assert.That(data.NpcSize(2), Is.EqualTo(3),
                "the online size cache is refreshed so the overlay + validation see the new size");
        });
    }

    // ── MODE 2: transient per-row placement ─────────────────────────────────────

    [Test]
    public void BeginPlace_EmptyRow_DoesNotEnterMode()
    {
        var map = new MapRecord { Name = "M" };
        map.Npcs.Add(new MapNpcEntry(0, null, null));   // a row with no NPC assigned yet
        var (vm, _) = Build([new MapRecord { Name = "(none)" }, map], Npcs2x2());
        vm.SelectedMap = Row(vm, 1);

        vm.BeginPlaceNpcRow(0);

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsPlacingNpc, Is.False, "an empty row has nothing to place");
            Assert.That(vm.StatusMessage,
                Is.EqualTo(EditorStrings.Get(EditorStrings.MapEditorStatus_PlaceNeedsNpc)));
        });
    }

    [Test]
    public void BeginPlace_ThenValidTile_PinsRowAndExitsMode()
    {
        var map = new MapRecord { Name = "M" };
        map.Npcs.Add(new MapNpcEntry(2, null, null));   // row 0: a 2x2 NPC, unpinned
        var (vm, _) = Build([new MapRecord { Name = "(none)" }, map], Npcs2x2());
        vm.SelectedMap = Row(vm, 1);

        vm.BeginPlaceNpcRow(0);
        Assume.That(vm.IsPlacingNpc, Is.True, "precondition: placement mode is active");
        Assume.That(vm.SelectedMode, Is.EqualTo(EditorMode.Attribute), "placement forces Attribute mode");
        Assume.That(vm.CanPlacePlacingNpcAt(3, 3), Is.True, "precondition: (3,3) is a legal 2x2 pin");

        vm.PlaceNpcAtHover(3, 3);

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsPlacingNpc, Is.False, "a successful place exits the mode");
            Assert.That(map.Npcs[0].PinX, Is.EqualTo(3));
            Assert.That(map.Npcs[0].PinY, Is.EqualTo(3));
        });
    }

    [Test]
    public void Place_OnInvalidTile_ReportsAndStaysInMode()
    {
        var map = new MapRecord { Name = "M" };
        map.Npcs.Add(new MapNpcEntry(2, null, null));
        var (vm, _) = Build([new MapRecord { Name = "(none)" }, map], Npcs2x2());
        vm.SelectedMap = Row(vm, 1);

        vm.BeginPlaceNpcRow(0);
        // A 2x2 anchored at the far corner spills off the map — invalid.
        Assume.That(vm.CanPlacePlacingNpcAt(Constants.MaxMapX, Constants.MaxMapY), Is.False);

        vm.PlaceNpcAtHover(Constants.MaxMapX, Constants.MaxMapY);

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsPlacingNpc, Is.True, "an invalid tile keeps placement mode active for a retry");
            Assert.That(map.Npcs[0].HasPin, Is.False, "no pin is written for an invalid tile");
            Assert.That(vm.StatusMessage,
                Is.EqualTo(EditorStrings.Get(EditorStrings.MapEditorStatus_PlaceOffMap)));
        });
    }

    [Test]
    public void Place_MovesExistingPin_AndUndoRestoresIt()
    {
        var map = new MapRecord { Name = "M" };
        map.Npcs.Add(new MapNpcEntry(2, 3, 3));   // row 0: already pinned at (3,3)
        var (vm, _) = Build([new MapRecord { Name = "(none)" }, map], Npcs2x2());
        vm.SelectedMap = Row(vm, 1);

        vm.BeginPlaceNpcRow(0);
        vm.PlaceNpcAtHover(6, 6);   // re-place elsewhere → the single pin MOVES

        Assert.Multiple(() =>
        {
            Assert.That(map.Npcs[0].PinX, Is.EqualTo(6), "re-placing moves the pin to the new tile");
            Assert.That(map.Npcs[0].PinY, Is.EqualTo(6));
        });

        vm.UndoCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(map.Npcs[0].PinX, Is.EqualTo(3), "undo restores the original pin tile");
            Assert.That(map.Npcs[0].PinY, Is.EqualTo(3));
        });
    }
}

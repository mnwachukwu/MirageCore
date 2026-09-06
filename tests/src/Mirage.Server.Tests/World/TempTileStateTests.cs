using Mirage.Server.Core.World;
using Mirage.Shared;
using NUnit.Framework;

namespace Mirage.Server.Tests.World;

/// <summary>
/// <see cref="TempTileState"/> — the per-map runtime tile state, and the two work lists the game loop's
/// sweeps read.
///
/// <para>Both are sparse: an entry exists only while something is actually running on that tile, and its
/// presence IS the active flag. That is what bounds the door and item sweeps by how much is happening on a
/// map instead of by how many tiles it has — the door sweep runs on every observed map every AI tick, and
/// the item sweep runs on every map in the world once a second, so a per-tile scan would grow with map
/// area in the two hottest places it could.</para>
/// </summary>
[TestFixture]
public class TempTileStateTests
{
    // ── The work lists ────────────────────────────────────────────────────────

    /// <summary>A map where nothing is open and nothing has been taken hands the sweeps nothing to do,
    /// which is the whole of the early-out they gate on.</summary>
    [Test]
    public void AFreshMap_OffersNoWorkToEitherSweep()
    {
        var temp = new TempTileState();

        Assert.Multiple(() =>
        {
            Assert.That(temp.OpenDoors, Is.Empty);
            Assert.That(temp.TakenTileItems, Is.Empty);
        });
    }

    /// <summary>The work list holds exactly what is running — never a cell per tile.</summary>
    [Test]
    public void TheWorkListsHoldOneEntryPerRunningThing_NotOnePerTile()
    {
        var temp = new TempTileState();

        temp.OpenDoor(3, 3, WorldLayer.Ground, 1_000);
        temp.OpenDoor(9, 4, WorldLayer.Ground, 2_000);
        temp.TakeTileItem(5, 5, WorldLayer.Ground, 1_000);

        Assert.Multiple(() =>
        {
            Assert.That(temp.OpenDoors, Has.Count.EqualTo(2));
            Assert.That(temp.TakenTileItems, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void ClosingADoor_TakesItOffTheWorkList()
    {
        var temp = new TempTileState();
        temp.OpenDoor(3, 3, WorldLayer.Ground, 1_000);

        temp.CloseDoor(3, 3, WorldLayer.Ground);

        Assert.Multiple(() =>
        {
            Assert.That(temp.IsDoorOpen(3, 3, WorldLayer.Ground), Is.False);
            Assert.That(temp.OpenDoors, Is.Empty, "a shut door is not swept again");
        });
    }

    [Test]
    public void RestoringATileItem_TakesItOffTheWorkList()
    {
        var temp = new TempTileState();
        temp.TakeTileItem(5, 5, WorldLayer.Ground, 1_000);

        temp.RestoreTileItem(5, 5, WorldLayer.Ground);

        Assert.Multiple(() =>
        {
            Assert.That(temp.TileItemTakenAt(5, 5, WorldLayer.Ground), Is.Zero);
            Assert.That(temp.TakenTileItems, Is.Empty, "an item standing on its tile is not swept again");
        });
    }

    // ── Per-layer independence ────────────────────────────────────────────────

    /// <summary>A Key door on a bridge deck and the ground door beneath it are two doors, tracked and aged
    /// out separately.</summary>
    [Test]
    public void OneTilesTwoLayers_AreTwoDoorsWithTwoClocks()
    {
        var temp = new TempTileState();

        temp.OpenDoor(6, 6, WorldLayer.Ground, 1_000);
        temp.OpenDoor(6, 6, WorldLayer.Fringe, 4_000);

        Assert.Multiple(() =>
        {
            Assert.That(temp.OpenDoors, Has.Count.EqualTo(2));
            Assert.That(temp.OpenDoors[(6, 6, WorldLayer.Ground)], Is.EqualTo(1_000));
            Assert.That(temp.OpenDoors[(6, 6, WorldLayer.Fringe)], Is.EqualTo(4_000));
        });

        temp.CloseDoor(6, 6, WorldLayer.Ground);

        Assert.Multiple(() =>
        {
            Assert.That(temp.IsDoorOpen(6, 6, WorldLayer.Ground), Is.False);
            Assert.That(temp.IsDoorOpen(6, 6, WorldLayer.Fringe), Is.True, "the deck door is untouched");
        });
    }

    [Test]
    public void OneTilesTwoLayers_AreTwoTileItemsWithTwoClocks()
    {
        var temp = new TempTileState();

        temp.TakeTileItem(4, 5, WorldLayer.Ground, 1_000);
        temp.TakeTileItem(4, 5, WorldLayer.Fringe, 7_000);

        Assert.Multiple(() =>
        {
            Assert.That(temp.TileItemTakenAt(4, 5, WorldLayer.Ground), Is.EqualTo(1_000));
            Assert.That(temp.TileItemTakenAt(4, 5, WorldLayer.Fringe), Is.EqualTo(7_000));
        });

        temp.RestoreTileItem(4, 5, WorldLayer.Ground);

        Assert.Multiple(() =>
        {
            Assert.That(temp.TileItemTakenAt(4, 5, WorldLayer.Ground), Is.Zero);
            Assert.That(temp.TileItemTakenAt(4, 5, WorldLayer.Fringe), Is.EqualTo(7_000), "the deck item is untouched");
        });
    }

    // ── Tick 0 ────────────────────────────────────────────────────────────────

    /// <summary>Presence is the flag, so a door opened at tick 0 is open. There is no stamp value that
    /// doubles as "shut" and therefore no clamp to work around one.</summary>
    [Test]
    public void ADoorOpenedAtTickZero_IsOpen()
    {
        var temp = new TempTileState();

        temp.OpenDoor(2, 2, WorldLayer.Ground, 0);

        Assert.Multiple(() =>
        {
            Assert.That(temp.IsDoorOpen(2, 2, WorldLayer.Ground), Is.True);
            Assert.That(temp.OpenDoors[(2, 2, WorldLayer.Ground)], Is.Zero, "and its clock starts at 0, not 1");
        });
    }

    /// <summary>Re-stamping an open door would extend its window, so callers gate on
    /// <see cref="TempTileState.IsDoorOpen"/> first — this pins that the gate is the thing protecting it,
    /// because the write itself does overwrite.</summary>
    [Test]
    public void ReOpeningADoor_OverwritesItsClock()
    {
        var temp = new TempTileState();

        temp.OpenDoor(2, 2, WorldLayer.Ground, 1_000);
        temp.OpenDoor(2, 2, WorldLayer.Ground, 9_000);

        Assert.That(temp.OpenDoors[(2, 2, WorldLayer.Ground)], Is.EqualTo(9_000));
    }
}

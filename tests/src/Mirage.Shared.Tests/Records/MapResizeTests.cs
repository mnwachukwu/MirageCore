using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Records;

/// <summary>
/// Resizing a map.
///
/// <para>It cannot be undone: the discarded tiles are not written anywhere first, so the only protection an
/// author gets is being told what they are about to lose. <see cref="MapResize.CostOf"/> is that telling,
/// and these hold it to counting exactly what goes — an over-count cries wolf on a harmless resize, and an
/// under-count loses work that was reported as safe.</para>
/// </summary>
[TestFixture]
public class MapResizeTests
{
    private const int Map = 1, Other = 2;

    private static MapRecord Authored(int w, int h)
    {
        var map = new MapRecord(w, h);
        map.EditTile(0, 0, t => t.WithCell(LayerType.Ground, 0, LayerCell.Pack(5, 0, false)));
        return map;
    }

    // ── Growing ───────────────────────────────────────────────────────────────

    [Test]
    public void Growing_KeepsEveryTileAndCostsNothing()
    {
        var map = Authored(16, 12);
        map.EditTile(15, 11, t => t with { Type = TileType.Blocked });

        var cost = MapResize.CostOf(map, new MapSize(24, 20));
        MapResize.Apply(map, new MapSize(24, 20));

        Assert.Multiple(() =>
        {
            Assert.That(cost.IsLossy, Is.False, "nothing falls outside a larger map");
            Assert.That((map.Width, map.Height), Is.EqualTo((24, 20)));
            Assert.That(map.Tile[15, 11].Type, Is.EqualTo(TileType.Blocked), "the old corner is where it was");
            Assert.That(map.Tile[23, 19].Type, Is.EqualTo(TileType.Walkable), "new ground is blank");
            Assert.That(map.Tile[23, 19], Is.Not.Null, "and addressable");
        });
    }

    // ── Shrinking ─────────────────────────────────────────────────────────────

    [Test]
    public void Shrinking_KeepsWhatFitsAndDiscardsTheRest()
    {
        var map = Authored(24, 20);
        map.EditTile(3, 4, t => t with { Type = TileType.Blocked });
        map.EditTile(20, 18, t => t with { Type = TileType.Blocked });   // outside 16x12

        MapResize.Apply(map, new MapSize(16, 12));

        Assert.Multiple(() =>
        {
            Assert.That((map.Width, map.Height), Is.EqualTo((16, 12)));
            Assert.That(map.Tile[3, 4].Type, Is.EqualTo(TileType.Blocked), "a tile inside both sizes is untouched");
            Assert.That(map.Contains(20, 18), Is.False, "and the far one is simply not there");
        });
    }

    /// <summary>Only AUTHORED tiles are counted. Trimming empty margin is a free operation and has to read
    /// as one, or the warning becomes noise an author learns to click through.</summary>
    [Test]
    public void ShrinkingIntoBlankGround_CostsNothing()
    {
        var map = Authored(24, 20);
        map.EditTile(3, 4, t => t with { Type = TileType.Blocked });   // well inside the target

        Assert.That(MapResize.CostOf(map, new MapSize(16, 12)).IsLossy, Is.False);
    }

    [Test]
    public void EveryKindOfAuthoringOutsideTheNewBounds_IsCounted()
    {
        var map = Authored(24, 20);
        map.EditTile(20, 2, t => t with { Type = TileType.Blocked });                        // an attribute
        map.EditTile(21, 3, t => t.WithCell(LayerType.Ground, 0, LayerCell.Pack(7, 0, false)));               // ground art
        map.EditTile(22, 4, t => t.WithCell(LayerType.Canopy, 0, LayerCell.Pack(7, 0, false)));               // canopy art
        map.EditTile(23, 5, t => t with { FringeAttr = new FringeAttr { Type = TileType.Blocked } });   // a fringe plane

        Assert.That(MapResize.CostOf(map, new MapSize(16, 12)).AuthoredTiles, Is.EqualTo(4));
    }

    [Test]
    public void ALightOutsideTheNewBounds_IsCountedAndDropped()
    {
        var map = Authored(24, 20);
        map.Lights.Add(new PlacedLight(Guid.NewGuid(), 4, 4, LightSpec.Torch));
        map.Lights.Add(new PlacedLight(Guid.NewGuid(), 20, 4, LightSpec.Torch));

        var cost = MapResize.CostOf(map, new MapSize(16, 12));
        MapResize.Apply(map, new MapSize(16, 12));

        Assert.Multiple(() =>
        {
            Assert.That(cost.Lights, Is.EqualTo(1));
            Assert.That(map.Lights, Has.Count.EqualTo(1), "the one still on the map stays");
            Assert.That(map.Lights[0].X, Is.EqualTo(4));
        });
    }

    /// <summary>A spawn pin outside the new bounds is cleared, not left pointing at a tile that is gone —
    /// the NPC keeps its slot and goes back to spawning wherever it fits.</summary>
    [Test]
    public void ASpawnPinOutsideTheNewBounds_IsCountedAndCleared()
    {
        var map = Authored(24, 20);
        map.Npcs.Add(new MapNpcEntry(9, 2, 3));
        map.Npcs.Add(new MapNpcEntry(10, 20, 3));

        var cost = MapResize.CostOf(map, new MapSize(16, 12));
        MapResize.Apply(map, new MapSize(16, 12));

        Assert.Multiple(() =>
        {
            Assert.That(cost.NpcPins, Is.EqualTo(1));
            Assert.That(map.Npcs, Has.Count.EqualTo(2), "both NPCs keep their slots");
            Assert.That(map.Npcs[0].HasPin, Is.True, "the pin that still fits is untouched");
            Assert.That(map.Npcs[1].HasPin, Is.False, "the one that does not is cleared");
            Assert.That(map.Npcs[1].Npc, Is.EqualTo(10), "and it is still the same NPC");
        });
    }

    // ── Warps arriving from elsewhere ─────────────────────────────────────────

    /// <summary>The failure an author cannot see from the map they are resizing: a door on some OTHER map,
    /// aimed at a tile this size does not include.</summary>
    [Test]
    public void AWarpFromAnotherMapLandingOnRemovedGround_IsCounted()
    {
        var world = new MapRecord?[3];
        world[Map] = Authored(24, 20);
        world[Other] = new MapRecord(16, 12);

        // Aimed at (20,2), which a 16x12 map does not have.
        world[Other]!.EditTile(1, 1, t => t with { Type = TileType.Warp, WarpMap = Map, WarpX = 20, WarpY = 2 });
        // Aimed at (5,5), which it still does.
        world[Other]!.EditTile(2, 2, t => t with { Type = TileType.Warp, WarpMap = Map, WarpX = 5, WarpY = 5 });

        var cost = MapResize.CostOf(world[Map]!, new MapSize(16, 12), world, Map);

        Assert.That(cost.InboundWarps, Is.EqualTo(1), "only the one that would land on nothing");
    }

    [Test]
    public void AFringeWarpLandingOnRemovedGround_IsCountedToo()
    {
        var world = new MapRecord?[3];
        world[Map] = Authored(24, 20);
        world[Other] = new MapRecord(16, 12);
        world[Other]!.EditTile(1, 1, t => t with
        {
            FringeAttr = new FringeAttr { Type = TileType.Warp, WarpMap = Map, WarpX = 3, WarpY = 18 },
        });

        Assert.That(MapResize.CostOf(world[Map]!, new MapSize(16, 12), world, Map).InboundWarps, Is.EqualTo(1));
    }

    [Test]
    public void WarpsToOtherMaps_AreNotThisResizesConcern()
    {
        var world = new MapRecord?[3];
        world[Map] = Authored(24, 20);
        world[Other] = new MapRecord(16, 12);
        // Somewhere else entirely, so this resize is none of its business.
        world[Other]!.EditTile(1, 1, t => t with { Type = TileType.Warp, WarpMap = 99, WarpX = 20, WarpY = 18 });

        Assert.That(MapResize.CostOf(world[Map]!, new MapSize(16, 12), world, Map).InboundWarps, Is.Zero);
    }

    // ── The link rule ─────────────────────────────────────────────────────────
    // A neighbourhood measures in one size, so a linked map cannot be resized on its own. Both directions
    // of a link count: naming a neighbour and being named by one are the same join seen from either end.

    [Test]
    public void AMapWithNoLinks_IsFreeToResize()
    {
        var world = new MapRecord?[3];
        world[Map] = new MapRecord(16, 12);
        world[Other] = new MapRecord(16, 12);

        Assert.That(MapResize.LinkedMaps(world, Map), Is.Empty);
    }

    [Test]
    public void AMapThatNamesANeighbour_IsLinked()
    {
        var world = new MapRecord?[3];
        world[Map] = new MapRecord(16, 12);
        world[Other] = new MapRecord(16, 12);
        world[Map]!.Right = Other;

        Assert.That(MapResize.LinkedMaps(world, Map), Is.EqualTo(new[] { Other }));
    }

    /// <summary>The half an author cannot see from the map they are on: someone else's map pointing here.</summary>
    [Test]
    public void AMapNAMEDByANeighbour_IsLinkedToo()
    {
        var world = new MapRecord?[3];
        world[Map] = new MapRecord(16, 12);
        world[Other] = new MapRecord(16, 12);
        world[Other]!.Left = Map;

        Assert.That(MapResize.LinkedMaps(world, Map), Is.EqualTo(new[] { Other }));
    }

    [Test]
    public void AJoinSeenFromBothEnds_IsOneNeighbour()
    {
        var world = new MapRecord?[3];
        world[Map] = new MapRecord(16, 12);
        world[Other] = new MapRecord(16, 12);
        world[Map]!.Right = Other;
        world[Other]!.Left = Map;

        Assert.That(MapResize.LinkedMaps(world, Map), Is.EqualTo(new[] { Other }), "listed once, not twice");
    }

    [Test]
    public void AMapLinkedToItself_IsNotLinkedToAnything()
    {
        var world = new MapRecord?[3];
        world[Map] = new MapRecord(16, 12);
        world[Map]!.Up = Map;

        Assert.That(MapResize.LinkedMaps(world, Map), Is.Empty, "a map is always its own size");
    }

    // ── Bounds ────────────────────────────────────────────────────────────────

    [Test]
    public void AMapCannotBeResizedToNothing()
    {
        var map = Authored(16, 12);

        MapResize.Apply(map, new MapSize(0, 0));

        Assert.That((map.Width, map.Height), Is.EqualTo((1, 1)), "one tile is the floor");
    }

    [Test]
    public void ResizingToTheSameSize_ChangesNothing()
    {
        var map = Authored(16, 12);
        map.EditTile(3, 4, t => t with { Type = TileType.Blocked });

        var cost = MapResize.CostOf(map, new MapSize(16, 12));
        MapResize.Apply(map, new MapSize(16, 12));

        Assert.Multiple(() =>
        {
            Assert.That(cost.IsLossy, Is.False);
            Assert.That((map.Width, map.Height), Is.EqualTo((16, 12)));
            Assert.That(map.Tile[3, 4].Type, Is.EqualTo(TileType.Blocked));
        });
    }
}

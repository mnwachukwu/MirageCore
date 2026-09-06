using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Ai;

// Size-aware fixed-spawn placement validation: a pin's SxS footprint (top-left anchor) must
// be fully on-map, all Walkable, and not overlap another pinned entry. Shared by the editor (place/render) + the
// server save backstop, so this locks the agreed rule. NPC id doubles as its size in these tests (npc 2 = 2x2).
[TestFixture]
public class MapNpcPlacementTests
{
    static int SizeById(int npcId) => System.Math.Max(1, npcId);

    static MapRecord MapWith(params MapNpcEntry[] entries)
    {
        var map = new MapRecord();          // ctor fills a fully Walkable tile grid
        map.Npcs.AddRange(entries);
        return map;
    }

    [Test]
    public void ValidPlacement_FitsOnWalkable_ReturnsNone()
    {
        var map = MapWith(new MapNpcEntry(2, null, null));   // a 2x2 NPC
        Assert.That(MapNpcPlacement.ValidatePin(map, 0, 3, 3, WorldLayer.Ground, SizeById), Is.EqualTo(NpcPlacementError.None));
    }

    [Test]
    public void FootprintOffMapEdge_ReturnsOffMap()
    {
        var map = MapWith(new MapNpcEntry(2, null, null));   // 2x2 anchored at the far corner spills off-map
        Assert.That(MapNpcPlacement.ValidatePin(map, 0, Constants.MaxMapX, Constants.MaxMapY, WorldLayer.Ground, SizeById),
            Is.EqualTo(NpcPlacementError.OffMap));
    }

    [Test]
    public void FootprintCoversBlockedTile_ReturnsOnBlocked()
    {
        var map = MapWith(new MapNpcEntry(2, null, null));   // 2x2 at (4,4) covers (5,5)
        map.EditTile(5, 5, t => t with { Type = TileType.Blocked });
        Assert.That(MapNpcPlacement.ValidatePin(map, 0, 4, 4, WorldLayer.Ground, SizeById), Is.EqualTo(NpcPlacementError.OnBlocked));
    }

    [Test]
    public void FootprintOverlapsAnotherPin_ReturnsOverlap()
    {
        // Entry 0: 2x2 pinned at (3,3) → covers (3,3)-(4,4). Entry 1: 1x1 candidate at (4,4), inside it.
        var map = MapWith(new MapNpcEntry(2, 3, 3), new MapNpcEntry(1, null, null));
        Assert.That(MapNpcPlacement.ValidatePin(map, 1, 4, 4, WorldLayer.Ground, SizeById), Is.EqualTo(NpcPlacementError.Overlap));
    }

    [Test]
    public void OverlapBelowIndex_EnforcesFirstWins()
    {
        // Entry 0 pinned at (4,4) (1x1); entry 1 pinned at (3,3) (2x2, covers (4,4) too — they overlap).
        var map = MapWith(new MapNpcEntry(1, 4, 4), new MapNpcEntry(2, 3, 3));
        // The server sanitize validates each pin against only EARLIER ones: entry 0 (index 0) checks nothing → kept.
        Assert.That(MapNpcPlacement.ValidatePin(map, 0, 4, 4, WorldLayer.Ground, SizeById, overlapBelowIndex: 0),
            Is.EqualTo(NpcPlacementError.None), "first pin wins (ignores later pins)");
        // Entry 1 checks entries < 1 (entry 0) → sees the overlap → its pin gets dropped.
        Assert.That(MapNpcPlacement.ValidatePin(map, 1, 3, 3, WorldLayer.Ground, SizeById, overlapBelowIndex: 1),
            Is.EqualTo(NpcPlacementError.Overlap), "later pin loses to the earlier one");
    }

    [Test]
    public void PinsOnDifferentLayers_MayStackOnTheSameTile()
    {
        // Entry 0 pinned on GROUND at (4,4); entry 1 a candidate on FRINGE at the same (4,4).
        var map = MapWith(new MapNpcEntry(1, 4, 4, WorldLayer.Ground), new MapNpcEntry(1, null, null));
        Assert.Multiple(() =>
        {
            // Same tile, DIFFERENT plane → no conflict (a ground mob under a bridge + a mob on the deck).
            Assert.That(MapNpcPlacement.ValidatePin(map, 1, 4, 4, WorldLayer.Fringe, SizeById),
                Is.EqualTo(NpcPlacementError.None), "a Fringe pin stacks over a Ground pin");
            // Same tile, SAME plane → still an overlap.
            Assert.That(MapNpcPlacement.ValidatePin(map, 1, 4, 4, WorldLayer.Ground, SizeById),
                Is.EqualTo(NpcPlacementError.Overlap), "two Ground pins on one tile still conflict");
        });
    }
}

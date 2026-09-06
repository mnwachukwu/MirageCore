using NUnit.Framework;

namespace Mirage.Shared.Tests.World;

/// <summary>Pure footprint math for variable-size NPCs: FootprintContains, LeadingEdgeTiles (the melee/
/// movement strip), TileRun membership, and cross-seam resolution. No world state needed.</summary>
[TestFixture]
public class WorldCoordHelperFootprintTests
{
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void FootprintContains_CoversExactlySxS(int size)
    {
        const int ax = 5, ay = 7;
        for (int j = -1; j <= size; j++)
        {
            for (int i = -1; i <= size; i++)
            {
                bool inside = i >= 0 && i < size && j >= 0 && j < size;
                Assert.That(WorldCoordHelper.FootprintContains(ax, ay, size, ax + i, ay + j), Is.EqualTo(inside),
                    $"offset ({i},{j}) for size {size}");
            }
        }
    }

    [Test]
    public void LeadingEdgeTiles_Right_IsColumnPastRightEdge()
    {
        const int ax = 4, ay = 6, size = 3;
        var run = WorldCoordHelper.LeadingEdgeTiles(ax, ay, size, Direction.Right);
        Assert.That(run.Count, Is.EqualTo(size));
        for (int j = 0; j < size; j++)
            Assert.That(run[j], Is.EqualTo((ax + size, ay + j)));
    }

    [Test]
    public void LeadingEdgeTiles_Left_IsColumnBeforeAnchor()
    {
        const int ax = 4, ay = 6, size = 2;
        var run = WorldCoordHelper.LeadingEdgeTiles(ax, ay, size, Direction.Left);
        for (int j = 0; j < size; j++)
            Assert.That(run[j], Is.EqualTo((ax - 1, ay + j)));
    }

    [Test]
    public void LeadingEdgeTiles_Down_IsRowPastBottomEdge()
    {
        const int ax = 4, ay = 6, size = 3;
        var run = WorldCoordHelper.LeadingEdgeTiles(ax, ay, size, Direction.Down);
        for (int i = 0; i < size; i++)
            Assert.That(run[i], Is.EqualTo((ax + i, ay + size)));
    }

    [Test]
    public void LeadingEdgeTiles_Up_IsRowAboveAnchor()
    {
        const int ax = 4, ay = 6, size = 2;
        var run = WorldCoordHelper.LeadingEdgeTiles(ax, ay, size, Direction.Up);
        for (int i = 0; i < size; i++)
            Assert.That(run[i], Is.EqualTo((ax + i, ay - 1)));
    }

    [Test]
    public void LeadingEdgeTiles_Size1_IsSingleFacedTile()
    {
        Assert.That(WorldCoordHelper.LeadingEdgeTiles(4, 6, 1, Direction.Right)[0], Is.EqualTo((5, 6)));
        Assert.That(WorldCoordHelper.LeadingEdgeTiles(4, 6, 1, Direction.Up)[0], Is.EqualTo((4, 5)));
    }

    [Test]
    public void TileRun_Contains_MatchesTiles()
    {
        var run = WorldCoordHelper.LeadingEdgeTiles(4, 6, 3, Direction.Right);
        for (int k = 0; k < run.Count; k++)
        {
            var (wx, wy) = run[k];
            Assert.That(run.Contains(wx, wy), Is.True);
        }
        Assert.That(run.Contains(4, 6), Is.False);          // the anchor itself is not on the strike strip
        Assert.That(run.Contains(4 + 3, 6 + 3), Is.False);  // one past the end of the strip
    }

    [Test]
    public void Footprint_StraddlesSeam_RightNeighborResolves()
    {
        // Center map = 1 at grid [1,1], right neighbor = 2. Anchor local (15,0) on center = world (31,12).
        var grid = new MapGrid(0, 0, 0, 0, 1, 2, 0, 0, 0, WorldCoordHelper.MapTilesX, WorldCoordHelper.MapTilesY);
        var (aWX, aWY) = grid.ToWorld(1, 1, 15, 0);
        Assert.That((aWX, aWY), Is.EqualTo((31, 12)));

        // Size-2 footprint {(31,12),(32,12)}: col 31 = center local 15; col 32 = right neighbor local 0.
        Assert.That(WorldCoordHelper.FootprintContains(aWX, aWY, 2, 32, 12), Is.True);
        Assert.That(grid.ResolveWorldTile(31, 12), Is.EqualTo((1, 15, 0)));
        Assert.That(grid.ResolveWorldTile(32, 12), Is.EqualTo((2, 0, 0)));
    }

    // ── Spell-circle range (footprint-aware, r = 5) ──────────────────────────

    [Test]
    public void IsInSpellRange_Size1_MatchesPointCheck_AtTheBoundary()
    {
        Assert.That(WorldCoordHelper.IsInSpellRange(0, 0, 5, 0), Is.True, "exactly r=5 away is in range");
        Assert.That(WorldCoordHelper.IsInSpellRange(0, 0, 6, 0), Is.False, "6 away is out");
        Assert.That(WorldCoordHelper.IsInSpellRange(0, 0, 1, 5, 0, 1),
            Is.EqualTo(WorldCoordHelper.IsInSpellRange(0, 0, 5, 0)), "the 6-arg with size 1/1 is the plain point check");
    }

    [Test]
    public void IsInSpellRange_OversizeTarget_NearEdgeInCircle_IsInRange()
    {
        // Caster (9,4); a size-3 NPC anchored (2,2) has footprint [2,4]x[2,4].  Its near corner (4,4) is exactly
        // r=5 away (IN), but its anchor (2,2) is ~7.3 away (OUT) - the fix targets the body, not the corner.
        Assert.That(WorldCoordHelper.IsInSpellRange(9, 4, 1, 2, 2, 3), Is.True, "the near edge is inside the circle");
        Assert.That(WorldCoordHelper.IsInSpellRange(9, 4, 2, 2), Is.False, "the anchor alone is out of the circle");
    }

    [Test]
    public void IsInSpellRange_IsSymmetric_ForMixedSizes()
    {
        // Two-way: swapping caster/target roles agrees, so an oversize NPC casting at a player is fair both ways.
        Assert.That(WorldCoordHelper.IsInSpellRange(9, 4, 1, 2, 2, 3),
            Is.EqualTo(WorldCoordHelper.IsInSpellRange(2, 2, 3, 9, 4, 1)));
    }

    [Test]
    public void IsInSpellRange_NpcVsNpc_BothOversize_UsesNearestEdges()
    {
        // Two size-3 footprints on the same rows: A [0,2], B [7,9] on X → nearest edges col 2↔7 → gap 5 = IN.
        Assert.That(WorldCoordHelper.IsInSpellRange(0, 0, 3, 7, 0, 3), Is.True);
        // Shift B one further (anchor 8 → near edge col 8) → gap 6 → OUT.
        Assert.That(WorldCoordHelper.IsInSpellRange(0, 0, 3, 8, 0, 3), Is.False);
    }

    // ── Edge-to-edge melee adjacency ──────────────────────────────────────────

    /// <summary>Size 1 on both sides has to stay exactly the anchor rule it generalizes, or the oversize fix
    /// quietly moves every ordinary mob's reach with it.</summary>
    [Test]
    public void AreFootprintsAdjacent_Size1_IsExactlyIsWorldAdjacent()
    {
        const int ax = 8, ay = 8;
        for (int dy = -3; dy <= 3; dy++)
        {
            for (int dx = -3; dx <= 3; dx++)
            {
                Assert.That(WorldCoordHelper.AreFootprintsAdjacent(ax, ay, 1, ax + dx, ay + dy, 1),
                    Is.EqualTo(WorldCoordHelper.IsWorldAdjacent(ax, ay, ax + dx, ay + dy)),
                    $"offset ({dx},{dy})");
            }
        }
    }

    /// <summary>Two size-3 bodies standing face to face are THREE tiles apart anchor to anchor — the distance an
    /// anchor-based gate reads as far away, which is why they could never reach each other.</summary>
    [Test]
    public void AreFootprintsAdjacent_Size3_ReachesOnlyWhereTheEdgesTouch()
    {
        const int ax = 4, ay = 6, size = 3;   // A spans x 4..6
        Assert.Multiple(() =>
        {
            Assert.That(WorldCoordHelper.AreFootprintsAdjacent(ax, ay, size, ax + 2, ay, size), Is.False,
                "anchors 2 apart means the bodies overlap — not a reach case at all");
            Assert.That(WorldCoordHelper.AreFootprintsAdjacent(ax, ay, size, ax + 3, ay, size), Is.True,
                "anchors 3 apart is edge to edge at size 3 — the touching case");
            Assert.That(WorldCoordHelper.AreFootprintsAdjacent(ax, ay, size, ax + 4, ay, size), Is.False,
                "anchors 4 apart leaves one clear tile between the bodies");
        });
    }

    [Test]
    public void AreFootprintsAdjacent_MixedSizes_ReadsTheSameFromEitherBody()
    {
        // A size-3 body at (4,6) spans x 4..6 / y 6..8; a size-1 body at (7,7) sits against its right edge.
        Assert.Multiple(() =>
        {
            Assert.That(WorldCoordHelper.AreFootprintsAdjacent(4, 6, 3, 7, 7, 1), Is.True);
            Assert.That(WorldCoordHelper.AreFootprintsAdjacent(7, 7, 1, 4, 6, 3), Is.True,
                "symmetric, so neither body can reach one the other cannot reach back");
        });
    }

    [Test]
    public void AreFootprintsAdjacent_DiagonalCornerContact_IsNotReach()
    {
        // A spans x 4..6 / y 6..8; B's top-left corner meets A's bottom-right corner and nothing else.
        Assert.That(WorldCoordHelper.AreFootprintsAdjacent(4, 6, 3, 7, 9, 3), Is.False,
            "melee is cardinal — corner contact is not reach");
    }

    // ── Measuring from the body rather than the anchor ────────────────────────
    // The anchor is bookkeeping — the top-left tile of a block that is all equally the NPC. Everything that
    // compares a distance against a threshold has to measure from the block, or the threshold means a
    // different thing on each side of it.

    /// <summary>Every tile touching a body faces the edge it touches, so the leading-edge strip covers it.</summary>
    [TestCase(2)]
    [TestCase(3)]
    public void FootprintFacingToward_EveryTouchingTile_LandsOnTheLeadingEdge(int size)
    {
        const int ax = 5, ay = 5;
        for (int i = 0; i < size; i++)
        {
            (int X, int Y)[] touching =
            [
                (ax + i, ay - 1),          // above column i
                (ax + i, ay + size),       // below column i
                (ax - 1, ay + i),          // left of row i
                (ax + size, ay + i),       // right of row i
            ];
            foreach (var (tx, ty) in touching)
            {
                var dir = WorldCoordHelper.FootprintFacingToward(ax, ay, size, tx, ty);
                Assert.That(WorldCoordHelper.LeadingEdgeTiles(ax, ay, size, dir).Contains(tx, ty), Is.True,
                    $"size {size}: ({tx},{ty}) touches the body but the facing points elsewhere");
            }
        }
    }

    [Test]
    public void FootprintFacingToward_DiagonalPastACorner_IsNotOnAnyEdge()
    {
        // Body 5..7 both axes. (8,8) is past the bottom-right corner — touching nothing, so whichever edge
        // it resolves to must not claim it. Melee is cardinal.
        var dir = WorldCoordHelper.FootprintFacingToward(5, 5, 3, 8, 8);
        Assert.That(WorldCoordHelper.LeadingEdgeTiles(5, 5, 3, dir).Contains(8, 8), Is.False);
    }

    [Test]
    public void FootprintManhattan_IsOneExactlyWhenAdjacent()
    {
        for (int aSize = 1; aSize <= 3; aSize++)
        {
            for (int bSize = 1; bSize <= 3; bSize++)
            {
                for (int bx = 0; bx <= 10; bx++)
                {
                    for (int by = 0; by <= 10; by++)
                    {
                        bool adjacent = WorldCoordHelper.AreFootprintsAdjacent(5, 5, aSize, bx, by, bSize);
                        int d = WorldCoordHelper.FootprintManhattan(5, 5, aSize, bx, by, bSize);
                        Assert.That(d == 1, Is.EqualTo(adjacent),
                            $"sizes {aSize}/{bSize} at ({bx},{by}): distance {d} vs adjacency {adjacent}");
                    }
                }
            }
        }
    }

    [Test]
    public void FootprintManhattan_IsSymmetric_AndSidesAgree()
    {
        // The lopsidedness this replaces: anchor-to-anchor, a body reads as further from something at its
        // bottom-right than from the same thing at its top-left.
        Assert.That(WorldCoordHelper.FootprintManhattan(5, 5, 3, 5, 2, 1),
            Is.EqualTo(WorldCoordHelper.FootprintManhattan(5, 5, 3, 5, 10, 1)),
            "three tiles above the body is the same distance as three tiles below it");
        Assert.That(WorldCoordHelper.FootprintManhattan(5, 5, 3, 9, 6, 2),
            Is.EqualTo(WorldCoordHelper.FootprintManhattan(9, 6, 2, 5, 5, 3)),
            "and reading it from either body gives the same number");
    }

    // 🔴 The safety property for the whole change: a one-tile NPC's body IS its anchor, so every helper here
    // must agree exactly with the anchor math it replaced. Nothing changes for the overwhelming majority of
    // the roster; only oversize NPCs move at all.
    [Test]
    public void AtSizeOne_EveryFootprintMeasureMatchesTheAnchorMath()
    {
        for (int bx = 0; bx <= 12; bx++)
        {
            for (int by = 0; by <= 12; by++)
            {
                if (bx == 5 && by == 5) continue;   // the body's own tile has no side to be beyond
                Assert.Multiple(() =>
                {
                    Assert.That(WorldCoordHelper.FootprintFacingToward(5, 5, 1, bx, by),
                        Is.EqualTo(WorldCoordHelper.WorldDirectionFrom(5, 5, bx, by)), $"facing at ({bx},{by})");
                    Assert.That(WorldCoordHelper.FootprintManhattan(5, 5, 1, bx, by, 1),
                        Is.EqualTo(WorldCoordHelper.WorldManhattan(5, 5, bx, by)), $"distance at ({bx},{by})");
                    Assert.That(WorldCoordHelper.FootprintOffsetTo(5, 5, 1, bx, by),
                        Is.EqualTo((bx - 5, by - 5)), $"offset at ({bx},{by})");
                    Assert.That(WorldCoordHelper.AreFootprintsWithin(5, 5, 1, bx, by, 1, 4),
                        Is.EqualTo(System.Math.Abs(bx - 5) <= 4 && System.Math.Abs(by - 5) <= 4), $"range box at ({bx},{by})");
                });
            }
        }
    }
}

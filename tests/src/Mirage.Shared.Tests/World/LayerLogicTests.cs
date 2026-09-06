using Mirage.Shared.Records;
using NUnit.Framework;
using System.Collections.Generic;

namespace Mirage.Shared.Tests.World;

// Truth table for the shared two-plane movement keystone (LayerLogic), independent of the server/client that
// wrap it.  Covers the BOUNDARY-based layer transition (ascend/descend fire only at a ramp block's ground edge,
// sticky inside — so a deep ramp behaves the same up and down), the ramp corridor + depth-fit gates in CanEnter,
// and per-layer attribute reads.  All world-tile space, so a small hand-built grid view suffices.
[TestFixture]
public class LayerLogicTests
{
    // Minimal IWorldTileView: sparse grid keyed by (x,y).  Unset cells read as a plain walkable tile, so both
    // planes are walkable-by-default (the uniform model) — the open ground/fringe surrounding the ramps.
    private sealed class GridView : LayerLogic.IWorldTileView
    {
        private static readonly TileRecord Plain = new();
        private readonly Dictionary<(int, int), TileRecord> _tiles = new();

        public TileRecord? At(int x, int y) => _tiles.TryGetValue((x, y), out var t) ? t : Plain;

        public GridView Ramp(int x, int y, Direction groundSide)
        {
            _tiles[(x, y)] = new TileRecord { FringeAttr = new FringeAttr { Type = TileType.LayerRamp, RampGroundSide = groundSide } };
            return this;
        }

        public GridView Wall(int x, int y, WorldLayer layer)
        {
            _tiles[(x, y)] = layer == WorldLayer.Fringe
                ? new TileRecord { FringeAttr = new FringeAttr { Type = TileType.Blocked } }
                : new TileRecord { Type = TileType.Blocked };
            return this;
        }
    }

    private static WorldLayer Resolve(GridView v, int x, int y, WorldLayer from, Direction dir) =>
        LayerLogic.ResolveLayer(v, x, y, size: 1, from, dir);

    private static bool CanEnter(GridView v, int x, int y, WorldLayer from, Direction dir, int size = 1) =>
        LayerLogic.CanEnter(v, x, y, size, from, dir, out _);

    // ── AttrFor: the uniform fringe plane is walkable by default ────────────────
    [Test]
    public void AttrFor_ReadsGroundInline_AndFringeSubRecord_WalkableByDefault()
    {
        var wallGround = new TileRecord { Type = TileType.Blocked };
        var wallFringe = new TileRecord { FringeAttr = new FringeAttr { Type = TileType.Blocked } };

        Assert.Multiple(() =>
        {
            Assert.That(LayerLogic.AttrFor(wallGround, WorldLayer.Ground).Type, Is.EqualTo(TileType.Blocked));
            Assert.That(LayerLogic.AttrFor(wallGround, WorldLayer.Fringe).Type, Is.EqualTo(TileType.Walkable),
                "a ground wall does not block the fringe plane above it");
            Assert.That(LayerLogic.AttrFor(wallFringe, WorldLayer.Fringe).Type, Is.EqualTo(TileType.Blocked));
            Assert.That(LayerLogic.AttrFor(wallFringe, WorldLayer.Ground).Type, Is.EqualTo(TileType.Walkable),
                "a fringe wall does not block the ground plane beneath it");
        });
    }

    // ── Boundary transition on a simple 1-deep ramp ─────────────────────────────
    // A ramp at (5,3), ground side Down: foot at (5,4) = ground, top at (5,2) = fringe.
    [Test]
    public void OneDeepRamp_AscendsMountingFromGround_DescendsSteppingOffToGround()
    {
        var v = new GridView().Ramp(5, 3, Direction.Down);

        Assert.Multiple(() =>
        {
            // Mount from the ground foot (moving Up, up-ramp) → Fringe.
            Assert.That(Resolve(v, 5, 3, WorldLayer.Ground, Direction.Up), Is.EqualTo(WorldLayer.Fringe));
            // Step off the top (moving Up onto non-ramp) → still Fringe (you walked onto the high landing).
            Assert.That(Resolve(v, 5, 2, WorldLayer.Fringe, Direction.Up), Is.EqualTo(WorldLayer.Fringe));
            // Step off the foot (moving Down onto non-ramp ground) → Ground.
            Assert.That(Resolve(v, 5, 4, WorldLayer.Fringe, Direction.Down), Is.EqualTo(WorldLayer.Ground));
        });
    }

    // ── The deep-ramp fix: symmetric up/down, sticky across the whole span ───────
    // A 3-deep vertical ramp (5,1)(5,2)(5,3), ground side Down.  Foot (5,4)=ground, top (5,0)=fringe.
    [Test]
    public void DeepRamp_StaysOnFringeAcrossTheSpan_DescendingOnlyAtTheGroundEdge()
    {
        var v = new GridView()
            .Ramp(5, 1, Direction.Down).Ramp(5, 2, Direction.Down).Ramp(5, 3, Direction.Down);

        Assert.Multiple(() =>
        {
            // Climbing up: ascend at the foot, Fringe every interior step.
            Assert.That(Resolve(v, 5, 3, WorldLayer.Ground, Direction.Up), Is.EqualTo(WorldLayer.Fringe), "mount");
            Assert.That(Resolve(v, 5, 2, WorldLayer.Fringe, Direction.Up), Is.EqualTo(WorldLayer.Fringe), "interior up");
            Assert.That(Resolve(v, 5, 1, WorldLayer.Fringe, Direction.Up), Is.EqualTo(WorldLayer.Fringe), "interior up");
            Assert.That(Resolve(v, 5, 0, WorldLayer.Fringe, Direction.Up), Is.EqualTo(WorldLayer.Fringe), "onto top landing");

            // Coming down: a per-tile rule would drop you to Ground on the first interior step; the boundary
            // rule keeps you on the ramp surface (Fringe) until you actually step off the foot.
            Assert.That(Resolve(v, 5, 2, WorldLayer.Fringe, Direction.Down), Is.EqualTo(WorldLayer.Fringe), "interior down stays Fringe");
            Assert.That(Resolve(v, 5, 3, WorldLayer.Fringe, Direction.Down), Is.EqualTo(WorldLayer.Fringe), "interior down stays Fringe");
            Assert.That(Resolve(v, 5, 4, WorldLayer.Fringe, Direction.Down), Is.EqualTo(WorldLayer.Ground), "descend only at the ground edge");
        });
    }

    // ── Interior of a MIXED-direction block is sticky (your [>][<] / staircase) ──
    [Test]
    public void MixedDirectionInterior_KeepsLayer_BothWays()
    {
        // (5,5) ground-side Left ('>'), (6,5) ground-side Right ('<') — high ends meet in the middle (a peak).
        var v = new GridView().Ramp(5, 5, Direction.Left).Ramp(6, 5, Direction.Right);

        Assert.Multiple(() =>
        {
            // Between the two ramp tiles, on either layer, the step keeps your layer (no mid-block flip).
            Assert.That(Resolve(v, 6, 5, WorldLayer.Fringe, Direction.Right), Is.EqualTo(WorldLayer.Fringe));
            Assert.That(Resolve(v, 5, 5, WorldLayer.Fringe, Direction.Left), Is.EqualTo(WorldLayer.Fringe));
            Assert.That(Resolve(v, 6, 5, WorldLayer.Ground, Direction.Right), Is.EqualTo(WorldLayer.Ground));
            // Both are legal steps (no corridor gate between two ramps).
            Assert.That(CanEnter(v, 6, 5, WorldLayer.Fringe, Direction.Right), Is.True);
            Assert.That(CanEnter(v, 5, 5, WorldLayer.Fringe, Direction.Left), Is.True);
        });
    }

    // ── A ramp is a SOLID connector: blocked underneath, walkable only on its Fringe surface (Q2) ──
    // You can't walk under/through a ramp; you mount it from the ground foot, which ascends you onto the Fringe.
    // The movement gate (MovementSystem.CanPlayerWalkOnTile / IsNpcTileFree) refuses a step whose AttrFor at the
    // resulting layer is Blocked — so a ramp read on the Ground layer must be Blocked.
    [Test]
    public void Ramp_IsSolidOnTheGroundPlane_WalkableOnlyOnFringe()
    {
        var ramp = new TileRecord { FringeAttr = new FringeAttr { Type = TileType.LayerRamp, RampGroundSide = Direction.Down } };

        Assert.Multiple(() =>
        {
            Assert.That(LayerLogic.AttrFor(ramp, WorldLayer.Ground).Type, Is.EqualTo(TileType.Blocked),
                "solid understructure — you can't walk under a ramp on the ground plane");
            Assert.That(LayerLogic.AttrFor(ramp, WorldLayer.Fringe).Type, Is.EqualTo(TileType.LayerRamp),
                "its ramp surface is walkable on the fringe");
        });
    }

    // The "walk under the bridge through a ramp" bug: stepping toward a ramp from the wrong side/direction keeps
    // you on Ground (no ascend), where the ramp reads Blocked → the gate refuses it.  Only mounting correctly
    // (from the ground foot, up-ramp) ascends you to the Fringe (where the ramp is walkable).
    [Test]
    public void SteppingOntoARampWithoutAMount_StaysGround_WhereTheRampIsBlocked()
    {
        // '^' at (5,5): ground side Down (mounts from below).
        var v = new GridView().Ramp(5, 5, Direction.Down);
        var rampTile = v.At(5, 5)!.Value;

        Assert.Multiple(() =>
        {
            // Down (from above, walking "under") and Right (perpendicular) do NOT ascend → stay Ground...
            Assert.That(Resolve(v, 5, 5, WorldLayer.Ground, Direction.Down), Is.EqualTo(WorldLayer.Ground), "walking under: no ascend");
            Assert.That(Resolve(v, 5, 5, WorldLayer.Ground, Direction.Right), Is.EqualTo(WorldLayer.Ground), "perpendicular: no ascend");
            // ...and on Ground the ramp is Blocked, so the movement gate refuses the step.
            Assert.That(LayerLogic.AttrFor(rampTile, WorldLayer.Ground).Type, Is.EqualTo(TileType.Blocked));
            // The legit mount (up-ramp, from the ground foot) ascends → the ramp reads walkable on Fringe.
            Assert.That(Resolve(v, 5, 5, WorldLayer.Ground, Direction.Up), Is.EqualTo(WorldLayer.Fringe), "up-ramp mount ascends");
            Assert.That(LayerLogic.AttrFor(rampTile, WorldLayer.Fringe).Type, Is.EqualTo(TileType.LayerRamp));
        });
    }

    // ── The corridor gate: enter a ramp only along its mount axis (both planes) ──
    [Test]
    public void Corridor_BlocksPerpendicularMount_AllowsAlongAxisMount()
    {
        // '^' at (5,5): ground side Down → vertical mount axis {Up,Down}.
        var v = new GridView().Ramp(5, 5, Direction.Down);

        Assert.Multiple(() =>
        {
            // Perpendicular approach from a non-ramp tile (moving Right/Left onto its side) is blocked...
            Assert.That(CanEnter(v, 5, 5, WorldLayer.Ground, Direction.Right), Is.False, "perpendicular from the side");
            Assert.That(CanEnter(v, 5, 5, WorldLayer.Fringe, Direction.Left), Is.False, "perpendicular, fringe side too");
            // ...along the mount axis it is allowed (and mounting up-ramp ascends).
            Assert.That(CanEnter(v, 5, 5, WorldLayer.Ground, Direction.Up), Is.True, "along-axis mount");
            Assert.That(Resolve(v, 5, 5, WorldLayer.Ground, Direction.Up), Is.EqualTo(WorldLayer.Fringe));
        });
    }

    // Between two ramps the corridor gate never fires — that is what lets you walk along a wide ramp / around a
    // multi-mount block (your [O][R][R][R][O] row and the staircase interior).
    [Test]
    public void Corridor_NeverBlocksBetweenTwoRampTiles()
    {
        // A horizontal row of Down-mounted ramps: moving Left/Right between them is perpendicular to their axis,
        // but both tiles are ramps, so it is allowed.
        var v = new GridView().Ramp(4, 6, Direction.Down).Ramp(5, 6, Direction.Down).Ramp(6, 6, Direction.Down);

        Assert.Multiple(() =>
        {
            Assert.That(CanEnter(v, 5, 6, WorldLayer.Fringe, Direction.Right), Is.True);
            Assert.That(CanEnter(v, 6, 6, WorldLayer.Fringe, Direction.Right), Is.True);
            // But escaping sideways onto the non-ramp gap IS blocked (ramp→non-ramp, perpendicular).
            Assert.That(CanEnter(v, 7, 6, WorldLayer.Fringe, Direction.Right), Is.False, "can't leave the row's side");
        });
    }

    // ── Depth fit-gate: a body only mounts a ramp whose block holds its whole footprint ──
    [Test]
    public void FitGate_Size2_NeedsA2x2RampSquareToMount()
    {
        // Narrow 1-wide, 3-deep Down ramp: a size-2 body mounting from the ground foot can't fit.
        var narrow = new GridView()
            .Ramp(5, 1, Direction.Down).Ramp(5, 2, Direction.Down).Ramp(5, 3, Direction.Down);
        // 2-wide, 3-deep Down ramp: a size-2 body fits (a 2x2 all-ramp square exists).
        var wide = new GridView()
            .Ramp(4, 1, Direction.Down).Ramp(5, 1, Direction.Down)
            .Ramp(4, 2, Direction.Down).Ramp(5, 2, Direction.Down)
            .Ramp(4, 3, Direction.Down).Ramp(5, 3, Direction.Down);

        Assert.Multiple(() =>
        {
            Assert.That(CanEnter(narrow, 5, 3, WorldLayer.Ground, Direction.Up, size: 2), Is.False, "1-wide can't hold size 2");
            Assert.That(CanEnter(wide, 4, 3, WorldLayer.Ground, Direction.Up, size: 2), Is.True, "2-wide holds size 2");
            // Size 1 always fits.
            Assert.That(CanEnter(narrow, 5, 3, WorldLayer.Ground, Direction.Up, size: 1), Is.True);
        });
    }

    // ── Multi-mount staircase: differently-directed ramps each mount from their own ground edge ──
    [Test]
    public void MultiMount_EachGroundEdgeAscends_AndConvergesOnFringe()
    {
        // Interior 2x2 ramp block with two mount directions:
        //   (1,1)='^' (2,1)='^'   ← mount from below
        //   (1,2)='>' (2,2)='^'   ← (1,2) mounts from the left
        var v = new GridView()
            .Ramp(1, 1, Direction.Down).Ramp(2, 1, Direction.Down)
            .Ramp(1, 2, Direction.Left).Ramp(2, 2, Direction.Down);

        Assert.Multiple(() =>
        {
            // Mount the '^' column from the ground below (moving Up).
            Assert.That(Resolve(v, 2, 2, WorldLayer.Ground, Direction.Up), Is.EqualTo(WorldLayer.Fringe), "bottom mount");
            // Mount the '>' from the ground to its left (moving Right).
            Assert.That(Resolve(v, 1, 2, WorldLayer.Ground, Direction.Right), Is.EqualTo(WorldLayer.Fringe), "left mount");
            // Once inside, moving between the two mount tiles keeps you on Fringe.
            Assert.That(Resolve(v, 2, 2, WorldLayer.Fringe, Direction.Right), Is.EqualTo(WorldLayer.Fringe), "interior converge");
        });
    }

    // ── Cross-layer LoS / melee connect (LayerConnects): same layer always; across layers only via a ramp ──
    private static bool Conn(GridView v, int ax, int ay, WorldLayer al, int bx, int by, WorldLayer bl) =>
        LayerLogic.LayerConnects(v, ax, ay, al, bx, by, bl);

    [Test]
    public void LayerConnects_SameLayerAlways_CrossLayerOnlyTowardARampsGroundSide()
    {
        // '^' ramp at (6,5), ground side DOWN: (6,6) is its ground foot (mount side); (6,4) is off its high/lift
        // end (behind it); (5,5) is to its side. (8,8) is a plain (non-ramp) tile.
        var v = new GridView().Ramp(6, 5, Direction.Down);

        Assert.Multiple(() =>
        {
            // Same layer connects regardless of distance — obstacle/adjacency rules are applied separately.
            Assert.That(Conn(v, 2, 2, WorldLayer.Ground, 9, 9, WorldLayer.Ground), Is.True, "ground↔ground");
            Assert.That(Conn(v, 2, 2, WorldLayer.Fringe, 9, 9, WorldLayer.Fringe), Is.True, "fringe↔fringe (a ramp person ↔ a bridge person are both Fringe)");

            // A person ON the ramp (Fringe at (6,5)) reaches the ground only TOWARD the ramp's ground (mount) side...
            Assert.That(Conn(v, 6, 5, WorldLayer.Fringe, 6, 6, WorldLayer.Ground), Is.True, "ramp ↔ ground at its foot (mount side)");
            Assert.That(Conn(v, 6, 5, WorldLayer.Fringe, 6, 9, WorldLayer.Ground), Is.True, "ramp ↔ distant ground down the mount axis (range-agnostic)");
            // ...and is reachable FROM a ground person on that side (order-independent).
            Assert.That(Conn(v, 6, 6, WorldLayer.Ground, 6, 5, WorldLayer.Fringe), Is.True, "ground at the foot ↔ ramp person");

            // But NOT off the ramp's high (lift) end, nor across its sides — you can't hit a ground target behind or
            // beside a ramp from on top of it (the reported "attack the ground behind the ramp" bug).
            Assert.That(Conn(v, 6, 5, WorldLayer.Fringe, 6, 4, WorldLayer.Ground), Is.False, "ramp ✗ ground off its lift/high end (behind)");
            Assert.That(Conn(v, 6, 5, WorldLayer.Fringe, 5, 5, WorldLayer.Ground), Is.False, "ramp ✗ ground to its side (perpendicular)");

            // A plain ground point and a plain fringe (non-ramp) point NEVER connect across the planes.
            Assert.That(Conn(v, 6, 6, WorldLayer.Ground, 8, 8, WorldLayer.Fringe), Is.False, "ground ↔ plain fringe deck");
            Assert.That(Conn(v, 2, 2, WorldLayer.Ground, 8, 8, WorldLayer.Fringe), Is.False, "distant ground ↔ plain fringe");
        });
    }
}

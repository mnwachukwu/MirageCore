using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Diagnostics;
using System.Reflection;

namespace Mirage.Server.Tests.Ai;

// Locks the per-pass shared BFS direction-field cache (CachedStepTowardObservableArea / FillPathField) to the
// single-source BFS it deduplicates: for EVERY possible source tile the cached decode must return exactly what
// FindStepTowardObservableArea returns for a chaser standing there.  This is the drift guard — the two methods'
// per-tile checks (occupied / unlinked / attack-slot ring w/ its visited side-effect / walkable) and
// the record-before-checks ordering must stay identical, or a chaser reading the shared field would step
// differently from one running its own BFS.  Both privates are reflected by name (harness per NpcChaseRoutingTests).
// A separate [Explicit] benchmark records the gang-dedup win.  Coords are 16x12 (x 0-15, y 0-11).
[TestFixture]
public class NpcPathCacheTests
{
    const int Map = 1;
    const int W = 16, H = 12;   // center-cell tile dims

    static readonly MethodInfo FindStepMethod =
        typeof(NpcAiSystem).GetMethod("FindStepTowardObservableArea", BindingFlags.NonPublic | BindingFlags.Instance)!;
    static readonly MethodInfo CachedStepMethod =
        typeof(NpcAiSystem).GetMethod("CachedStepTowardObservableArea", BindingFlags.NonPublic | BindingFlags.Instance)!;
    static readonly FieldInfo PathNowField =
        typeof(NpcAiSystem).GetField("_pathNow", BindingFlags.NonPublic | BindingFlags.Instance)!;
    static readonly FieldInfo PathFieldCacheField =
        typeof(NpcAiSystem).GetField("_pathFieldCache", BindingFlags.NonPublic | BindingFlags.Instance)!;
    static readonly FieldInfo PathFieldBuffersUsedField =
        typeof(NpcAiSystem).GetField("_pathFieldBuffersUsed", BindingFlags.NonPublic | BindingFlags.Instance)!;

    // Floods actually run this pass (a buffer is rented per field BUILT), and distinct keys held this pass.
    static int FieldsBuilt(NpcAiSystem ai) => (int)PathFieldBuffersUsedField.GetValue(ai)!;
    static int CacheEntries(NpcAiSystem ai) => ((System.Collections.ICollection)PathFieldCacheField.GetValue(ai)!).Count;
    static readonly MethodInfo VacatesRampMethod =
        typeof(NpcAiSystem).GetMethod("ChaserVacatesRampFor", BindingFlags.NonPublic | BindingFlags.Instance)!;
    static bool VacatesRamp(NpcAiSystem ai, MapNpcRecord mn, WorldLayer targetLayer)
        => (bool)VacatesRampMethod.Invoke(ai, new object[] { Map, mn, targetLayer })!;

    // The blind single-source BFS: (mapNum, fromX, fromY, fromLayer, targetMap, toX, toY, targetLayer, npc, planAroundActors=false, 0, 0).
    static Direction? SingleSource(NpcAiSystem ai, NpcRecord npc, int fromX, int fromY, int toX, int toY,
                                   WorldLayer fromLayer = WorldLayer.Ground, WorldLayer targetLayer = WorldLayer.Ground)
        => (Direction?)FindStepMethod.Invoke(ai, new object[] { Map, fromX, fromY, fromLayer, Map, toX, toY, targetLayer, npc, false, 0, 0, 1 });

    // The cached decode: (mapNum, fromX, fromY, fromLayer, targetMap, toX, toY, targetLayer, npc).
    static Direction? Cached(NpcAiSystem ai, NpcRecord npc, int fromX, int fromY, int toX, int toY,
                             WorldLayer fromLayer = WorldLayer.Ground, WorldLayer targetLayer = WorldLayer.Ground)
        => (Direction?)CachedStepMethod.Invoke(ai, new object[] { Map, fromX, fromY, fromLayer, Map, toX, toY, targetLayer, npc, 1 });

    // ── Differential equivalence (the drift lock) ─────────────────────────────

    [Test]
    public void CachedField_MatchesSingleSource_OverEverySourceTile()
    {
        // One world geometry, exercised for three NPC profiles that hit different BFS branches:
        //  - AttackOnSight size-1 : NpcAvoid is a wall, single-tile footprint;
        //  - Guard        size-1 : NpcAvoid is walkable (ignoreNpcAvoid) — a different reachable region;
        //  - AttackOnSight size-2 : the footprint (FootprintBlockWalkable) branch.
        // Two targets vary the expansion root (root-handling: invariants 2 & 3).  A live NPC sits on the ring
        // of the first target so the attack-slot mask + its visited side-effect are on the hot path.
        var (ai, world) = NewWorldWithGeometry();

        foreach (var (behavior, size) in new[]
        {
            (NpcBehavior.AttackOnSight, 1),
            (NpcBehavior.Guard,         1),
            (NpcBehavior.AttackOnSight, 2),
        })
        {
            world.Npcs[1].Behavior = behavior;
            world.Npcs[1].Size = size;
            var npc = world.Npcs[1];

            foreach (var (toX, toY) in new[] { (8, 3), (4, 8) })
                AssertEquivalentOverGrid(ai, npc, toX, toY);
        }
    }

    static void AssertEquivalentOverGrid(NpcAiSystem ai, NpcRecord npc, int toX, int toY)
    {
        for (int sy = 0; sy < H; sy++)
        {
            for (int sx = 0; sx < W; sx++)
            {
                var single = SingleSource(ai, npc, sx, sy, toX, toY);
                var cached = Cached(ai, npc, sx, sy, toX, toY);
                Assert.That(cached, Is.EqualTo(single),
                    $"cached step != single-source step at source ({sx},{sy}) -> target ({toX},{toY}); npc size={npc.EffectiveSize}, behavior={npc.Behavior}");
            }
        }
    }

    // ── Layered (bridge) differential equivalence ─────────────────────────────

    // The layered BFS carries a (cell, WorldLayer) state, so the two flood bodies must stay mirrored across
    // ramp ascend/descend edges and the fringe-vs-ground state split too — not just on flat ground.  Same drift
    // lock over a bridge geometry: for every source (cell, fromLayer) and both target layers the cached decode
    // must equal the single-source BFS.  size=2 also exercises the fringe-fit gate (the 1-tall strip can't hold
    // a 2x2 body, so the fringe is unreachable — both bodies must agree that it is).
    [Test]
    public void CachedField_MatchesSingleSource_OverBothLayers_OnABridge()
    {
        var (ai, world) = NewWorldWithBridge();

        foreach (int size in new[] { 1, 2 })
        {
            world.Npcs[1].Size = size;
            var npc = world.Npcs[1];

            foreach (var (toX, toY, targetLayer) in new[]
            {
                (7, 6, WorldLayer.Fringe),   // on the bridge deck
                (7, 6, WorldLayer.Ground),   // directly under the deck
                (2, 6, WorldLayer.Ground),   // off to the side, same row
            })
            {
                foreach (var fromLayer in new[] { WorldLayer.Ground, WorldLayer.Fringe })
                {
                    for (int sy = 0; sy < H; sy++)
                    {
                        for (int sx = 0; sx < W; sx++)
                        {
                            var single = SingleSource(ai, npc, sx, sy, toX, toY, fromLayer, targetLayer);
                            var cached = Cached(ai, npc, sx, sy, toX, toY, fromLayer, targetLayer);
                            Assert.That(cached, Is.EqualTo(single),
                                $"cached != single at source ({sx},{sy},{fromLayer}) -> target ({toX},{toY},{targetLayer}); npc size={npc.EffectiveSize}");
                        }
                    }
                }
            }
        }
    }

    // The reported "an NPC won't follow me UP a ramp" case, asserted CONCRETELY (not just cached==single): a
    // ground chaser at a ramp's foot, targeting a player up on the bridge deck, must step ONTO the ramp (mount),
    // and a chaser already climbing must keep closing across the deck.  A null (no path) or non-mount step here is
    // exactly that follow-up-the-ramp bug.  Bridge geometry: deck Fringe x=5..9 y=6; west ramp (4,6) mounts from
    // its Left foot (3,6) by stepping Right; east ramp (10,6) from its Right foot (11,6) by stepping Left.
    [Test]
    public void GroundChaser_AtARampFoot_StepsUpTowardAFringeDeckTarget()
    {
        var (ai, world) = NewWorldWithBridge();
        var npc = world.Npcs[1];   // size 1

        Assert.Multiple(() =>
        {
            Assert.That(SingleSource(ai, npc, 3, 6, 7, 6, WorldLayer.Ground, WorldLayer.Fringe), Is.EqualTo(Direction.Right),
                "ground chaser at the west ramp foot (3,6) mounts RIGHT toward a deck target");
            Assert.That(SingleSource(ai, npc, 11, 6, 7, 6, WorldLayer.Ground, WorldLayer.Fringe), Is.EqualTo(Direction.Left),
                "ground chaser at the east ramp foot (11,6) mounts LEFT toward a deck target");
            // On the ramp already (Fringe): keep going onto the deck, then close along it.
            Assert.That(SingleSource(ai, npc, 4, 6, 7, 6, WorldLayer.Fringe, WorldLayer.Fringe), Is.EqualTo(Direction.Right),
                "chaser on the west ramp steps onto the deck");
            Assert.That(SingleSource(ai, npc, 5, 6, 7, 6, WorldLayer.Fringe, WorldLayer.Fringe), Is.EqualTo(Direction.Right),
                "chaser on the deck closes toward the target");
            // Approaching the foot from further out on the ground still heads for the ramp foot.
            Assert.That(SingleSource(ai, npc, 2, 6, 7, 6, WorldLayer.Ground, WorldLayer.Fringe), Is.EqualTo(Direction.Right),
                "ground chaser two tiles out still routes toward the ramp foot");
        });
    }

    // The "reached ramp, then froze" chokepoint fix: a chaser standing ON a ramp with its target on the SAME
    // layer (up on the deck the ramp leads to) must vacate the 1-wide mount so a second chaser can climb behind
    // it — so it falls through to a STEP instead of camping. It still holds for a cross-layer (ground foot)
    // target, and on a plain deck tile it is never a chokepoint.
    [Test]
    public void ChaserOnARamp_VacatesForASameLayerTarget_ButHoldsOtherwise()
    {
        var (ai, world) = NewWorldWithBridge();
        var mn = world.MapNpcs[Map, 1];
        mn.Num = 1;
        mn.Hp = 100;

        Assert.Multiple(() =>
        {
            // Standing on the west ramp (4,6), on the Fringe.
            mn.X = 4;
            mn.Y = 6;
            mn.Layer = WorldLayer.Fringe;
            Assert.That(VacatesRamp(ai, mn, WorldLayer.Fringe), Is.True, "on a ramp, same-layer deck target → vacate the mount");
            Assert.That(VacatesRamp(ai, mn, WorldLayer.Ground), Is.False, "on a ramp, cross-layer ground target → hold (foot reach)");

            // Standing on a plain deck tile (5,6) — not a ramp, so never a chokepoint: always hold and attack.
            mn.X = 5;
            Assert.That(VacatesRamp(ai, mn, WorldLayer.Fringe), Is.False, "on the deck (not a ramp) → hold and attack");
        });
    }

    // Map 6's real bug: the ramp is at the RIGHT EDGE (15,9) and its deck continues across the right seam onto the
    // neighbor map — a CROSS-SEAM horizontal ramp. A ground chaser at the foot must still compute the mount step
    // toward a target on the neighbor's deck. (Up/down ramps that stay within one map already worked.)
    [Test]
    public void GroundChaser_FollowsUpACrossSeamRamp_ToADeckOnTheNeighborMap()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        world.Npcs[1].Behavior = NpcBehavior.AttackOnSight;
        world.Npcs[1].Size = 1;
        world.Maps[Map].Right = 2;      // link Map 1 → Map 2 at the right seam
        world.Maps[2].Left = Map;
        // Ramp at Map 1's right edge (15,6), ground side Left (mount from (14,6) by stepping Right); deck continues
        // onto Map 2 at (0,6). Mirrors map6.json (ramp (15,9) data1=Left).
        world.Maps[Map].EditTile(15, 6, t => t with { FringeAttr = new FringeAttr { Type = TileType.LayerRamp, RampGroundSide = Direction.Left } });
        world.Maps[2].EditTile(0, 6, t => t with { FringeAttr = new FringeAttr { Type = TileType.Walkable } });

        var ai = new NpcAiSystem(world, pm, null!, null!, null!, null!, null!, null!);
        var npc = world.Npcs[1];

        // NPC at the ramp foot (14,6) on Map 1's ground; target = a player on Map 2's deck (0,6), Fringe.
        var step = (Direction?)FindStepMethod.Invoke(ai, new object[]
            { Map, 14, 6, WorldLayer.Ground, 2, 0, 6, WorldLayer.Fringe, npc, false, 0, 0, 1 });
        Assert.That(step, Is.EqualTo(Direction.Right),
            "ground chaser at a cross-seam ramp foot must mount toward a deck on the neighbor map");
    }

    // ── Cache sharing: one flood per (target, footprint, behavior) per pass ───

    // A realistic converging gang — several chasers closing on ONE player from different corners of the
    // observable area — must share ONE flood for the whole pass, because the field is source-agnostic: nothing
    // about WHICH chaser reads it is part of PathFieldKey.  The cache is per-pass (cleared when _pathNow moves),
    // so every chaser here reads within a single stamp, as the real legs pass does.  Asserting on floods BUILT
    // rather than on timing keeps this a hard behavioral lock, not a heuristic.
    //
    // Source-agnosticism is structural — PathFieldKey simply has nowhere to put a per-chaser origin — so
    // there is nothing there for a test to probe.  What needs locking is that the SHARING actually happens
    // and that sharing does not change anyone's step.
    [Test]
    public void GangConvergingOnOneTarget_SharesOneFieldPerPass()
    {
        var (ai, world) = NewWorldWithGeometry();
        var npc = world.Npcs[1];
        npc.Behavior = NpcBehavior.AttackOnSight;
        npc.Size = 1;
        const int toX = 8, toY = 3;

        // Six chasers boxing the target in — one flood between them, not six.
        var gang = new (int x, int y)[] { (2, 2), (14, 2), (2, 10), (14, 10), (8, 0), (0, 6) };

        SetPathNow(ai, 5_000);
        var steps = new Direction?[gang.Length];
        for (int i = 0; i < gang.Length; i++)
            steps[i] = Cached(ai, npc, gang[i].x, gang[i].y, toX, toY);

        int built = FieldsBuilt(ai);
        int entries = CacheEntries(ai);

        // Control for the same gang, varying a component that IS keyed (the target cell).  This is what makes
        // the assert above a MEASUREMENT rather than a reading off a stuck counter: the identical gang shape
        // yields one flood per distinct key value here, so 1-vs-6 is a real difference.  (Footprint would NOT
        // work as the control — EffectiveSize clamps to MaxNpcSize, so six sizes collapse to three keys.)
        var targets = new (int x, int y)[] { (8, 3), (4, 8), (2, 2), (12, 9), (6, 1), (10, 10) };
        SetPathNow(ai, 5_001);
        for (int i = 0; i < gang.Length; i++)
            Cached(ai, npc, gang[i].x, gang[i].y, targets[i].x, targets[i].y);
        int builtWhenKeyedComponentVaries = FieldsBuilt(ai);

        Assert.Multiple(() =>
        {
            Assert.That(built, Is.EqualTo(1),
                $"{gang.Length} chasers on one target share one behavior/footprint, so they must share ONE flood "
                + "per pass regardless of where each one stands");
            Assert.That(entries, Is.EqualTo(1),
                "one center map + one target + one footprint + one behavior => exactly one cache key");
            Assert.That(builtWhenKeyedComponentVaries, Is.EqualTo(gang.Length),
                "control: varying a keyed component over the same gang must give one flood EACH — so the "
                + "single flood above is genuine sharing, not a counter that never moves");
            // ...and sharing must not have changed anyone's step: each still gets what its own BFS would return.
            for (int i = 0; i < gang.Length; i++)
            {
                Assert.That(steps[i], Is.EqualTo(SingleSource(ai, npc, gang[i].x, gang[i].y, toX, toY)),
                    $"chaser {i} at ({gang[i].x},{gang[i].y}) read a step its own BFS disagrees with");
            }
        });
    }

    // The flip side of the sharing test: the key must not be too COARSE.  Everything that genuinely changes the
    // field — target cell, footprint, behavior (which folds in ignoreNpcAvoid) — must still get its own flood
    // within one pass, and returning to an earlier profile must hit rather than rebuild.
    [Test]
    public void FieldCache_StillSplitsOnWhatActuallyChangesTheField()
    {
        var (ai, world) = NewWorldWithGeometry();
        var npc = world.Npcs[1];
        npc.Behavior = NpcBehavior.AttackOnSight;
        npc.Size = 1;

        SetPathNow(ai, 6_000);
        Cached(ai, npc, 2, 2, 8, 3);
        Assert.That(FieldsBuilt(ai), Is.EqualTo(1), "first chaser builds the field");

        Cached(ai, npc, 2, 2, 4, 8);
        Assert.That(FieldsBuilt(ai), Is.EqualTo(2), "a different target roots a different flood");

        npc.Behavior = NpcBehavior.Guard;            // ignoreNpcAvoid flips => different walkable region
        Cached(ai, npc, 2, 2, 8, 3);
        Assert.That(FieldsBuilt(ai), Is.EqualTo(3), "behavior changes walkability => its own flood");

        npc.Behavior = NpcBehavior.AttackOnSight;
        npc.Size = 2;                                // footprint changes what fits
        Cached(ai, npc, 2, 2, 8, 3);
        Assert.That(FieldsBuilt(ai), Is.EqualTo(4), "footprint changes fit => its own flood");

        npc.Size = 1;                                // back to the very first profile, from a new source tile
        Cached(ai, npc, 9, 9, 8, 3);
        Assert.That(FieldsBuilt(ai), Is.EqualTo(4),
                    "same behavior+footprint+target reuses the first flood — the field is source-agnostic");
    }

    // ── Benchmark: gang share beats per-chaser (Explicit; run manually) ────────

    [Explicit, Category("Benchmark")]
    [Test]
    public void Benchmark_GangShareBeatsPerChaser()
    {
        // NOTE: both paths pay the same per-call reflection Invoke tax, so the RELATIVE numbers are meaningful
        // (the difference is BFS work, not reflection).  Open map => each BFS is a full 48x36 flood.
        var (ai, world) = NewWorldWithGeometry(open: true);
        world.Npcs[1].Behavior = NpcBehavior.AttackOnSight;
        world.Npcs[1].Size = 1;
        var npc = world.Npcs[1];
        int toX = 8, toY = 6;
        var gang = new (int x, int y)[] { (2, 2), (14, 2), (2, 10), (14, 10), (8, 0), (0, 6), (15, 6), (8, 11) };
        var solo = gang[..1];

        // Warm up (JIT + first field build) so timings measure steady state.
        for (int i = 0; i < 50; i++) { SetPathNow(ai, 1000 + i); foreach (var s in gang) { Cached(ai, npc, s.x, s.y, toX, toY); SingleSource(ai, npc, s.x, s.y, toX, toY); } }

        const int Passes = 5000;
        double cached8 = TimePasses(ai, npc, gang, toX, toY, Passes, useCache: true);
        double single8 = TimePasses(ai, npc, gang, toX, toY, Passes, useCache: false);
        double cached1 = TimePasses(ai, npc, solo, toX, toY, Passes, useCache: true);
        double single1 = TimePasses(ai, npc, solo, toX, toY, Passes, useCache: false);

        TestContext.WriteLine($"8 chasers/pass x{Passes}: cached={cached8:F1}ms  single-source={single8:F1}ms  (speedup {single8 / cached8:F2}x)");
        TestContext.WriteLine($"1 chaser /pass x{Passes}: cached={cached1:F1}ms  single-source={single1:F1}ms  (ratio {cached1 / single1:F2}x — full flood vs early-terminate)");
        Assert.That(cached8, Is.LessThan(single8),
            "8-chaser gang: one shared flood + 8 O(1) decodes must beat 8 single-source floods");
    }

    static double TimePasses(NpcAiSystem ai, NpcRecord npc, (int x, int y)[] sources, int toX, int toY, int passes, bool useCache)
    {
        var sw = Stopwatch.StartNew();
        for (int p = 0; p < passes; p++)
        {
            SetPathNow(ai, 2_000_000 + p);   // advance the pass stamp so the cache rebuilds each pass (realistic)
            foreach (var s in sources)
            {
                if (useCache) Cached(ai, npc, s.x, s.y, toX, toY);
                else SingleSource(ai, npc, s.x, s.y, toX, toY);
            }
        }
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds;
    }

    static void SetPathNow(NpcAiSystem ai, long v) => PathNowField.SetValue(ai, v);

    // ── Harness ───────────────────────────────────────────────────────────────

    // Center map with a horizontal wall (open ends), an NpcAvoid ring tile, and a live NPC on the (8,3) ring
    // so both the wall-routing and the attack-slot mask are exercised.  `open:true` gives a bare walkable map
    // (no walls/blocker) for the benchmark.  FindStep/CachedStep read only _world/_pm, so the other subsystems
    // can be null (as in NpcChaseRoutingTests).
    static (NpcAiSystem ai, GameWorld world) NewWorldWithGeometry(bool open = false)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        world.Npcs[1].Behavior = NpcBehavior.AttackOnSight;   // chaser template (mutated per profile)
        world.Npcs[2].Behavior = NpcBehavior.Stationary;      // blocker template

        if (!open)
        {
            foreach (int x in new[] { 5, 6, 7, 8, 9 })         // horizontal wall below (8,3), open at the ends
                world.Maps[Map].EditTile(x, 5, t => t with { Type = TileType.Blocked });
            world.Maps[Map].EditTile(7, 3, t => t with { Type = TileType.NpcAvoid });   // ring tile: wall for AoS, walkable for guards

            var blocker = world.MapNpcs[Map, 1];
            blocker.Num = 2;
            blocker.X = 8;
            blocker.Y = 4;
            blocker.Hp = 100;  // on the (8,3) ring
        }

        var ai = new NpcAiSystem(world, pm, null!, null!, null!, null!, null!, null!);
        return (ai, world);
    }

    // Center map with a horizontal fringe "bridge" at y=6: a walkable fringe surface spanning x=5..9, capped by
    // a ramp at each end (west ramp mounts from the Left/ground side, east ramp from the Right), with plain
    // walkable ground under the whole thing.  Exercises the layered BFS's ramp ascend/descend edges plus the
    // fringe/ground state split in both flood bodies.
    static (NpcAiSystem ai, GameWorld world) NewWorldWithBridge()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        world.Npcs[1].Behavior = NpcBehavior.AttackOnSight;
        world.Npcs[1].Size = 1;

        for (int x = 5; x <= 9; x++)
            world.Maps[Map].EditTile(x, 6, t => t with { FringeAttr = new FringeAttr { Type = TileType.Walkable } });
        world.Maps[Map].EditTile(4, 6, t => t with { FringeAttr = new FringeAttr { Type = TileType.LayerRamp, RampGroundSide = Direction.Left } });
        world.Maps[Map].EditTile(10, 6, t => t with { FringeAttr = new FringeAttr { Type = TileType.LayerRamp, RampGroundSide = Direction.Right } });

        var ai = new NpcAiSystem(world, pm, null!, null!, null!, null!, null!, null!);
        return (ai, world);
    }
}

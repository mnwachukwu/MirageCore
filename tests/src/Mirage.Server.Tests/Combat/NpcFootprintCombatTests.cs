using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Tests.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Collections.Generic;

namespace Mirage.Server.Tests.Combat;

/// <summary>Server integration coverage for variable-size NPCs: footprint occupancy (within-map and across a
/// seam) and the large-NPC melee strip (one swing strikes every player on the leading edge), plus the size-1
/// regression guards (single-tile occupancy, omnidirectional adjacency).</summary>
[TestFixture]
public class NpcFootprintCombatTests
{
    const int Map = 1;
    const int StartHp = 9999;
    // These pin REACH, not cadence. Every NPC here starts with AttackTimer at 0, so any live clock reading
    // clears the cooldown and the gate answers the question the test is asking.
    static long Now => Environment.TickCount64;

    // ── Footprint occupancy ───────────────────────────────────────────────────
    [Test]
    public void Size2Npc_OccupiesItsWholeFootprint()
    {
        var world = new GameWorld();
        world.Npcs[1].Size = 2;
        var n = world.MapNpcs[Map, 1];
        n.Num = 1;
        n.X = 5;
        n.Y = 5;

        foreach (var (x, y) in new[] { (5, 5), (6, 5), (5, 6), (6, 6) })
            Assert.That(world.IsTileOccupiedByNpc(Map, x, y), Is.True, $"footprint tile ({x},{y})");
        foreach (var (x, y) in new[] { (4, 5), (7, 5), (5, 4), (5, 7) })
            Assert.That(world.IsTileOccupiedByNpc(Map, x, y), Is.False, $"tile just outside ({x},{y})");
    }

    [Test]
    public void Size1Npc_OccupiesOnlyItsTile()
    {
        var world = new GameWorld();
        world.Npcs[1].Size = 1;
        var n = world.MapNpcs[Map, 1];
        n.Num = 1;
        n.X = 5;
        n.Y = 5;
        Assert.That(world.IsTileOccupiedByNpc(Map, 5, 5), Is.True);
        Assert.That(world.IsTileOccupiedByNpc(Map, 6, 5), Is.False);
        Assert.That(world.IsTileOccupiedByNpc(Map, 5, 6), Is.False);
    }

    [Test]
    public void Size2Npc_AtRightEdge_SpillsOntoRightNeighbor()
    {
        var world = new GameWorld();
        world.Maps[1].Right = 2;
        world.Maps[2].Left = 1;
        world.Npcs[1].Size = 2;
        var n = world.MapNpcs[1, 1];
        n.Num = 1;
        n.X = Constants.MaxMapX;
        n.Y = 5;  // footprint {15,16}x{5,6}

        // On the home map the on-map column (15) is occupied.
        Assert.That(world.IsTileOccupiedByNpc(1, Constants.MaxMapX, 5), Is.True);
        // The spilled column resolves onto the right neighbor's local col 0, rows 5 and 6.
        Assert.That(world.IsTileOccupiedByNpc(2, 0, 5), Is.True, "spilled body blocks on the neighbor map");
        Assert.That(world.IsTileOccupiedByNpc(2, 0, 6), Is.True);
        Assert.That(world.IsTileOccupiedByNpc(2, 0, 4), Is.False, "outside the spilled rows");
        Assert.That(world.IsTileOccupiedByNpc(2, 1, 5), Is.False, "the body only spills one column");
    }

    [Test]
    public void Size2Guest_OccupiesItsWholeFootprint()
    {
        // A chasing NPC that crossed a seam lives in MapTraversalNpcs; its footprint must block just like a native's.
        var world = new GameWorld();
        world.Npcs[1].Size = 2;
        world.MapTraversalNpcs[Map].Add(new TraversalNpcRecord
        {
            Num = 1, CurrentMapNum = Map, X = 5, Y = 5, SpawnMapNum = Map, SpawnSlot = 1,
        });
        foreach (var (x, y) in new[] { (5, 5), (6, 5), (5, 6), (6, 6) })
            Assert.That(world.IsTileOccupiedByNpc(Map, x, y), Is.True, $"guest footprint tile ({x},{y})");
        Assert.That(world.IsTileOccupiedByNpc(Map, 7, 5), Is.False, "just outside the guest footprint");
    }

    // ── Large-NPC melee strip ─────────────────────────────────────────────────
    [Test]
    public void Size3Npc_FacingRight_CanAttackAnyTileOnLeadingEdge()
    {
        var (combat, world, pm) = NewCombat();
        world.Npcs[1].Size = 3;
        world.Npcs[1].Str = 50;
        world.Npcs[1].Behavior = NpcBehavior.AttackOnSight;
        var n = PlaceNpc(world, 1, 1, 5, 5);
        n.Dir = Direction.Right;  // footprint {5,6,7}; right edge = col 8, rows {5,6,7}

        RegisterPlayer(world, pm, 10, 8, 5);
        RegisterPlayer(world, pm, 11, 8, 6);
        RegisterPlayer(world, pm, 12, 8, 7);
        RegisterPlayer(world, pm, 13, 8, 8);    // one row past the strip
        RegisterPlayer(world, pm, 14, 10, 5);   // two tiles from the footprint

        Assert.That(combat.CanNpcAttackPlayer(Map, 1, 10, Environment.TickCount64), Is.True, "strike tile (8,5)");
        Assert.That(combat.CanNpcAttackPlayer(Map, 1, 11, Environment.TickCount64), Is.True, "strike tile (8,6)");
        Assert.That(combat.CanNpcAttackPlayer(Map, 1, 12, Environment.TickCount64), Is.True, "strike tile (8,7)");
        Assert.That(combat.CanNpcAttackPlayer(Map, 1, 13, Environment.TickCount64), Is.False, "off the leading-edge strip");
        Assert.That(combat.CanNpcAttackPlayer(Map, 1, 14, Environment.TickCount64), Is.False, "two tiles from the footprint");
    }

    [Test]
    public void Size3Npc_OneSwing_StrikesEveryPlayerOnLeadingEdge()
    {
        var (combat, world, pm) = NewCombat();
        world.Npcs[1].Size = 3;
        world.Npcs[1].Str = 60;
        world.Npcs[1].Behavior = NpcBehavior.AttackOnSight;
        var n = PlaceNpc(world, 1, 1, 5, 5);
        n.Dir = Direction.Right;
        n.AttackTimer = 0;

        var onStrip = new[]
        {
            RegisterPlayer(world, pm, 10, 8, 5),
            RegisterPlayer(world, pm, 11, 8, 6),
            RegisterPlayer(world, pm, 12, 8, 7),
        };
        var offStrip = RegisterPlayer(world, pm, 13, 8, 8);   // one row past the strip

        combat.NpcAttackPlayer(Map, 1, 10, Environment.TickCount64);   // primary target = the player at (8,5)

        foreach (var pc in onStrip)
            Assert.That(pc.Hp, Is.LessThan(StartHp), "every player on the leading edge takes the one swing");
        Assert.That(offStrip.Hp, Is.EqualTo(StartHp), "a player off the strip is untouched by the same swing");
    }

    // The whole body is the NPC, so every tile touching it is in reach — including the ones beyond a corner
    // column or row, where the direction read off the ANCHOR points along the wrong axis entirely. A player
    // standing above a size-3 body's right-hand column is one tile from three tons of monster; the anchor sits
    // two columns to its left, so an anchor-derived facing calls that "to the right" and swings at empty air.
    [TestCase(7, 4, TestName = "Footprint_AboveTheRightColumn")]
    [TestCase(5, 4, TestName = "Footprint_AboveTheLeftColumn")]
    [TestCase(4, 7, TestName = "Footprint_LeftOfTheBottomRow")]
    [TestCase(4, 5, TestName = "Footprint_LeftOfTheTopRow")]
    [TestCase(8, 7, TestName = "Footprint_RightOfTheBottomRow")]
    [TestCase(7, 8, TestName = "Footprint_BelowTheRightColumn")]
    public void Size3Npc_ReachesEveryTileTouchingItsBody(int px, int py)
    {
        var (combat, world, pm) = NewCombat();
        world.Npcs[1].Size = 3;
        world.Npcs[1].Str = 50;
        world.Npcs[1].Behavior = NpcBehavior.AttackOnSight;
        PlaceNpc(world, 1, 1, 5, 5);   // body {5,6,7} x {5,6,7}
        RegisterPlayer(world, pm, 10, px, py);

        Assert.That(combat.CanNpcAttackPlayer(Map, 1, 10, Now), Is.True,
            $"({px},{py}) touches the body, so it is in reach");
    }

    [TestCase(8, 8, TestName = "Footprint_DiagonalPastTheCorner")]
    [TestCase(4, 4, TestName = "Footprint_DiagonalPastTheOtherCorner")]
    [TestCase(9, 6, TestName = "Footprint_TwoColumnsOut")]
    public void Size3Npc_DoesNotReachPastItsBody(int px, int py)
    {
        var (combat, world, pm) = NewCombat();
        world.Npcs[1].Size = 3;
        world.Npcs[1].Str = 50;
        world.Npcs[1].Behavior = NpcBehavior.AttackOnSight;
        PlaceNpc(world, 1, 1, 5, 5);
        RegisterPlayer(world, pm, 10, px, py);

        Assert.That(combat.CanNpcAttackPlayer(Map, 1, 10, Now), Is.False,
            $"({px},{py}) only touches the body at a corner or not at all");
    }

    [Test]
    public void Size1Npc_MeleeStaysOmnidirectional()
    {
        var (combat, world, pm) = NewCombat();
        world.Npcs[1].Size = 1;
        world.Npcs[1].Str = 50;
        world.Npcs[1].Behavior = NpcBehavior.AttackOnSight;
        var n = PlaceNpc(world, 1, 1, 5, 5);
        n.Dir = Direction.Right;  // faces right...
        RegisterPlayer(world, pm, 10, 5, 4);                            // ...but the player is directly ABOVE
        Assert.That(combat.CanNpcAttackPlayer(Map, 1, 10, Environment.TickCount64), Is.True,
            "a size-1 NPC still melees any Manhattan-1 neighbor regardless of facing");
    }

    // ── NPC vs NPC reach (both sides can be oversize) ─────────────────────────
    // Unlike the player gate, neither side here is guaranteed size 1. Two size-3 bodies whose edges touch sit
    // three tiles apart anchor to anchor, so an anchor-distance gate refuses the swing while the pathing keeps
    // trying to close — which is what made them shuffle around each other instead of fighting.

    [Test]
    public void Size3Npcs_TouchingEdges_CanAttackEachOther()
    {
        var (combat, world, _) = NewCombat();
        world.Npcs[1].Size = 3;
        world.Npcs[2].Size = 3;
        var a = PlaceNpc(world, 1, 1, 5, 5);   // x 5..7, y 5..7
        var b = PlaceNpc(world, 2, 2, 8, 5);   // x 8..10, y 5..7 — its left edge against a's right edge

        Assert.Multiple(() =>
        {
            Assert.That(WorldCoordHelper.WorldManhattan(5, 5, 8, 5), Is.EqualTo(3),
                "a touching pair is 3 apart by anchor — the distance the old gate measured");
            Assert.That(combat.CanNpcAttackNpc(Map, a, Map, b, Now), Is.True, "touching edges are in reach");
            Assert.That(combat.CanNpcAttackNpc(Map, b, Map, a, Now), Is.True, "and reach reads the same from either body");
        });
    }

    [Test]
    public void Size3Npcs_WithAClearTileBetween_StillCannotAttack()
    {
        var (combat, world, _) = NewCombat();
        world.Npcs[1].Size = 3;
        world.Npcs[2].Size = 3;
        var a = PlaceNpc(world, 1, 1, 5, 5);   // x 5..7
        var b = PlaceNpc(world, 2, 2, 9, 5);   // x 9..11 — column 8 is empty between them

        Assert.That(combat.CanNpcAttackNpc(Map, a, Map, b, Now), Is.False, "one clear tile of gap is out of reach");
    }

    [Test]
    public void Size3Npcs_MeetingOnlyAtACorner_CannotAttack()
    {
        var (combat, world, _) = NewCombat();
        world.Npcs[1].Size = 3;
        world.Npcs[2].Size = 3;
        var a = PlaceNpc(world, 1, 1, 5, 5);   // x 5..7, y 5..7
        var b = PlaceNpc(world, 2, 2, 8, 8);   // x 8..10, y 8..10 — corner to corner only

        Assert.That(combat.CanNpcAttackNpc(Map, a, Map, b, Now), Is.False, "melee is cardinal — a diagonal corner is not reach");
    }

    [Test]
    public void Size1Npcs_ReachIsUnchanged()
    {
        var (combat, world, _) = NewCombat();
        world.Npcs[1].Size = 1;
        world.Npcs[2].Size = 1;
        world.Npcs[3].Size = 1;
        var a = PlaceNpc(world, 1, 1, 5, 5);
        var neighbor = PlaceNpc(world, 2, 2, 5, 6);
        var twoAway = PlaceNpc(world, 3, 3, 5, 7);

        Assert.Multiple(() =>
        {
            Assert.That(combat.CanNpcAttackNpc(Map, a, Map, neighbor, Now), Is.True, "the classic one-tile neighbor");
            Assert.That(combat.CanNpcAttackNpc(Map, a, Map, twoAway, Now), Is.False, "two tiles is still out of reach");
        });
    }

    // ── Cross-seam movement (the straddle enabler) ────────────────────────────
    [Test]
    public void Size2Npc_CanStepTowardSeam_LeadingEdgeValidatedOnNeighbor()
    {
        var (world, movement) = NewMovement();
        world.Maps[1].Right = 2;
        world.Maps[2].Left = 1;
        world.Npcs[1].Size = 2;
        var n = world.MapNpcs[1, 1];
        n.Num = 1;
        n.X = 14;
        n.Y = 5;  // footprint {14,15} on map 1
        Assert.That(movement.CanNpcMoveFrom(1, n, Direction.Right), Is.True,
            "the leading edge (col 16) resolves onto the right neighbor and is clear, so the body may straddle the seam");
    }

    [Test]
    public void Size2Npc_BlockedFromSeam_WhenNeighborLandingTileOccupied()
    {
        var (world, movement) = NewMovement();
        world.Maps[1].Right = 2;
        world.Maps[2].Left = 1;
        world.Npcs[1].Size = 2;
        var n = world.MapNpcs[1, 1];
        n.Num = 1;
        n.X = 14;
        n.Y = 5;
        // A blocker on the neighbor tile the leading edge would enter (map 2, local (0,5)).
        world.Npcs[2].Size = 1;
        var blocker = world.MapNpcs[2, 1];
        blocker.Num = 2;
        blocker.X = 0;
        blocker.Y = 5;
        Assert.That(movement.CanNpcMoveFrom(1, n, Direction.Right), Is.False,
            "a blocker on the neighbor-side leading-edge tile stops the straddle step");
    }

    [Test]
    public void Size2Npc_WithinMapStep_NeverWalksAnchorOffMap()
    {
        // The wander bug: a big NPC steps via CanNpcMove (no StepLeavesMap), so a within-map step must refuse
        // to push the anchor off the edge - otherwise its footprint lands on no on-map tile and stops blocking.
        var (world, movement) = NewMovement();
        world.Maps[1].Right = 2;
        world.Maps[2].Left = 1;
        world.Npcs[1].Size = 2;
        var n = world.MapNpcs[1, 1];
        n.Num = 1;
        n.X = Constants.MaxMapX;
        n.Y = 5;  // anchor at the right edge (footprint straddling)
        Assert.That(movement.CanNpcMoveFrom(1, n, Direction.Right), Is.False,
            "a within-map step must not move the anchor off-map; a genuine cross is handled by StepLeavesMap/TryNativeStep");
    }

    // ── Harness (per-file convention: local combat build + no-op dispatcher) ───
    // Two-layer world: an NPC can't melee another NPC across layers — only where a ramp connects them ("layer 1.5").
    [Test]
    public void CanNpcAttackNpc_GatedByLayer_ExceptAtARampFoot()
    {
        var (combat, world, _) = NewCombat();
        var attacker = PlaceNpc(world, 1, 1, 5, 6);   // ground foot
        var victim = PlaceNpc(world, 2, 1, 5, 5);     // one tile up (orthogonally adjacent)

        Assert.Multiple(() =>
        {
            // Same layer, adjacent → connects.
            attacker.Layer = WorldLayer.Ground;
            victim.Layer = WorldLayer.Ground;
            Assert.That(combat.CanNpcAttackNpc(Map, attacker, Map, victim, Now), Is.True, "same-layer neighbors connect");

            // Victim on the fringe over a plain tile → no reach from the ground.
            victim.Layer = WorldLayer.Fringe;
            Assert.That(combat.CanNpcAttackNpc(Map, attacker, Map, victim, Now), Is.False, "no cross-layer melee on a plain tile");

            // A ramp on the victim's tile (mounts from below) → the ground attacker at its foot connects.
            world.Maps[Map].EditTile(5, 5, t => t with { FringeAttr = new FringeAttr { Type = TileType.LayerRamp, RampGroundSide = Direction.Down } });
            Assert.That(combat.CanNpcAttackNpc(Map, attacker, Map, victim, Now), Is.True, "reaches a victim on the adjacent ramp");
        });
    }

    // The reported asymmetry: an NPC on the ground can't melee a player up on the fringe (or vice-versa) unless the
    // fringe endpoint is on a ramp — CanNpcAttackPlayer must gate on layer exactly like the player's own melee.
    [Test]
    public void CanNpcAttackPlayer_GatedByLayer_ExceptAtARampFoot()
    {
        var (combat, world, pm) = NewCombat();
        world.Npcs[1].Size = 1;
        world.Npcs[1].Str = 50;
        world.Npcs[1].Behavior = NpcBehavior.AttackOnSight;
        var n = PlaceNpc(world, 1, 1, 5, 6);            // NPC on the ground foot
        var pc = RegisterPlayer(world, pm, 10, 5, 5);   // player one tile up, adjacent

        Assert.Multiple(() =>
        {
            // Same layer, adjacent → the NPC can swing.
            n.Layer = WorldLayer.Ground;
            pc.Layer = WorldLayer.Ground;
            Assert.That(combat.CanNpcAttackPlayer(Map, 1, 10, Environment.TickCount64), Is.True, "same-layer neighbors connect");

            // Player up on the fringe over a plain tile → the NPC beneath can't reach it.
            pc.Layer = WorldLayer.Fringe;
            Assert.That(combat.CanNpcAttackPlayer(Map, 1, 10, Environment.TickCount64), Is.False, "no cross-layer melee on a plain tile");

            // A ramp on the player's tile → the ground NPC at its foot connects.
            world.Maps[Map].EditTile(5, 5, t => t with { FringeAttr = new FringeAttr { Type = TileType.LayerRamp, RampGroundSide = Direction.Down } });
            Assert.That(combat.CanNpcAttackPlayer(Map, 1, 10, Environment.TickCount64), Is.True, "reaches a player on the adjacent ramp");
        });
    }

    // ── The strip strikes NPCs too ────────────────────────────────────────────
    // A body three tiles wide swings once and hits everything on the tiles past its leading edge. That
    // was true of players and not of NPCs, so the same mob cleaved a line of players and pecked at one
    // of two enemies pressed against the same face.

    static MapNpcRecord PlaceFoe(GameWorld world, int slot, int num, int x, int y, int size = 1,
                                 NpcBehavior behavior = NpcBehavior.AttackOnSight, int group = 0)
    {
        world.Npcs[num].Size = size;
        world.Npcs[num].Behavior = behavior;
        world.Npcs[num].Group = group;
        var mn = world.MapNpcs[Map, slot];
        mn.Num = num;
        mn.X = x;
        mn.Y = y;
        mn.Hp = StartHp;
        return mn;
    }

    static MapNpcRecord PlaceWideAttacker(GameWorld world, int slot, int num, int x, int y,
                                          NpcBehavior behavior = NpcBehavior.AttackOnSight)
    {
        world.Npcs[num].Size = 3;
        world.Npcs[num].Str = 60;
        world.Npcs[num].Behavior = behavior;
        var mn = world.MapNpcs[Map, slot];
        mn.Num = num;
        mn.X = x;
        mn.Y = y;
        mn.Hp = StartHp;
        mn.Dir = Direction.Right;   // footprint cols {5,6,7}; strike strip = col 8, rows {5,6,7}
        mn.AttackTimer = 0;
        return mn;
    }

    /// <summary>🔴 The reported bug: two enemies on one edge, one taking damage.</summary>
    [Test]
    public void WideNpc_StrikesEveryEnemyOnItsLeadingEdge()
    {
        var (combat, world, _) = NewCombat();
        var attacker = PlaceWideAttacker(world, 1, 1, 5, 5);
        var a = PlaceFoe(world, 2, 2, 8, 5);
        var b = PlaceFoe(world, 3, 3, 8, 6);
        var offStrip = PlaceFoe(world, 4, 4, 8, 8);   // one row past the strip

        combat.NpcAttackNpc(Map, 1, attacker, Map, 2, a, Now);

        Assert.Multiple(() =>
        {
            Assert.That(a.Hp, Is.LessThan(StartHp), "the primary victim");
            Assert.That(b.Hp, Is.LessThan(StartHp), "and the one beside it on the same face");
            Assert.That(offStrip.Hp, Is.EqualTo(StartHp), "but not a body off the strip");
        });
    }

    /// <summary>The whole swing is one beat, so a wide victim covering several strip tiles takes ONE hit —
    /// anchor-per-tile matching would charge it three.</summary>
    [Test]
    public void AWideVictim_IsStruckOnceHoweverMuchOfTheStripItCovers()
    {
        var (combat, world, _) = NewCombat();
        var attacker = PlaceWideAttacker(world, 1, 1, 5, 5);
        // A body two tiles deep on the strip, and a one-tile body on the third. They cannot SHARE a tile —
        // occupancy is footprint-aware, so two bodies never stand on one — and the sweep asks each tile who
        // is on it, so a stacked pair would be one answer anyway.
        var wide = PlaceFoe(world, 2, 2, 8, 5, size: 2);      // covers (8,5) (8,6) of the strip
        var single = PlaceFoe(world, 3, 3, 8, 7);             // reference: one tile, one hit

        combat.NpcAttackNpc(Map, 1, attacker, Map, 2, wide, Now);
        int wideLoss = StartHp - wide.Hp;

        var (combat2, world2, _) = NewCombat();
        var attacker2 = PlaceWideAttacker(world2, 1, 1, 5, 5);
        var lone = PlaceFoe(world2, 2, 2, 8, 5);
        combat2.NpcAttackNpc(Map, 1, attacker2, Map, 2, lone, Now);
        int loneLoss = StartHp - lone.Hp;

        Assert.Multiple(() =>
        {
            Assert.That(wideLoss, Is.GreaterThan(0), "the wide body is struck");
            Assert.That(single.Hp, Is.LessThan(StartHp), "so is the one sharing its tile row");
            // Damage varies per swing, so compare against three times the worst a single hit can be.
            Assert.That(wideLoss, Is.LessThan(loneLoss * 2),
                "a body straddling two strip tiles must take one hit, not one per tile");
        });
    }

    /// <summary>A warband does not mince itself. Allies are same-kind or same non-zero Group, the rule the AI
    /// acquires by.</summary>
    [Test]
    public void AnAllyOnTheStrip_IsNeverStruck()
    {
        var (combat, world, _) = NewCombat();
        world.Npcs[1].Group = 7;
        var attacker = PlaceWideAttacker(world, 1, 1, 5, 5);
        var foe = PlaceFoe(world, 2, 2, 8, 5);
        var sameGroup = PlaceFoe(world, 3, 3, 8, 6, group: 7);
        // Placed by hand: PlaceFoe would rewrite template 1's Size, and template 1 is the ATTACKER — the
        // helper would shrink it to a single tile and there would be no strip left to test.
        var sameKind = world.MapNpcs[Map, 4];
        sameKind.Num = 1;
        sameKind.X = 8;
        sameKind.Y = 7;
        sameKind.Hp = StartHp;

        combat.NpcAttackNpc(Map, 1, attacker, Map, 2, foe, Now);

        Assert.Multiple(() =>
        {
            Assert.That(foe.Hp, Is.LessThan(StartHp), "the enemy is struck");
            Assert.That(sameGroup.Hp, Is.EqualTo(StartHp), "a same-group ally is not");
            Assert.That(sameKind.Hp, Is.EqualTo(StartHp), "nor is one of the attacker's own kind");
        });
    }

    /// <summary>A wide guard cutting down whatever stands behind its target would make the safest tiles the
    /// most dangerous. Its primary is always struck; a bystander has to be aggressive in its own right.</summary>
    [Test]
    public void AGuard_SparesABystanderItWouldNotHavePickedAFightWith()
    {
        var (combat, world, _) = NewCombat();
        var guard = PlaceWideAttacker(world, 1, 1, 5, 5, NpcBehavior.Guard);
        var target = PlaceFoe(world, 2, 2, 8, 5);
        var bystander = PlaceFoe(world, 3, 3, 8, 6, behavior: NpcBehavior.Friendly);
        var aggressor = PlaceFoe(world, 4, 4, 8, 7, behavior: NpcBehavior.AttackOnSight);

        combat.NpcAttackNpc(Map, 1, guard, Map, 2, target, Now);

        Assert.Multiple(() =>
        {
            Assert.That(target.Hp, Is.LessThan(StartHp), "the guard's own target");
            Assert.That(aggressor.Hp, Is.LessThan(StartHp), "and anything hostile beside it");
            Assert.That(bystander.Hp, Is.EqualTo(StartHp), "but not a friendly caught in the arc");
        });
    }

    /// <summary>An ORDINARY mob is not a guard and does not care who is standing there — the same principle
    /// the player strip states.</summary>
    [Test]
    public void AnOrdinaryMob_StrikesEvenAPassiveBystander()
    {
        var (combat, world, _) = NewCombat();
        var attacker = PlaceWideAttacker(world, 1, 1, 5, 5);
        var target = PlaceFoe(world, 2, 2, 8, 5);
        var passive = PlaceFoe(world, 3, 3, 8, 6, behavior: NpcBehavior.Friendly);

        combat.NpcAttackNpc(Map, 1, attacker, Map, 2, target, Now);

        Assert.That(passive.Hp, Is.LessThan(StartHp));
    }

    /// <summary>Size 1 is untouched by any of this: one swing, one victim.</summary>
    [Test]
    public void ASingleTileNpc_StillStrikesOnlyItsTarget()
    {
        var (combat, world, _) = NewCombat();
        world.Npcs[1].Size = 1;
        world.Npcs[1].Str = 60;
        world.Npcs[1].Behavior = NpcBehavior.AttackOnSight;
        var attacker = world.MapNpcs[Map, 1];
        attacker.Num = 1;
        attacker.X = 7;
        attacker.Y = 5;
        attacker.Hp = StartHp;
        attacker.Dir = Direction.Right;
        attacker.AttackTimer = 0;

        var target = PlaceFoe(world, 2, 2, 8, 5);
        var neighbour = PlaceFoe(world, 3, 3, 8, 6);

        combat.NpcAttackNpc(Map, 1, attacker, Map, 2, target, Now);

        Assert.Multiple(() =>
        {
            Assert.That(target.Hp, Is.LessThan(StartHp));
            Assert.That(neighbour.Hp, Is.EqualTo(StartHp), "a size-1 body has no strip to cleave along");
        });
    }

    static (CombatSystem combat, GameWorld world, PlayerManager pm) NewCombat()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var blood = new BloodSystem(world, dispatcher);
        var movement = new MovementSystem(world, pm, dispatcher, blood);
        var combat = new CombatSystem(world, pm, dispatcher, items: null!, movement, joinLeave: null!, blood,
            objectives: new ObjectiveSystem(), guilds: null!, guildWar: null!, territory: null!);
        return (combat, world, pm);
    }

    static (GameWorld world, MovementSystem movement) NewMovement()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var blood = new BloodSystem(world, dispatcher);
        var movement = new MovementSystem(world, pm, dispatcher, blood);
        return (world, movement);
    }

    static MapNpcRecord PlaceNpc(GameWorld world, int slot, int num, int x, int y)
    {
        var mn = world.MapNpcs[Map, slot];
        mn.Num = num;
        mn.X = x;
        mn.Y = y;
        mn.Hp = 100;
        return mn;
    }

    static PlayerRecord RegisterPlayer(GameWorld world, PlayerManager pm, int index, int x, int y)
    {
        var sp = pm[index];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        var pc = sp.Char;
        pc.Map = Map;
        pc.X = x;
        pc.Y = y;
        pc.Level = 1;
        pc.MaxHp = StartHp;
        pc.Hp = StartHp;
        pc.Sp = 0;   // no SP → no block/dodge, so a landed swing always deals damage (deterministic)
        pc.Access = AdminLevel.Player;
        world.MapObservers[Map].Add(index);
        return pc;
    }

    sealed class NoOpDispatcher : IPacketDispatcher
    {
        public void SendTo(int index, IPacket packet) { }
        public void SendToAll(IPacket packet) { }
        public void SendToAllBut(int exclude, IPacket packet) { }
        public void SendToObservers(IReadOnlyCollection<int> observers, IPacket packet) { }
        public void SendToObserversBut(IReadOnlyCollection<int> observers, int exclude, IPacket packet) { }
        public void SendToViewport(int speakerIndex, IPacket packet) { }
        public void SendToViewportAt(int mapNum, int x, int y, IPacket packet) { }
        public void SendChatBubble(int speakerIndex, IPacket packet, string senderLogin, bool wholeRegion) { }
        public void SendToAdmins(IPacket packet) { }
        public void SendToGuild(int guildId, IPacket packet) { }
        public void SendToGuildBut(int guildId, int exclude, IPacket packet) { }
        public void SendLocalizedChatToGuild(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToGuildOfficers(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatTo(int index, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAll(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAllBut(int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObservers(IReadOnlyCollection<int> observers, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObserversBut(IReadOnlyCollection<int> observers, int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewport(int speakerIndex, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewportAt(int mapNum, int x, int y, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAdmins(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendToEditor(int editorIndex, IPacket packet) { }
        public void SendToAllEditors(IPacket packet) { }
        public void Disconnect(int index) { }
        public void DisconnectEditor(int editorIndex) { }
        public void GracefulDisconnect(int index) { }
        public void GracefulDisconnectEditor(int editorIndex) { }
    }
}

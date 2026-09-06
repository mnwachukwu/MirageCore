using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.World;

/// <summary>Player + NPC movement validity. Pure tile-type rules (guards alone ignore NpcAvoid); the
/// authoritative server-side PlayerMove (step into open tiles, refuse walls / closed doors / map edges with
/// no neighbor, run-stamina drain that no-SP downgrades, and warp-tile teleport); and
/// NPC tile-freedom checks.</summary>
[TestFixture]
public class MovementSystemTests
{
    const int Map = 1, Idx = 1;

    static (GameWorld world, PlayerManager pm, MovementSystem move, PlayerRecord p) Setup(int x, int y,
        IPacketDispatcher? dispatcher = null)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        dispatcher ??= new NoOpDispatcher();
        var blood = new BloodSystem(world, dispatcher);
        var move = new MovementSystem(world, pm, dispatcher, blood);
        var sp = pm[Idx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        var p = sp.Char;
        p.Map = Map;
        p.X = x;
        p.Y = y;
        p.MaxHp = 100;
        p.Hp = 100;  // full HP => no blood trail deposits during a step
        world.MapObservers[Map].Add(Idx);
        return (world, pm, move, p);
    }

    // ── Pure tile-type rules ─────────────────────────────────────────────────────

    [Test]
    public void IsNpcWalkableTileType_WalkableAndItemAlways_BlockersNever_NpcAvoidGuardsOnly()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MovementSystem.IsNpcWalkableTileType(TileType.Walkable, false), Is.True);
            Assert.That(MovementSystem.IsNpcWalkableTileType(TileType.Item, false), Is.True);
            Assert.That(MovementSystem.IsNpcWalkableTileType(TileType.Blocked, false), Is.False);
            Assert.That(MovementSystem.IsNpcWalkableTileType(TileType.Warp, false), Is.False);
            Assert.That(MovementSystem.IsNpcWalkableTileType(TileType.Key, true), Is.False);
            Assert.That(MovementSystem.IsNpcWalkableTileType(TileType.NpcAvoid, false), Is.False, "a wall for normal NPCs");
            Assert.That(MovementSystem.IsNpcWalkableTileType(TileType.NpcAvoid, true), Is.True, "walkable for guards");
            Assert.That(MovementSystem.IsNpcWalkableTileType(TileType.LayerRamp, false), Is.True, "a ramp is the walkable connector between layers");
        });
    }

    [Test]
    public void NpcIgnoresNpcAvoid_OnlyGuards()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MovementSystem.NpcIgnoresNpcAvoid(NpcBehavior.Guard), Is.True);
            Assert.That(MovementSystem.NpcIgnoresNpcAvoid(NpcBehavior.AttackOnSight), Is.False);
            Assert.That(MovementSystem.NpcIgnoresNpcAvoid(NpcBehavior.Friendly), Is.False);
        });
    }

    // ── PlayerMove ───────────────────────────────────────────────────────────────

    [Test]
    public void PlayerMove_OpenTile_Steps()
    {
        var (_, _, move, p) = Setup(5, 5);
        move.PlayerMove(Idx, Direction.Down, MovementType.Walking);
        Assert.Multiple(() =>
        {
            Assert.That(p.Y, Is.EqualTo(6));
            Assert.That(p.Dir, Is.EqualTo(Direction.Down));
        });
    }

    [Test]
    public void PlayerMove_BlockedTile_FacesButHoldsPosition()
    {
        var (world, _, move, p) = Setup(5, 5);
        world.Maps[Map].EditTile(5, 6, t => t with { Type = TileType.Blocked });
        move.PlayerMove(Idx, Direction.Down, MovementType.Walking);
        Assert.Multiple(() =>
        {
            Assert.That(p.Y, Is.EqualTo(5), "a wall stops the step");
            Assert.That(p.Dir, Is.EqualTo(Direction.Down), "but the player still turns to face it");
        });
    }

    [Test]
    public void PlayerMove_KeyDoor_ClosedBlocks_OpenPasses()
    {
        var (world, _, move, p) = Setup(5, 5);
        world.Maps[Map].EditTile(5, 6, t => t with { Type = TileType.Key });
        move.PlayerMove(Idx, Direction.Down, MovementType.Walking);
        Assert.That(p.Y, Is.EqualTo(5), "a closed door blocks");

        world.TempTiles[Map].OpenDoor(5, 6, WorldLayer.Ground, 1);
        move.PlayerMove(Idx, Direction.Down, MovementType.Walking);
        Assert.That(p.Y, Is.EqualTo(6), "an open door passes");
    }

    [Test]
    public void PlayerMove_MapEdge_NoNeighbor_HoldsPosition()
    {
        var (_, _, move, p) = Setup(5, 0);   // top row, no map above
        move.PlayerMove(Idx, Direction.Up, MovementType.Walking);
        Assert.That(p.Y, Is.EqualTo(0), "no neighbor above => can't leave the map");
    }

    [Test]
    public void PlayerMove_Running_DrainsOneSp()
    {
        var (_, _, move, p) = Setup(5, 5);
        p.MaxSp = 20;
        p.Sp = 20;
        move.PlayerMove(Idx, Direction.Down, MovementType.Running);
        Assert.Multiple(() =>
        {
            Assert.That(p.Y, Is.EqualTo(6));
            Assert.That(p.Sp, Is.EqualTo(19), "a run step drains 1 SP");
        });
    }

    /// <summary>A shield costs nothing extra to carry. It is already paid for in the fight — the only
    /// slot whose defense is rolled for rather than applied, and every block it wins spends stamina — so
    /// charging for the walk as well taxed the same choice twice.</summary>
    [Test]
    public void PlayerMove_Running_WithShield_DrainsTheSame()
    {
        var (_, _, move, p) = Setup(5, 5);
        p.MaxSp = 20;
        p.Sp = 20;
        p.ShieldSlot = 1;
        move.PlayerMove(Idx, Direction.Down, MovementType.Running);
        Assert.That(p.Sp, Is.EqualTo(19), "a shield does not add to run-stamina drain");
    }

    [Test]
    public void PlayerMove_RunWithNoSp_DowngradesToWalk()
    {
        var (_, _, move, p) = Setup(5, 5);
        p.MaxSp = 20;
        p.Sp = 0;
        move.PlayerMove(Idx, Direction.Down, MovementType.Running);
        Assert.Multiple(() =>
        {
            Assert.That(p.Y, Is.EqualTo(6), "still moves, at walking pace");
            Assert.That(p.Sp, Is.EqualTo(0), "no SP to drain");
        });
    }

    // Stepping onto a warp tile teleports to its WarpMap/WarpX/WarpY.
    [Test]
    public void PlayerMove_OntoWarpTile_Teleports()
    {
        var (world, _, move, p) = Setup(5, 5);
        world.Maps[Map].EditTile(5, 6, t => t with { Type = TileType.Warp, WarpMap = Map, WarpX = 8, WarpY = 9 });

        move.PlayerMove(Idx, Direction.Down, MovementType.Walking);

        Assert.Multiple(() =>
        {
            Assert.That(p.Map, Is.EqualTo(Map));
            Assert.That(p.X, Is.EqualTo(8), "warped to WarpX");
            Assert.That(p.Y, Is.EqualTo(9), "warped to WarpY");
        });
    }

    // ── Warp destinations that name no tile ──────────────────────────────────────
    // Every coordinate a warp is handed comes from outside the engine — an authored Warp attribute, a config
    // file, a character saved against an older map — and is indexed straight into MapRecord.Tile. Each case
    // below is an IndexOutOfRangeException on the game loop if the bound is not held.

    [TestCase(Constants.MaxMapX + 1, 5, TestName = "PlayerWarp past the right edge")]
    [TestCase(5, Constants.MaxMapY + 1, TestName = "PlayerWarp past the bottom edge")]
    [TestCase(-1, 5, TestName = "PlayerWarp to a negative column")]
    [TestCase(5, -1, TestName = "PlayerWarp to a negative row")]
    [TestCase(200, 200, TestName = "PlayerWarp far off the map")]
    public void PlayerWarp_ToATileThatDoesNotExist_LeavesThePlayerWhereTheyWere(int x, int y)
    {
        var (_, _, move, p) = Setup(3, 4);

        Assert.DoesNotThrow(() => move.PlayerWarp(Idx, Map, x, y));
        Assert.That((p.Map, p.X, p.Y), Is.EqualTo((Map, 3, 4)));
    }

    [Test]
    public void PlayerWarp_ToAMapThatDoesNotExist_LeavesThePlayerWhereTheyWere()
    {
        var (world, _, move, p) = Setup(3, 4);

        Assert.DoesNotThrow(() => move.PlayerWarp(Idx, world.Limits.Maps + 1, 5, 5));
        Assert.That((p.Map, p.X, p.Y), Is.EqualTo((Map, 3, 4)));
    }

    /// <summary>The refusal is spoken, so a broken door reads as broken in playtesting instead of as a
    /// door that does nothing.</summary>
    [Test]
    public void PlayerWarp_ToATileThatDoesNotExist_TellsThePlayer()
    {
        var chat = new ChatCapturingDispatcher();
        var (_, _, move, _) = Setup(3, 4, chat);

        move.PlayerWarp(Idx, Map, 99, 99);

        Assert.That(chat.Keys, Does.Contain(ServerStrings.MovementSystem_WarpDestinationMissing));
    }

    /// <summary>The case that reaches the engine in practice: a Warp tile authored with a destination past
    /// the edge of its own map. The step onto it lands; the teleport off it does not.</summary>
    [Test]
    public void PlayerMove_OntoAWarpTilePointingPastTheEdge_StepsOnAndStaysPut()
    {
        var (world, _, move, p) = Setup(5, 5);
        world.Maps[Map].EditTile(5, 6, t => t with { Type = TileType.Warp, WarpMap = Map, WarpX = 200, WarpY = 200 });

        Assert.DoesNotThrow(() => move.PlayerMove(Idx, Direction.Down, MovementType.Walking));
        Assert.That((p.Map, p.X, p.Y), Is.EqualTo((Map, 5, 6)), "on the warp tile, not through it");

        // And the refusal leaves nothing behind that the next step trips over.
        Assert.DoesNotThrow(() => move.PlayerMove(Idx, Direction.Up, MovementType.Walking));
    }

    // Two-plane world (§1b): the post-step Warp is read on the mover's OWN layer. A Warp authored on the fringe
    // deck (FringeAttr) fires for a fringe walker; a GROUND warp at the same tile is inert to someone crossing above.
    [Test]
    public void PlayerMove_FringeWarpFires_GroundWarpInertToAFringeWalker()
    {
        // (a) a fringe-deck warp fires for a fringe walker
        {
            var (world, _, move, p) = Setup(5, 5);
            p.Layer = WorldLayer.Fringe;
            world.Maps[Map].EditTile(5, 6, t => t with { FringeAttr = new FringeAttr { Type = TileType.Warp, WarpMap = Map, WarpX = 8, WarpY = 9 } });

            move.PlayerMove(Idx, Direction.Down, MovementType.Walking);

            Assert.Multiple(() =>
            {
                Assert.That(p.X, Is.EqualTo(8), "the fringe-deck warp fires and teleports");
                Assert.That(p.Y, Is.EqualTo(9));
            });
        }

        // (b) a ground warp does NOT fire for a fringe walker crossing the deck above it
        {
            var (world, _, move, p) = Setup(5, 5);
            p.Layer = WorldLayer.Fringe;
            world.Maps[Map].EditTile(5, 6, t => t with { Type = TileType.Warp, WarpMap = Map, WarpX = 8, WarpY = 9 });

            move.PlayerMove(Idx, Direction.Down, MovementType.Walking);

            Assert.Multiple(() =>
            {
                Assert.That((p.X, p.Y), Is.EqualTo((5, 6)), "the fringe walker just steps onto the tile — no teleport");
                Assert.That(p.Layer, Is.EqualTo(WorldLayer.Fringe), "still on the fringe plane");
            });
        }
    }

    // §1b target-layer: a Warp whose WarpLayer is Fringe delivers the player onto the deck.
    [Test]
    public void PlayerMove_WarpWithFringeDest_DeliversOntoTheFringePlane()
    {
        var (world, _, move, p) = Setup(5, 5);
        world.Maps[Map].EditTile(5, 6, t => t with { Type = TileType.Warp, WarpMap = Map, WarpX = 8, WarpY = 9, WarpLayer = WorldLayer.Fringe });

        move.PlayerMove(Idx, Direction.Down, MovementType.Walking);

        Assert.Multiple(() =>
        {
            Assert.That((p.X, p.Y), Is.EqualTo((8, 9)), "warped to the unpacked dest tile");
            Assert.That(p.Layer, Is.EqualTo(WorldLayer.Fringe), "delivered onto the fringe plane");
        });
    }

    // §1b per-layer doors: a KeyOpen opens the door on the layer AUTHORED in its DoorLayer — independent of the plane
    // the plate sits on — so a GROUND plate can open a FRINGE-deck door, leaving the ground door at that tile shut.
    [Test]
    public void PlayerMove_KeyOpen_OpensTheDoorOnItsAuthoredLayer()
    {
        var (world, _, move, p) = Setup(5, 5);   // p.Layer defaults to Ground → steps onto a ground plate
        var map = world.Maps[Map];
        map.EditTile(5, 6, t => t with { Type = TileType.KeyOpen });                                 // a GROUND KeyOpen plate at (5,6)
        map.EditTile(5, 6, t => t with { DoorX = 5 });
        map.EditTile(5, 6, t => t with { DoorY = 7 });  // targeting the door at (5,7)…
        map.EditTile(5, 6, t => t with { DoorLayer = WorldLayer.Fringe });                           // …on the FRINGE plane (cross-layer)
        map.EditTile(5, 7, t => t with { FringeAttr = new FringeAttr { Type = TileType.Key } });     // the fringe Key door it opens

        move.PlayerMove(Idx, Direction.Down, MovementType.Walking);   // step onto the ground plate

        var temp = world.TempTiles[Map];
        Assert.Multiple(() =>
        {
            Assert.That(temp.IsDoorOpen(5, 7, WorldLayer.Fringe), Is.True, "the authored (fringe) door opens");
            Assert.That(temp.IsDoorOpen(5, 7, WorldLayer.Ground), Is.False, "the ground door at the same tile is untouched");
        });
    }

    // §1b per-layer doors: a fringe Key door gates a FRINGE walker (closed blocks, open passes) but never a GROUND
    // walker beneath it — the ground plane at that tile is plain walkable, so the fringe door is invisible to it.
    [Test]
    public void PlayerMove_FringeKeyDoor_GatesFringeWalkerOnly()
    {
        // Ground walker passes under a closed fringe door.
        {
            var (world, _, move, p) = Setup(5, 5);   // p.Layer defaults to Ground
            world.Maps[Map].EditTile(5, 6, t => t with { FringeAttr = new FringeAttr { Type = TileType.Key } });
            move.PlayerMove(Idx, Direction.Down, MovementType.Walking);
            Assert.That(p.Y, Is.EqualTo(6), "a ground walker is not blocked by a fringe door above");
        }

        // Fringe walker is blocked by the closed fringe door, and passes once it opens.
        {
            var (world, _, move, p) = Setup(5, 5);
            p.Layer = WorldLayer.Fringe;
            world.Maps[Map].EditTile(5, 6, t => t with { FringeAttr = new FringeAttr { Type = TileType.Key } });

            move.PlayerMove(Idx, Direction.Down, MovementType.Walking);
            Assert.That(p.Y, Is.EqualTo(5), "a closed fringe door blocks the fringe walker");

            world.TempTiles[Map].OpenDoor(5, 6, WorldLayer.Fringe, 1);
            move.PlayerMove(Idx, Direction.Down, MovementType.Walking);
            Assert.That(p.Y, Is.EqualTo(6), "an open fringe door lets the fringe walker through");
        }
    }

    // ── The pace gate ────────────────────────────────────────────────────────────
    // PlayerMove validates WHERE a step lands; these cover WHEN it is allowed to. The property that
    // matters is the SUSTAINED one — a client cannot average more than the ms-per-tile its SPD and
    // movement type earn it — with a bounded bank on top so ordinary network bunching is absorbed
    // rather than rubber-banded.

    // How many steps a full bank pays for at a given per-tile cost: the bank starts one window behind
    // "now", and each step pushes it forward by one tile, so the step that carries it PAST now is the
    // last one allowed.
    static int BurstSteps(float msPerTile) =>
        (int)(MovementSystem.MoveCreditWindowMs / (long)msPerTile) + 1;

    // Steps allowed out of `count` attempts made every `cadenceMs`, starting from a full bank.
    static int AllowedAtCadence(int count, long cadenceMs, MovementType movement, int spd)
    {
        var sp = new ServerPlayer();
        long now = 1_000_000;   // well past the window, so the fresh 0 clamps to a full bank
        int allowed = 0;
        for (int i = 0; i < count; i++, now += cadenceMs)
            if (MovementSystem.TryConsumeMoveCredit(sp, movement, spd, now)) allowed++;
        return allowed;
    }

    [Test]
    public void MoveCredit_AFreshSlotStartsWithAFullBank_ThenRefuses()
    {
        int burst = BurstSteps(MovementFormulas.BaseWalkMsPerTile);
        // Every attempt at the same instant, so only the bank can pay for them.
        Assert.That(AllowedAtCadence(burst + 5, 0, MovementType.Walking, spd: 0), Is.EqualTo(burst),
            "a slot that has never moved starts with exactly one window of credit and no more");
    }

    [Test]
    public void MoveCredit_NeverBanksMoreThanOneWindow_HoweverLongThePause()
    {
        var sp = new ServerPlayer();
        Assert.That(MovementSystem.TryConsumeMoveCredit(sp, MovementType.Walking, 0, 1_000_000), Is.True);

        // An hour parked. The bank must refill to one window, not to an hour's worth of steps.
        long later = 1_000_000 + 3_600_000;
        int allowed = 0;
        for (int i = 0; i < 200; i++)
            if (MovementSystem.TryConsumeMoveCredit(sp, MovementType.Walking, 0, later)) allowed++;

        Assert.That(allowed, Is.EqualTo(BurstSteps(MovementFormulas.BaseWalkMsPerTile)));
    }

    [Test]
    public void MoveCredit_RefillsInRealTime_OneTilePerPace()
    {
        var sp = new ServerPlayer();
        long now = 1_000_000;
        while (MovementSystem.TryConsumeMoveCredit(sp, MovementType.Walking, 0, now)) { }   // spend the bank

        // One tile's worth of elapsed time buys exactly one step back, and no second one.
        now += (long)MovementFormulas.BaseWalkMsPerTile;
        Assert.Multiple(() =>
        {
            Assert.That(MovementSystem.TryConsumeMoveCredit(sp, MovementType.Walking, 0, now), Is.True);
            Assert.That(MovementSystem.TryConsumeMoveCredit(sp, MovementType.Walking, 0, now), Is.False);
        });
    }

    // The one that has to hold for the gate to be shippable: a client walking at exactly the pace the
    // formulas set is NEVER refused, however long it walks. A gate that rubber-bands honest players would
    // be worse than the hole it closes.
    [Test]
    public void MoveCredit_TheHonestClientPaceIsNeverRefused()
    {
        Assert.That(AllowedAtCadence(500, (long)MovementFormulas.BaseWalkMsPerTile, MovementType.Walking, spd: 0),
            Is.EqualTo(500), "walking");
        Assert.That(AllowedAtCadence(500, (long)MovementFormulas.RunMsPerTile(150), MovementType.Running, spd: 150),
            Is.EqualTo(500), "running at the SPD cap");
    }

    [Test]
    public void MoveCredit_SustainedSpeedIsCappedAtThePace_NotAtTheBank()
    {
        // Twice the walking cadence over a long run: the bank pays for the opening burst, then every
        // second step is refused, so the average converges on the honest rate rather than on the burst.
        const int Attempts = 1000;
        int allowed = AllowedAtCadence(Attempts, (long)(MovementFormulas.BaseWalkMsPerTile / 2), MovementType.Walking, spd: 0);
        int burst = BurstSteps(MovementFormulas.BaseWalkMsPerTile);

        Assert.That(allowed, Is.EqualTo(Attempts / 2 + burst).Within(1),
            "half the attempts plus the one-time bank — a 2x client travels at 1x once the bank is gone");
    }

    [Test]
    public void MoveCredit_ChargesTheRunPaceEarnedBySpd()
    {
        // Same cadence, same movement type: the SPD-capped build is charged less per tile, so more of
        // its steps are affordable. This is the gate reading MovementFormulas rather than a flat rate.
        long cadence = (long)MovementFormulas.RunMsPerTile(150);
        Assert.That(AllowedAtCadence(200, cadence, MovementType.Running, spd: 150),
            Is.GreaterThan(AllowedAtCadence(200, cadence, MovementType.Running, spd: 0)));
    }

    [Test]
    public void MoveCredit_RunningIsBilledAtTheWalkPaceWhenTheServerDowngradedIt()
    {
        var (_, pm, move, p) = Setup(5, 0);
        p.Spd = 150;
        p.Sp = 0;   // no stamina — PlayerMove forces walking, so the run pace must not be what is charged

        for (int i = 0; i < 20; i++)
            move.PlayerMove(Idx, Direction.Down, MovementType.Running);

        Assert.That(p.Y, Is.EqualTo(BurstSteps(MovementFormulas.BaseWalkMsPerTile)),
            "claiming Running on an empty SP bar must not buy the cheaper run rate");
        Assert.That(pm[Idx].Char.Sp, Is.Zero);
    }

    [Test]
    public void PlayerMove_RefusesTheStepPastTheBank_AndCorrectsTheClient()
    {
        var capture = new CapturingDispatcher();
        var (_, _, move, p) = Setup(5, 0, capture);

        int burst = BurstSteps(MovementFormulas.BaseWalkMsPerTile);
        for (int i = 0; i < burst + 3; i++)
            move.PlayerMove(Idx, Direction.Down, MovementType.Walking);

        Assert.Multiple(() =>
        {
            Assert.That(p.Y, Is.EqualTo(burst), "the steps past the bank never happened");
            // A refusal has to CORRECT rather than just drop: the client predicted the step locally, so a
            // silent discard is exactly the desync a tampered client would live in.
            var corrections = capture.SelfMoves.Where(m => m.X == 5 && m.Y == burst).ToList();
            Assert.That(corrections, Has.Count.EqualTo(3), "one correction per refused step");
        });
    }

    // ── NPC tile validity ────────────────────────────────────────────────────────

    [Test]
    public void CanNpcMoveFrom_OpenTileTrue_BlockedFalse()
    {
        var (world, _, move, _) = Setup(0, 0);   // player parked away from the NPC's path
        world.Npcs[1].Behavior = NpcBehavior.AttackOnSight;
        var npc = world.MapNpcs[Map, 1];
        npc.Num = 1;
        npc.X = 5;
        npc.Y = 5;
        npc.Hp = 100;

        Assert.That(move.CanNpcMoveFrom(Map, npc, Direction.Down), Is.True, "an open tile is movable");

        world.Maps[Map].EditTile(5, 4, t => t with { Type = TileType.Blocked });
        Assert.That(move.CanNpcMoveFrom(Map, npc, Direction.Up), Is.False, "a wall stops the NPC");
    }

    // A ramp is a SOLID connector: a ground NPC can't mount it perpendicular to its axis, and can't walk under
    // it — only a correct up-ramp mount from the ground foot is legal (and that ascends to Fringe).  '^' ramps at
    // (5,5)+(6,5), ground side Down → vertical mount axis.  This is the exact "NPC moved perpendicular onto the
    // ramp" case, checked through the real gate the wander/chase use.
    [Test]
    public void CanNpcMoveFrom_RampSolidExceptTheMount_NoPerpendicular_NoUnderWalk()
    {
        var (world, _, move, _) = Setup(0, 0);
        world.Npcs[1].Behavior = NpcBehavior.AttackOnSight;
        var map = world.Maps[Map];
        map.EditTile(5, 5, t => t with { FringeAttr = new FringeAttr { Type = TileType.LayerRamp, RampGroundSide = Direction.Down } });
        map.EditTile(6, 5, t => t with { FringeAttr = new FringeAttr { Type = TileType.LayerRamp, RampGroundSide = Direction.Down } });
        var npc = world.MapNpcs[Map, 1];
        npc.Num = 1;
        npc.Hp = 100;

        Assert.Multiple(() =>
        {
            // Perpendicular onto the ramp from the side, on the ground — ILLEGAL.
            npc.X = 4;
            npc.Y = 5;
            npc.Layer = WorldLayer.Ground;
            Assert.That(move.CanNpcMoveFrom(Map, npc, Direction.Right), Is.False, "no perpendicular mount from the side");

            // Walking under the ramp from above (along-axis, but not the mount direction) — ILLEGAL (solid on ground).
            npc.X = 5;
            npc.Y = 4;
            npc.Layer = WorldLayer.Ground;
            Assert.That(move.CanNpcMoveFrom(Map, npc, Direction.Down), Is.False, "can't walk under a ramp on the ground");

            // From an adjacent ramp tile, moving perpendicular — also ILLEGAL on the ground (you can't BE on a
            // ramp on the ground, so this can't set up; but assert the gate refuses it if the layer is Ground).
            npc.X = 5;
            npc.Y = 5;
            npc.Layer = WorldLayer.Ground;
            Assert.That(move.CanNpcMoveFrom(Map, npc, Direction.Right), Is.False, "no ground-perpendicular between ramps");

            // The one legal way on: from the ground foot below, moving up-ramp — allowed, and it ascends.
            npc.X = 5;
            npc.Y = 6;
            npc.Layer = WorldLayer.Ground;
            Assert.That(move.CanNpcMoveFrom(Map, npc, Direction.Up), Is.True, "the up-ramp mount from the foot is legal");
            move.NpcMove(Map, 1, Direction.Up, MovementType.Walking);
            Assert.That(npc.Layer, Is.EqualTo(WorldLayer.Fringe), "and mounting ascends to the fringe");
        });
    }

    // Cross-SEAM ramp gate: an NPC stepping across a map seam onto a ramp must obey the SAME corridor rule as a
    // within-map step. A cross-border step that skips CanEnter is the failure this pins, and it shows on bridges.
    // Map 1's right edge abuts Map 2; the gate reads the neighbor's ramp tile in world space.
    [Test]
    public void NpcStepPassesRampGate_AcrossASeam_BlocksPerpendicular_AllowsAlongAxisMount()
    {
        var (world, _, move, _) = Setup(0, 0);
        world.Npcs[1].Behavior = NpcBehavior.AttackOnSight;
        world.Maps[Map].Right = 2;      // link Map 1 → Map 2 at the right seam
        world.Maps[2].Left = Map;
        var npc = world.MapNpcs[Map, 1];
        npc.Num = 1;
        npc.Hp = 100;
        npc.X = Constants.MaxMapX;
        npc.Y = 5;
        npc.Layer = WorldLayer.Ground;

        Assert.Multiple(() =>
        {
            // A '^' ramp (vertical mount axis) just across the seam: stepping Right onto it is PERPENDICULAR → blocked.
            world.Maps[2].EditTile(0, 5, t => t with { FringeAttr = new FringeAttr { Type = TileType.LayerRamp, RampGroundSide = Direction.Down } });
            Assert.That(move.NpcStepPassesRampGate(Map, npc, Direction.Right, out _), Is.False,
                "no perpendicular mount across a seam");

            // A ramp whose ground side faces the NPC (mount axis ALONG the seam): stepping Right is the up-ramp
            // mount → allowed, and it ascends onto the bridge.
            world.Maps[2].EditTile(0, 5, t => t with { FringeAttr = new FringeAttr { Type = TileType.LayerRamp, RampGroundSide = Direction.Left } });
            Assert.That(move.NpcStepPassesRampGate(Map, npc, Direction.Right, out var layer), Is.True,
                "the along-axis mount across a seam is legal");
            Assert.That(layer, Is.EqualTo(WorldLayer.Fringe), "and it ascends");
        });
    }

    [Test]
    public void IsNpcDestFree_OffMap_False_EmptyTile_True_OccupiedTile_False()
    {
        var (world, _, move, _) = Setup(0, 0);
        var npc = world.MapNpcs[Map, 1];
        npc.Num = 1;
        npc.X = 7;
        npc.Y = 7;
        npc.Hp = 100;  // an NPC occupies (7,7)

        Assert.Multiple(() =>
        {
            Assert.That(move.IsNpcDestFree(Map, -1, 5, ignoreNpcAvoid: false), Is.False, "off-map is never free");
            Assert.That(move.IsNpcDestFree(Map, 5, 5, ignoreNpcAvoid: false), Is.True, "an empty walkable tile is free");
            Assert.That(move.IsNpcDestFree(Map, 7, 7, ignoreNpcAvoid: false), Is.False, "an NPC-occupied tile is not free");
        });
    }

    // Two-layer world: PlayerWarp / relog sets the layer to destLayer VERBATIM — no arrival re-fit. Persisting the
    // layer is what restores a player onto a bridge (destLayer carries the saved Fringe, and a ramp is walkable on
    // Fringe, so persistence alone is sufficient). Landing correctness is an AUTHORING concern for playtesting to
    // catch, not something the engine papers over — consistent with a bad persisted (X,Y) against an edited map.
    [Test]
    public void PlayerWarp_SetsLayerToDestLayer_Verbatim()
    {
        var (world, _, move, p) = Setup(0, 0);
        // A ramp at (5,5): solid (Blocked) on Ground, walkable (LayerRamp) on Fringe.
        world.Maps[Map].EditTile(5, 5, t => t with { FringeAttr = new FringeAttr { Type = TileType.LayerRamp, RampGroundSide = Direction.Down } });

        Assert.Multiple(() =>
        {
            // A relog carrying the persisted Fringe layer restores onto the ramp's Fringe surface — the
            // relog-onto-a-bridge fix, needing nothing but the verbatim set (the ramp is walkable on Fringe).
            move.PlayerWarp(Idx, Map, 5, 5, destLayer: WorldLayer.Fringe);
            Assert.That(p.Layer, Is.EqualTo(WorldLayer.Fringe));

            // A Ground target is honored verbatim — the engine does NOT re-fit onto the walkable Fringe surface
            // (a target that drops you in a wall is a map-authoring bug, deliberately not rescued here).
            move.PlayerWarp(Idx, Map, 5, 5, destLayer: WorldLayer.Ground);
            Assert.That(p.Layer, Is.EqualTo(WorldLayer.Ground));

            // A plain tile with a Ground target stays Ground.
            move.PlayerWarp(Idx, Map, 8, 8, destLayer: WorldLayer.Ground);
            Assert.That(p.Layer, Is.EqualTo(WorldLayer.Ground));
        });
    }

    // ── Harness ──────────────────────────────────────────────────────────────────

    sealed class ChatCapturingDispatcher : NoOpDispatcher
    {
        /// <summary>The localized key of every line spoken to a single player.</summary>
        public List<string> Keys { get; } = new();

        public override void SendLocalizedChatTo(int index, string key, ChatMetadata meta, params (string Key, object? Value)[] args) =>
            Keys.Add(key);
    }

    sealed class CapturingDispatcher : NoOpDispatcher
    {
        /// <summary>Every position packet sent to a single player — which for a mover is only ever a
        /// correction, since their own successful steps broadcast to the map EXCLUDING them.</summary>
        public List<SendPlayerMovePacket> SelfMoves { get; } = new();

        public override void SendTo(int index, IPacket packet)
        {
            if (packet is SendPlayerMovePacket m) SelfMoves.Add(m);
        }
    }

    class NoOpDispatcher : IPacketDispatcher
    {
        public virtual void SendTo(int index, IPacket packet) { }
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
        public virtual void SendLocalizedChatTo(int index, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
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

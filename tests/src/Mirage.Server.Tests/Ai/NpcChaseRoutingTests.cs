using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Tests.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Reflection;

namespace Mirage.Server.Tests.Ai;

// Regression coverage for the NPC-vs-NPC "mid-path blocker" case: a chaser whose straight-line path to its
// target is blocked by ANOTHER live NPC (not on the target's attack ring) will park behind it forever if it
// keeps re-planning blind, because the BFS plans THROUGH the blocker and the perfectly-in-line best-effort
// cannot sidestep.  So once STALLED, the chaser re-plans occupancy-aware (live actors are walls) and routes
// AROUND the blocker — EXCEPT actors that are chasing the chaser itself (its pursuers), which stay walkable
// so a guard walling off its target isn't danced around.
//
// The single-step decision is locked directly on the private BFS (FindStepTowardObservableArea) via
// reflection; a separate end-to-end test drives the real brain + legs and asserts the chaser reaches the
// victim.  Coords are 16x12 (x 0-15, y 0-11).
[TestFixture]
public class NpcChaseRoutingTests
{
    const int Map = 1;

    // ── Single-step BFS decision (deterministic) ──────────────────────────────
    // Geometry: victim V at (8,3); blocker B at (8,5) — two tiles below V, so OFF V's attack ring; chaser at
    // (8,6), directly behind B on the same column.  Blind plan steps straight up into B; the stalled
    // occupancy-aware plan must step sideways (B is masked, up is walled).

    [Test]
    public void FindStep_Blind_PlansStraightIntoMidPathBlocker()
    {
        var (ai, world) = NewWorldWithBlocker(blockerChasesChaser: false);
        var step = FindStep(ai, world, fromX: 8, fromY: 6, toX: 8, toY: 3, selfSlot: 1, planAroundActors: false);
        Assert.That(step, Is.EqualTo(Direction.Up), "blind chase ignores the live blocker and plans straight up into it");
    }

    [Test]
    public void FindStep_Stalled_RoutesAroundObliviousBlocker()
    {
        var (ai, world) = NewWorldWithBlocker(blockerChasesChaser: false);
        var step = FindStep(ai, world, fromX: 8, fromY: 6, toX: 8, toY: 3, selfSlot: 1, planAroundActors: true);
        Assert.That(step, Is.AnyOf(Direction.Left, Direction.Right),
            "a stalled chaser re-plans around the oblivious mid-path blocker to an open flank, not straight up into it");
    }

    [Test]
    public void FindStep_Stalled_HoldsBehindPursuer_NoDance()
    {
        // The blocker is now chasing the chaser (its NpcTarget == the chaser's identity): it must stay
        // walkable in the plan so the chaser holds behind it (settles) instead of routing around its own
        // hunter — the exact exclusion that keeps the guard↔AoS dance from reopening.
        var (ai, world) = NewWorldWithBlocker(blockerChasesChaser: true);
        var step = FindStep(ai, world, fromX: 8, fromY: 6, toX: 8, toY: 3, selfSlot: 1, planAroundActors: true);
        Assert.That(step, Is.EqualTo(Direction.Up), "a pursuer is not masked, so the chaser plans straight into it and holds (no dance)");
    }

    // ── The attack-slot ring is around the BODY, not the anchor ───────────────
    // A cell is an attack slot when a chaser standing there could swing — its body touching the target's.
    // Read off the anchor at Manhattan 1 that names four tiles, two of which sit INSIDE an oversize target,
    // and every real slot around it goes unrecognised: a trailer plans straight into an occupied one and
    // queues there instead of walking around to a free side.

    [Test]
    public void AttackSlotRing_AroundAnOversizeTarget_WallsOffAnOccupiedSlot()
    {
        var (ai, world) = OversizeVictimBehindAChokepoint(slotTaken: true);
        var step = FindStep(ai, world, fromX: 12, fromY: 4, toX: 8, toY: 3, selfSlot: 1,
                            planAroundActors: false, targetSize: 3);
        Assert.That(step, Is.Null,
            "the one slot on this side of the body is taken, so there is no route to a place this chaser could swing from");
    }

    [Test]
    public void AttackSlotRing_AroundAnOversizeTarget_LeavesAFreeSlotOpen()
    {
        // The control: the same geometry with nobody in the slot must still route straight through it.
        var (ai, world) = OversizeVictimBehindAChokepoint(slotTaken: false);
        var step = FindStep(ai, world, fromX: 12, fromY: 4, toX: 8, toY: 3, selfSlot: 1,
                            planAroundActors: false, targetSize: 3);
        Assert.That(step, Is.EqualTo(Direction.Left), "an empty attack slot is just a tile to walk through");
    }

    // A size-3 victim anchored at (8,3) — body {8,9,10} x {3,4,5} — walled off from the chaser at (12,4) by a
    // solid column at x=11 with a single gap at (11,4).  That gap is the only cell on this side whose body
    // would touch the victim's, so it is the only attack slot, and it is two tiles from the victim's ANCHOR.
    static (NpcAiSystem ai, GameWorld world) OversizeVictimBehindAChokepoint(bool slotTaken)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        world.Npcs[1].Behavior = NpcBehavior.AttackOnSight;   // the chaser, size 1
        world.Npcs[2].Behavior = NpcBehavior.Stationary;      // the victim's template
        world.Npcs[2].Size = 3;
        world.Npcs[3].Behavior = NpcBehavior.Stationary;      // whoever is standing in the slot

        for (int y = 0; y <= Constants.MaxMapY; y++)
        {
            if (y == 4) continue;
            world.Maps[Map].EditTile(11, y, t => t with { Type = TileType.Blocked });
        }

        var chaser = world.MapNpcs[Map, 1];
        chaser.Num = 1;
        chaser.X = 12;
        chaser.Y = 4;
        chaser.Hp = 100;

        var victim = world.MapNpcs[Map, 2];
        victim.Num = 2;
        victim.X = 8;
        victim.Y = 3;
        victim.Hp = 100;

        if (slotTaken)
        {
            var sitting = world.MapNpcs[Map, 3];
            sitting.Num = 3;
            sitting.X = 11;
            sitting.Y = 4;
            sitting.Hp = 100;
        }

        var ai = new NpcAiSystem(world, pm, null!, null!, null!, null!, null!, null!);
        return (ai, world);
    }

    // ── End-to-end: real brain + legs, chaser must reach the victim ────────────
    [Test]
    public void MidPathBlocker_ChaserRoutesAroundAndReachesVictim()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var ai = BuildAi(world, pm);

        world.Npcs[1].Behavior = NpcBehavior.AttackOnSight;   // chaser template
        world.Npcs[1].Str = 20;
        world.Npcs[1].Def = 10;
        world.Npcs[1].Int = 0;
        world.Npcs[1].Spd = 20;
        world.Npcs[1].Range = 5;
        world.Npcs[2].Behavior = NpcBehavior.Stationary;      // static: never moves, never targets anyone

        // Colinear: victim (8,3), static blocker (8,5) [off the victim's ring], chaser (8,7) behind the blocker.
        var chaser = world.MapNpcs[Map, 1];
        chaser.Num = 1;
        chaser.X = 8;
        chaser.Y = 7;
        chaser.Hp = 9999;
        chaser.Sp = 20;
        chaser.NpcTargetSpawnMap = Map;
        chaser.NpcTargetSpawnSlot = 3;
        chaser.HasMadeContact = true;
        var blocker = world.MapNpcs[Map, 2];
        blocker.Num = 2;
        blocker.X = 8;
        blocker.Y = 5;
        blocker.Hp = 9999;
        var victim = world.MapNpcs[Map, 3];
        victim.Num = 2;
        victim.X = 8;
        victim.Y = 3;
        victim.Hp = 9999;

        // A lone observer, far outside the chaser's aggro Range, so the map is processed but no player is acquired.
        var sp = pm[5];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        var pc = sp.Char;
        pc.Map = Map;
        pc.X = 0;
        pc.Y = 0;
        pc.Level = 1;
        world.MapObservers[Map].Add(5);

        long tick = 1_000_000;
        chaser.LastReachedTargetMs = tick;   // seed the unreachable-give-up clock as if freshly acquired
        int best = int.MaxValue;
        for (int i = 0; i < 40; i++)
        {
            chaser.NextMoveMs = 0;
            chaser.AttackTimer = 0;
            chaser.CombatExpiresAt = tick + 10_000_000;
            ai.RunMovement(tick);
            ai.RunForAllMaps(tick);
            best = Math.Min(best, Manhattan(chaser.X, chaser.Y, victim.X, victim.Y));
            if (best == 1) break;
            tick += 500;
        }
        Assert.That(best, Is.EqualTo(1),
            $"the chaser must route around the mid-path blocker and reach the victim (closest it got was distance {best})");
    }

    // ── Harness ───────────────────────────────────────────────────────────────
    static int Manhattan(int ax, int ay, int bx, int by) => Math.Abs(ax - bx) + Math.Abs(ay - by);

    // World for the single-step tests: chaser in slot 1 at (8,6); blocker in slot 2 at (8,5).  The blocker
    // optionally chases the chaser (pursuer case).  FindStep + its occupancy helper read only _world/_pm, so
    // the other NpcAiSystem dependencies can be null.
    static (NpcAiSystem ai, GameWorld world) NewWorldWithBlocker(bool blockerChasesChaser)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        world.Npcs[1].Behavior = NpcBehavior.AttackOnSight;
        world.Npcs[2].Behavior = NpcBehavior.Stationary;
        var chaser = world.MapNpcs[Map, 1];
        chaser.Num = 1;
        chaser.X = 8;
        chaser.Y = 6;
        chaser.Hp = 100;
        var blocker = world.MapNpcs[Map, 2];
        blocker.Num = 2;
        blocker.X = 8;
        blocker.Y = 5;
        blocker.Hp = 100;
        if (blockerChasesChaser)
        {
            blocker.NpcTargetSpawnMap = Map;
            blocker.NpcTargetSpawnSlot = 1;
        }  // its NpcTarget == the chaser
        var ai = new NpcAiSystem(world, pm, null!, null!, null!, null!, null!, null!);
        return (ai, world);
    }

    static Direction? FindStep(NpcAiSystem ai, GameWorld world, int fromX, int fromY, int toX, int toY, int selfSlot,
                               bool planAroundActors, int targetSize = 1)
    {
        var npc = world.Npcs[world.MapNpcs[Map, selfSlot].Num];
        var m = typeof(NpcAiSystem).GetMethod("FindStepTowardObservableArea", BindingFlags.NonPublic | BindingFlags.Instance)!;
        // (mapNum, fromX, fromY, fromLayer, targetMap, toX, toY, targetLayer, npc, planAroundActors, selfSpawnMap,
        //  selfSpawnSlot, targetSize)
        // These routing scenarios are all ground-layer, so both layers are Ground; every victim here is size 1.
        return (Direction?)m.Invoke(ai, new object[] { Map, fromX, fromY, WorldLayer.Ground, Map, toX, toY, WorldLayer.Ground, npc, planAroundActors, Map, selfSlot, targetSize });
    }

    // A real NpcAiSystem (real Combat/Movement/Blood/Spawn, no-op dispatcher) for the end-to-end drive; the
    // kill-only subsystems the chase never reaches are null.  Mirrors GuestNativeScenarioParityTests.BuildAi.
    static NpcAiSystem BuildAi(GameWorld world, PlayerManager pm)
    {
        var dispatcher = new NoOpDispatcher();
        var blood = new BloodSystem(world, dispatcher);
        var movement = new MovementSystem(world, pm, dispatcher, blood);
        var combat = new CombatSystem(world, pm, dispatcher, items: null!, movement, joinLeave: null!, blood, objectives: new ObjectiveSystem(), guilds: null!, guildWar: null!, territory: null!);
        var spawn = new SpawnSystem(world, pm, dispatcher);
        return new NpcAiSystem(world, pm, dispatcher, combat, movement, spawn, items: null!, blood);
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

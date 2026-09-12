using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Reflection;

namespace Mirage.Server.Tests.Ai;

// Focused coverage for NPC target ACQUISITION picking the nearest eligible candidate within the
// existing priority order (NpcAiSystem.Find*; see the "Target acquisition: line of sight,
// reachability & nearest" row in docs/changes-from-vb6.md).
//
// The Find* scanners are private and the assembly has no InternalsVisibleTo, so they're invoked by
// reflection on a minimally-wired system. Those methods read only _world and _pm (plus static
// helpers), so the other six constructor dependencies can be null. A fresh GameWorld() is a single
// open, walkable, neighborless map, so the line-of-sight and BFS-reachability gates pass trivially
// and distance (world-Manhattan, which collapses to local Manhattan on one map) is the only
// variable. Distances are made strictly distinct so the result is deterministic despite the
// non-deterministic HashSet iteration order of MapObservers.
[TestFixture]
public class NpcTargetAcquisitionTests
{
    const int Map = 1;
    const int ActorSlot = 1;          // the acting NPC (attacker / guard / mob) sits in slot 1 at (8,6)
    const int ActorX = 8, ActorY = 6;
    const int AggroRange = 15;        // spans the 16x12 map so range never excludes a candidate

    [Test]
    public void FindNoticeableNpc_PicksTheNearest_NotFirstInSlotOrder()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        world.Npcs[1].Behavior = NpcBehavior.Pursue;   // the noticing NPC's template
        world.Npcs[1].Range = AggroRange;
        world.Npcs[2].Behavior = NpcBehavior.Pursue;   // different Num => not kin, so noticeable

        var attacker = PlaceNpc(world, ActorSlot, num: 1, ActorX, ActorY);
        PlaceNpc(world, slot: 2, num: 2, ActorX, ActorY + 4);   // FAR  (dist 4), earlier in scan order
        PlaceNpc(world, slot: 3, num: 2, ActorX, ActorY + 2);   // NEAR (dist 2), later in scan order

        var winner = InvokeTuple(NewAi(world, pm), "FindNoticeableNpc", Map, ActorSlot, attacker);

        // Scan order alone would return slot 2 (first eligible); the nearest is slot 3.
        Assert.That(winner, Is.EqualTo((Map, 3)));
    }

    [Test]
    public void FindNoticeablePlayer_BreaksEqualLevelTieByNearest()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var mob = PlaceNpc(world, ActorSlot, num: 1, ActorX, ActorY);
        RegisterPlayer(world, pm, index: 5, ActorX, ActorY + 4, level: 3);   // FAR,  same level
        RegisterPlayer(world, pm, index: 6, ActorX, ActorY + 2, level: 3);   // NEAR, same level

        int winner = (int)InvokePrivate(NewAi(world, pm), "FindNoticeablePlayer", Map, mob, AggroRange);

        Assert.That(winner, Is.EqualTo(6));
    }

    [Test]
    public void FindNoticeablePlayer_KeepsLowestLevelOverCloserHigherLevel()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var mob = PlaceNpc(world, ActorSlot, num: 1, ActorX, ActorY);
        RegisterPlayer(world, pm, index: 5, ActorX, ActorY + 4, level: 2);   // FAR,  LOWER level
        RegisterPlayer(world, pm, index: 6, ActorX, ActorY + 2, level: 9);   // NEAR, higher level

        int winner = (int)InvokePrivate(NewAi(world, pm), "FindNoticeablePlayer", Map, mob, AggroRange);

        // Distance is only a tie-break: the intentional lowest-level "prey on the weak" rule still wins.
        Assert.That(winner, Is.EqualTo(5));
    }

    // ── Harness helpers ───────────────────────────────────────────────────────
    // The Find* scanners dereference only _world and _pm, so the remaining constructor
    // dependencies (dispatcher, combat, movement, spawn, items) are safely null here.
    static NpcAiSystem NewAi(GameWorld world, PlayerManager pm)
        => new(world, pm, null!, null!, null!, null!, null!, null!);

    static MapNpcRecord PlaceNpc(GameWorld world, int slot, int num, int x, int y, int target = 0)
    {
        var mn = world.MapNpcs[Map, slot];
        mn.Num = num;
        mn.X = x;
        mn.Y = y;
        mn.Hp = 100;
        mn.Target = target;
        return mn;
    }

    static void RegisterPlayer(GameWorld world, PlayerManager pm, int index, int x, int y, int level, bool pk = false)
    {
        var sp = pm[index];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        var pc = sp.Char;
        pc.Map = Map;
        pc.X = x;
        pc.Y = y;
        pc.Level = level;
        if (pk) pc.PkExpiryUtc = long.MaxValue;
        world.MapObservers[Map].Add(index);   // acquisition scans MapObservers; unobserved players are invisible
    }

    static object InvokePrivate(NpcAiSystem ai, string method, params object[] args)
        => typeof(NpcAiSystem).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(ai, args)!;

    static (int, int) InvokeTuple(NpcAiSystem ai, string method, params object[] args)
        => ((int, int))InvokePrivate(ai, method, args);
}

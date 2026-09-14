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

// Regression guard for the retreat DIRECTIONAL bias (see NpcAiSystem.PerpAwayDir).
//
// When a fleeing NPC is axis-aligned with whoever it noticed and its straight-away retreat is blocked
// (it is pinned against a wall), it must sidestep perpendicular.  Both perpendiculars are equally
// "away", so the choice must be a coin flip.  A fixed tie-break here (always Down when the alignment
// is horizontal, Right when vertical) funnels wall-pinned NPCs deterministically toward the
// bottom-right corner, piling them up on the right side of the map with a lean toward the bottom.
//
// This exercises the REAL private TryFleeStepAwayFrom on a real GameWorld + MovementSystem (a fresh
// GameWorld is one open, walkable, neighborless 16x12 map, so a step off an edge is a hard wall and
// stepping onto an interior tile always succeeds).  It reflects the private method for the same
// reason the acquisition suite does: the method is internal detail and the assembly has no
// InternalsVisibleTo.  Position is reset each trial; PerpAwayDir's Random.Shared drives the spread,
// so over many trials an unbiased tie-break converges to ~50/50 and a fixed one degenerates to
// 100/0 - which is exactly the regression this asserts against.
[TestFixture]
public class FleeDirectionBiasTests
{
    const int Map = 1;
    const int Slot = 1;
    const int NpcNum = 1;
    const int Trials = 6000;
    const int MinPerSide = Trials * 45 / 100;   // each direction must take >=45% of steps (expected ~50%)

    [Test]
    public void FleeSidestep_SameColumnThreat_IsLeftRightSymmetric()
    {
        // Pinned at the BOTTOM wall with the threat due NORTH (same column): away (Down) is
        // wall-blocked, so the retreat must sidestep along the x-axis.  A fixed tie-break always picks
        // Right here - the "everything ends up on the right side" bias.
        var r = RunSidestepTrials(npcX: 8, npcY: Constants.MaxMapY, tgtX: 8, tgtY: Constants.MaxMapY - 2);

        Assert.That(r.Moved, Is.EqualTo(Trials), "every same-column trial should produce a sidestep");
        Assert.That(r.Up + r.Down, Is.Zero, "a same-column pin must sidestep horizontally, never vertically");
        Assert.That(Math.Min(r.Left, r.Right), Is.GreaterThan(MinPerSide),
            $"retreat sidestep is left/right biased: left={r.Left} right={r.Right} (expected ~50/50)");
    }

    [Test]
    public void FleeSidestep_SameRowThreat_IsUpDownSymmetric()
    {
        // Pinned at the RIGHT wall with the threat due WEST (same row): away (Right) is wall-
        // blocked, so the retreat must sidestep along the y-axis.  A fixed tie-break always picks Down
        // here - the "heavy lean toward the bottom" bias.
        var r = RunSidestepTrials(npcX: Constants.MaxMapX, npcY: 6, tgtX: Constants.MaxMapX - 2, tgtY: 6);

        Assert.That(r.Moved, Is.EqualTo(Trials), "every same-row trial should produce a sidestep");
        Assert.That(r.Left + r.Right, Is.Zero, "a same-row pin must sidestep vertically, never horizontally");
        Assert.That(Math.Min(r.Up, r.Down), Is.GreaterThan(MinPerSide),
            $"retreat sidestep is up/down biased: up={r.Up} down={r.Down} (expected ~50/50)");
    }

    // Drives the real retreat decision Trials times from the given wall-pinned, axis-aligned position
    // and tallies which way the NPC actually stepped.
    static Tally RunSidestepTrials(int npcX, int npcY, int tgtX, int tgtY)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var ai = BuildAi(world, pm);

        // TryFleeStepAwayFrom doesn't gate on stats, but the pools give StepNpc a valid NpcRecord to read.
        var npc = world.Npcs[NpcNum];
        npc.Name = "doe";
        npc.Behavior = NpcBehavior.Flee;

        var mn = world.MapNpcs[Map, Slot];
        mn.Num = NpcNum;

        var method = typeof(NpcAiSystem).GetMethod("TryFleeStepAwayFrom",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        // Threat world coords are independent of the (reset-each-trial) NPC position, so compute once.
        var tw = WorldCoordHelper.ToWorldRelative(world.Maps, Map, Map, tgtX, tgtY);

        var t = new Tally();
        for (int i = 0; i < Trials; i++)
        {
            mn.X = npcX;
            mn.Y = npcY;
            var (npcWX, npcWY) = WorldCoordHelper.BuildMapGrid(world.Maps, Map).CenterToWorld(mn.X, mn.Y);
            bool moved = (bool)method.Invoke(ai, new object?[] { Map, Slot, mn, npcWX, npcWY, tw })!;
            if (!moved) continue;
            t.Moved++;
            int dx = mn.X - npcX, dy = mn.Y - npcY;
            if (dx < 0) t.Left++;
            else if (dx > 0) t.Right++;
            else if (dy < 0) t.Up++;
            else if (dy > 0) t.Down++;
        }
        return t;
    }

    sealed class Tally { public int Left, Right, Up, Down, Moved; }

    // A real NpcAiSystem with a real Movement and Spawn and a no-op dispatcher; the sub-system the
    // retreat step never reaches (items) is null - mirrors GuestNativeScenarioParityTests.
    static NpcAiSystem BuildAi(GameWorld world, PlayerManager pm)
    {
        var dispatcher = new NoOpDispatcher();
        var movement = new MovementSystem(world, pm, dispatcher);
        var spawn = new SpawnSystem(world, pm, dispatcher, items: null!);
        return new NpcAiSystem(world, pm, dispatcher, movement, spawn, items: null!);
    }

    // No-op packet dispatcher - the retreat step emits move/dir packets we don't need to observe.
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

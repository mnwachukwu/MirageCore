using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Tests.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.Ai;

/// <summary>
/// An NPC that has closed to melee STAYS closed while its swing recharges.
///
/// <para>The legs pass runs far more often than the brain, and it decides whether to step by asking whether
/// the NPC is in range. Asking <c>CanNpcAttack*</c> instead answers a different question — that gate also
/// carries the attack cooldown, so for the whole second after every hit it says no and the chase reads that
/// as "not there yet". The NPC steps in, the cooldown clears, it swings, it steps again: a shuffle on every
/// beat of a fight that should be a mob standing still and hitting.</para>
///
/// <para>Oversize bodies show it worst. A big NPC is footprint-adjacent from two or three tiles out, so its
/// step aims at a tile its body cannot occupy — it slides along the target instead of settling.</para>
/// </summary>
[TestFixture]
public class NpcMeleeHoldTests
{
    private const int Map = 1;

    // The victim sits on the chaser's leading edge but OFF its anchor axis, which only an oversize body can
    // manage — a one-tile NPC is adjacent only at Manhattan 1, so its anchor always lines up. Off-axis is
    // what makes the blocked step fall through to a best-effort sidestep instead of simply failing.
    [TestCase(1, 8, 4, 8, 3, TestName = "NpcMeleeHold_SizeOne")]
    [TestCase(2, 8, 4, 9, 3, TestName = "NpcMeleeHold_SizeTwo")]
    [TestCase(3, 8, 4, 10, 3, TestName = "NpcMeleeHold_SizeThree")]
    public void InRangeAndRecharging_DoesNotStep(int chaserSize, int chaserX, int chaserY, int victimX, int victimY)
    {
        var (ai, world, chaser, victim) = Engaged(chaserSize, chaserX, chaserY, victimX, victimY);
        long tick = 1_000_000;
        chaser.AttackTimer = tick;   // just swung — recharging for the whole window below

        for (long now = tick; now <= tick + 700; now += 50)
        {
            chaser.NextMoveMs = 0;   // legs always ready, so only the range test can hold it
            ai.RunMovement(now);
            Assert.That((chaser.X, chaser.Y), Is.EqualTo((chaserX, chaserY)),
                $"a size-{chaserSize} NPC in melee range stepped while its swing was recharging (t+{now - tick}ms)");
        }

        Assert.That(chaser.Hp, Is.GreaterThan(0));
        Assert.That(victim.Hp, Is.GreaterThan(0));
    }

    [Test]
    public void OutOfRangeAndRecharging_StillCloses()
    {
        // The control: holding position is the range test's doing, not the legs pass having gone inert.
        var (ai, world, chaser, victim) = Engaged(chaserSize: 1, chaserX: 8, chaserY: 9, victimX: 8, victimY: 3);
        long tick = 1_000_000;
        chaser.AttackTimer = tick;

        for (long now = tick; now <= tick + 700; now += 50)
        {
            chaser.NextMoveMs = 0;
            ai.RunMovement(now);
        }

        Assert.That(chaser.Y, Is.LessThan(9),
            "a recharging NPC that is NOT yet in range must still close on its target");
    }

    // Chaser in slot 1 holding an NPC target in slot 3 at (8,3), on a map somebody is watching.
    private static (NpcAiSystem Ai, GameWorld World, MapNpcRecord Chaser, MapNpcRecord Victim) Engaged(
        int chaserSize, int chaserX, int chaserY, int victimX, int victimY)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var ai = BuildAi(world, pm);

        world.Npcs[1].Behavior = NpcBehavior.AttackOnSight;
        world.Npcs[1].Str = 20;
        world.Npcs[1].Def = 10;
        world.Npcs[1].Int = 0;      // never casts, so the melee path is the only one
        world.Npcs[1].Spd = 20;
        world.Npcs[1].Range = 5;
        world.Npcs[1].Size = chaserSize;
        world.Npcs[2].Behavior = NpcBehavior.Stationary;

        var chaser = world.MapNpcs[Map, 1];
        chaser.Num = 1;
        chaser.X = chaserX;
        chaser.Y = chaserY;
        chaser.Hp = 9999;
        chaser.Sp = 20;
        chaser.NpcTargetSpawnMap = Map;
        chaser.NpcTargetSpawnSlot = 3;
        chaser.HasMadeContact = true;

        var victim = world.MapNpcs[Map, 3];
        victim.Num = 2;
        victim.X = victimX;
        victim.Y = victimY;
        victim.Hp = 9999;

        // An observer far outside the chaser's aggro range, so the map is processed and no player is acquired.
        var sp = pm[5];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Char.Map = Map;
        sp.Char.X = 0;
        sp.Char.Y = 11;
        sp.Char.Level = 1;
        world.MapObservers[Map].Add(5);

        return (ai, world, chaser, victim);
    }

    private static NpcAiSystem BuildAi(GameWorld world, PlayerManager pm)
    {
        var dispatcher = new NoOpDispatcher();
        var blood = new BloodSystem(world, dispatcher);
        var movement = new MovementSystem(world, pm, dispatcher, blood);
        var combat = new CombatSystem(world, pm, dispatcher, items: null!, movement, joinLeave: null!, blood,
            objectives: new ObjectiveSystem(), guilds: null!, guildWar: null!, territory: null!);
        var spawn = new SpawnSystem(world, pm, dispatcher);
        return new NpcAiSystem(world, pm, dispatcher, combat, movement, spawn, items: null!, blood);
    }

    private sealed class NoOpDispatcher : IPacketDispatcher
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

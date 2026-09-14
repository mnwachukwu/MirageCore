using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.Ai;

/// <summary>
/// Pointing a creature at somebody.
///
/// <para>🔴 <b>A record says how a body MOVES, and nothing about why.</b> That is what keeps the
/// vocabulary genre-agnostic, and it is also what leaves a game unable to say the most ordinary thing
/// about a creature: that it fights back. A body authored to amble never notices anybody, so without a
/// way in from outside there is no rule a game can write that makes it turn around.</para>
///
/// <para>These pin the way in, and they pin the half that is easy to get silently wrong: the lock alone
/// moves a body for one tick, and then the brain reads a record with no noticing rule, finds nothing to
/// mind, and takes a wander stride over the top of it.</para>
/// </summary>
[TestFixture]
public class NpcRousingTests
{
    const int Map = 1, Slot = 1, Idx = 1;

    /// <summary>A body authored to AMBLE — the one that has no noticing rule of its own, and the whole
    /// reason this seam exists.</summary>
    [Test]
    public void AnAmblingBodyChasesWhoeverItWasPointedAt()
    {
        var (world, game, _) = Build(NpcBehavior.Wander);

        Assert.That(world.Provoke(Creature, Player), Is.True);

        var mn = game.MapNpcs[Map, Slot];
        Assert.Multiple(() =>
        {
            Assert.That(mn.Target, Is.EqualTo(Idx), "it holds the body it was sent after");
            Assert.That(mn.Roused, Is.True, "and the brain has been told the target is not its own idea");
            Assert.That(mn.RushCommitted, Is.True,
                "a body that was SENT is not deciding whether to be interested, so it runs the approach");
        });
    }

    /// <summary>⚠ <b>The half that fails silently.</b> The legs step toward whatever a body holds, so a
    /// bare lock does move it — and then the brain reads a record with no noticing rule, finds nothing to
    /// mind, and walks it somewhere else. The two passes pull in different directions and the body
    /// twitches on the spot, which reads as the target having got away.
    ///
    /// <para>These two are the same forty ticks with and without the rousing, and the difference between
    /// them is the whole of what the flag does.</para></summary>
    [Test]
    public void ARousedBodyMindsItsTargetInsteadOfAmbling()
    {
        var (world, game, ai) = Build(NpcBehavior.Wander);
        world.Provoke(Creature, Player);

        var mn = game.MapNpcs[Map, Slot];
        for (int tick = 0; tick < 40; tick++) ai.RunForAllMaps(System.Environment.TickCount64);

        Assert.Multiple(() =>
        {
            Assert.That(mn.Target, Is.EqualTo(Idx), "it still holds them");
            Assert.That(mn.WanderStepsLeft, Is.Zero, "and it never started a stroll");
        });
    }

    /// <summary>⚠ Ambling is a one-in-eight roll per idle tick, and a stride can be spent inside the same
    /// call that started it, so this watches for the body LEAVING ITS TILE rather than for a stride
    /// count. Two hundred ticks make never starting one a one-in-a-trillion event.</summary>
    [Test]
    public void AnUnrousedBodyAmblesOverTheTopOfABareLock()
    {
        var (_, game, ai) = Build(NpcBehavior.Wander);
        var mn = game.MapNpcs[Map, Slot];
        mn.Target = Idx;

        bool ambled = false;
        for (int tick = 0; tick < 200 && !ambled; tick++)
        {
            ai.RunForAllMaps(System.Environment.TickCount64);
            ambled = mn.X != 5 || mn.Y != 5;
        }

        Assert.That(ambled, Is.True,
            "the brain has no reason to mind a lock nothing roused it onto");
    }

    /// <summary>A body authored to hold its tile keeps holding it. Rousing overrides the noticing, not
    /// the legs — a statue that walked because a game pointed at somebody would be a record whose one
    /// word stopped meaning anything.</summary>
    [Test]
    public void AStationaryBodyStillHoldsItsTile()
    {
        var (world, game, ai) = Build(NpcBehavior.Stationary);
        var mn = game.MapNpcs[Map, Slot];
        int x = mn.X, y = mn.Y;

        world.Provoke(Creature, Player);
        ai.RunForAllMaps(System.Environment.TickCount64);

        Assert.Multiple(() =>
        {
            Assert.That(mn.Target, Is.EqualTo(Idx), "it minds them");
            Assert.That((mn.X, mn.Y), Is.EqualTo((x, y)), "and it does not walk");
        });
    }

    /// <summary>Letting go hands the body back to its record.</summary>
    [Test]
    public void ForgettingClearsBothTheLockAndTheRousing()
    {
        var (world, game, _) = Build(NpcBehavior.Wander);
        world.Provoke(Creature, Player);

        Assert.That(world.Forget(Creature), Is.True);

        var mn = game.MapNpcs[Map, Slot];
        Assert.Multiple(() =>
        {
            Assert.That(mn.Target, Is.Zero);
            Assert.That(mn.Roused, Is.False);
        });
    }

    /// <summary>⚠ A body pointed at itself would chase its own tile forever, holding a lock nothing can
    /// end, so it is refused rather than accepted and left to stall.</summary>
    [Test]
    public void ACreatureIsNotSentAfterItself()
    {
        var (world, _, _) = Build(NpcBehavior.Wander);

        Assert.That(world.Provoke(Creature, Creature), Is.False);
    }

    [Test]
    public void NobodyIsSentAfterABodyThatIsNotInTheWorld()
    {
        var (world, _, _) = Build(NpcBehavior.Wander);

        Assert.Multiple(() =>
        {
            Assert.That(world.Provoke(Creature, EntityHandle.ForPlayer(9)), Is.False, "no such player");
            Assert.That(world.Provoke(EntityHandle.ForNpc(Map, 40), Player), Is.False, "no such creature");
        });
    }

    /// <summary>A creature sent after another creature, which is the other half of the same verb.</summary>
    [Test]
    public void ACreatureIsSentAfterAnotherCreature()
    {
        var (world, game, _) = Build(NpcBehavior.Wander);
        Place(game, slot: 2, num: 2, x: 9, y: 5);

        Assert.That(world.Provoke(Creature, EntityHandle.ForNpc(Map, 2)), Is.True);

        var mn = game.MapNpcs[Map, Slot];
        Assert.Multiple(() =>
        {
            Assert.That(mn.NpcTargetSpawnSlot, Is.EqualTo(2));
            Assert.That(mn.Target, Is.Zero, "the two locks are exclusive — it is after one body");
            Assert.That(mn.Roused, Is.True);
        });
    }

    /// <summary>🔴 What a rule about a species keys on. Two records may share a name and a name may be
    /// translated, so a rule written against one silently follows an editor rename.</summary>
    [Test]
    public void AKindSaysWhichCreatureABodyIs()
    {
        var (world, game, _) = Build(NpcBehavior.Wander);
        Place(game, slot: 2, num: 7, x: 9, y: 5);

        Assert.Multiple(() =>
        {
            Assert.That(world.KindOf(EntityHandle.ForNpc(Map, 2)), Is.EqualTo(7));
            Assert.That(world.KindOf(Player), Is.Zero, "a player is not a copy of anything");
            Assert.That(world.KindOf(EntityHandle.ForNpc(Map, 40)), Is.Zero, "and neither is nobody");
        });
    }

    /// <summary>The bodies AROUND an event, which a game is never handed and cannot otherwise reach.</summary>
    [Test]
    public void CreaturesNearASquareAreAnsweredNearestFirst()
    {
        var (world, game, _) = Build(NpcBehavior.Wander);
        Place(game, slot: 2, num: 2, x: 8, y: 5);    // three away
        Place(game, slot: 3, num: 3, x: 6, y: 5);    // one away
        Place(game, slot: 4, num: 4, x: 5, y: 11);   // six away

        var near = world.NpcsNear(new WorldPlace(Map, 5, 5), tiles: 4);

        Assert.That(near, Is.EqualTo(new[]
        {
            EntityHandle.ForNpc(Map, Slot),   // standing on the square itself
            EntityHandle.ForNpc(Map, 3),
            EntityHandle.ForNpc(Map, 2),
        }), "nearest first, and the one six tiles out is not close");
    }

    /// <summary>🔴 <b>What an AUTHOR wrote, as against what this one body is doing.</b> A game reads its
    /// own attributes off a body freely and could otherwise learn nothing about the record behind
    /// it.</summary>
    [Test]
    public void ABodyAnswersWhatItsRecordWasAuthoredAs()
    {
        var (world, game, _) = Build(NpcBehavior.Flee);
        game.Npcs[1].Group = 4;
        game.Npcs[1].Range = 9;

        Assert.Multiple(() =>
        {
            Assert.That(world.BehaviorOf(Creature), Is.EqualTo("flee"));
            Assert.That(world.GroupOf(Creature), Is.EqualTo(4));
            Assert.That(world.RangeOf(Creature), Is.EqualTo(9));
            Assert.That(world.BehaviorOf(Player), Is.Empty, "a player was authored as nothing");
        });
    }

    /// <summary>The other half of pointing a body at somebody. Without it a rule cannot tell a creature
    /// already in a fight from one standing idle.</summary>
    [Test]
    public void ABodySaysWhetherItIsAfterAnybody()
    {
        var (world, _, _) = Build(NpcBehavior.Wander);

        Assert.That(world.IsChasing(Creature), Is.False);

        world.Provoke(Creature, Player);
        Assert.That(world.IsChasing(Creature), Is.True);

        world.Forget(Creature);
        Assert.That(world.IsChasing(Creature), Is.False);
    }

    // ── Harness ───────────────────────────────────────────────────────────────

    static EntityHandle Creature => EntityHandle.ForNpc(Map, Slot);
    static EntityHandle Player => EntityHandle.ForPlayer(Idx);

    /// <summary>One open map, one creature of the given behavior standing on (5,5), and one player
    /// standing beside it. Built through <see cref="IWorld"/> rather than through the AI system,
    /// because that interface is the promise a game is written against.</summary>
    static (IWorld World, GameWorld Game, NpcAiSystem Ai) Build(NpcBehavior behavior)
    {
        var game = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var movement = new MovementSystem(game, pm, dispatcher);
        var attributes = new AttributeSystem(game, pm, dispatcher);
        var items = new ItemSystem(game, pm, dispatcher, persistence: null!, bg: null!);
        var decals = new DecalSystem(game, dispatcher);
        var spawn = new SpawnSystem(game, pm, dispatcher, items);
        var deaths = new DeathSystem(game, pm, dispatcher, movement, spawn);
        var ai = new NpcAiSystem(game, pm, dispatcher, movement, spawn, items);

        game.Maps[Map] = new MapRecord(16, 12);
        for (int x = 0; x < 16; x++)
            for (int y = 0; y < 12; y++)
                game.Maps[Map].Tile[x, y] = new TileRecord { Type = TileType.Walkable };

        game.Npcs[1].Behavior = behavior;
        game.Npcs[1].Range = 0;   // it notices nobody on its own, so anything it holds was handed to it
        Place(game, Slot, num: 1, x: 5, y: 5);

        var sp = pm[Idx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Char.Map = Map;
        sp.Char.X = 5;
        sp.Char.Y = 6;

        // A map nobody watches is a map the brain skips, so the player standing on it is what makes
        // these ticks run at all.
        game.MapObservers[Map].Add(Idx);

        var world = new ServerWorld(game, pm, attributes, deaths, movement, items,
                                    joinLeave: null!, decals, ai, guilds: null!, dispatcher);
        return (world, game, ai);
    }

    static void Place(GameWorld game, int slot, int num, int x, int y)
    {
        var mn = game.MapNpcs[Map, slot];
        mn.Num = num;
        mn.X = x;
        mn.Y = y;
    }

    // Nothing here reads what was sent — these pin what a body HOLDS, and the target broadcast is the
    // client's copy of that same fact.
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

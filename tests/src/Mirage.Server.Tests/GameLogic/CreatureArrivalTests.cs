using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.GameLogic;

/// <summary>
/// A creature arrives, and a game gets to say what it is made of.
///
/// <para>🔴 <b>Without this seam a creature has no numbers.</b> Core spawns a body carrying a copy of its
/// template and has never heard of health, of a level, or of what one is worth to kill — so the first
/// rule that reads a creature's health reads a number nobody put there. It is also the only moment a
/// fresh body can be told apart from the one before it, which anything varying per spawn needs:
/// a champion, a night-time boost, a scaled reward.</para>
///
/// <para>And the sweep beside it. <c>NpcsNear</c> answers about a neighborhood, which is the wrong
/// question for a rule that has to reach EVERY body — night falling on a world, a census, a clean-up.
/// A rule like that has no square to measure from.</para>
/// </summary>
[TestFixture]
public class CreatureArrivalTests
{
    private const int Map = 1, Other = 2, Kind = 1, Index = 1;

    private sealed class Watcher(Action<EntityHandle>? also = null) : IWorldObserver
    {
        public string Name => "watcher";

        public List<EntityHandle> Arrived { get; } = [];

        public void OnNpcSpawned(EntityHandle npc)
        {
            Arrived.Add(npc);
            also?.Invoke(npc);
        }
    }

    /// <summary>An observer whose only move is to throw, so the engine's answer to a game's bug is
    /// pinned rather than assumed.</summary>
    private sealed class Faulty : IWorldObserver
    {
        public string Name => "faulty";

        public void OnNpcSpawned(EntityHandle npc) => throw new InvalidOperationException("a game's bug");
    }

    private sealed class Module(params IWorldObserver[] observers) : ICoreModule
    {
        public string Name => "Test";

        public void Configure(ICoreBuilder builder)
        {
            foreach (var observer in observers) builder.AddObserver(observer);
        }
    }

    private sealed class Harness
    {
        public required GameWorld World { get; init; }
        public required SpawnSystem Spawns { get; init; }
        public required ServerWorld Seam { get; init; }
    }

    /// <summary>Two open maps, one creature kind, and one player watching the first. The spawn posts
    /// are the caller's to write, because where a body comes from is under test.</summary>
    private static Harness Build(params IWorldObserver[] observers)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var items = new ItemSystem(world, pm, dispatcher, persistence: null!, bg: null!);

        foreach (int mapNum in (int[])[Map, Other])
        {
            world.Maps[mapNum] = new MapRecord(16, 12);
            for (int x = 0; x < 16; x++)
                for (int y = 0; y < 12; y++)
                    world.Maps[mapNum].Tile[x, y] = new TileRecord { Type = TileType.Walkable };
        }

        world.Npcs[Kind].Name = "Bandit";
        world.Npcs[Kind].SpawnSecs = 30;

        var sp = pm[Index];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Char.Name = "Matt";
        sp.Char.Map = Map;
        sp.Char.X = 5;
        sp.Char.Y = 6;
        world.MapObservers[Map].Add(Index);

        var events = new WorldEvents(CoreRegistry.Build([new Module(observers)]));
        var spawns = new SpawnSystem(world, pm, dispatcher, items, rng: null, loot: null, events: events);
        var seam = new ServerWorld(world, pm, new AttributeSystem(world, pm, dispatcher),
                                   deaths: null!, movement: null!, items: items, joinLeave: null!,
                                   decals: null!, ai: null!, guilds: null!, dispatcher: dispatcher,
                                   spawns: spawns);

        return new Harness { World = world, Spawns = spawns, Seam = seam };
    }

    /// <summary>One authored post, pinned so the random search cannot put the body somewhere else.</summary>
    private static void Post(GameWorld world, int mapNum, int x, int y) =>
        world.Maps[mapNum].Npcs.Add(new MapNpcEntry(Kind, x, y));

    // ── The arrival ───────────────────────────────────────────────────────────

    [Test]
    public void AGameIsToldWhenACreatureArrives()
    {
        var watcher = new Watcher();
        var h = Build(watcher);
        Post(h.World, Map, 5, 5);

        h.Spawns.SpawnNpc(1, Map);

        Assert.That(watcher.Arrived, Is.EqualTo(new[] { EntityHandle.ForNpc(Map, 1) }));
    }

    /// <summary>The body is on its tile before the handler runs, so a rule that reads where it is gets an
    /// answer rather than nowhere.</summary>
    [Test]
    public void TheBodyIsStandingSomewhereByThen()
    {
        WorldPlace seen = WorldPlace.Nowhere;
        Harness? h = null;
        var watcher = new Watcher(npc => seen = h!.Seam.PlaceOf(npc));
        h = Build(watcher);
        Post(h.World, Map, 7, 3);

        h.Spawns.SpawnNpc(1, Map);

        Assert.That(seen, Is.EqualTo(new WorldPlace(Map, 7, 3)));
    }

    /// <summary>The seam exists so a handler can write a creature's numbers, so the write has to
    /// reach the body and still be on it afterwards.</summary>
    [Test]
    public void AGameCanWriteACreaturesNumbersFromIt()
    {
        Harness? h = null;
        var watcher = new Watcher(npc => h!.Seam.SetAttribute(npc, "hp", AttributeValue.From(42L)));
        h = Build(watcher);
        Post(h.World, Map, 4, 4);

        h.Spawns.SpawnNpc(1, Map);

        var bag = h.Seam.AttributesOf(EntityHandle.ForNpc(Map, 1));
        Assert.That(bag, Is.Not.Null);
        Assert.That(bag!.TryGet("hp", out var held), Is.True);
        Assert.That(held.AsLong(), Is.EqualTo(42L));
    }

    /// <summary>A body respawning is an arrival like any other, which a per-spawn rule depends
    /// on: the second wolf is a fresh roll, not the first one continuing.</summary>
    [Test]
    public void ARespawnIsAnArrivalToo()
    {
        var watcher = new Watcher();
        var h = Build(watcher);
        Post(h.World, Map, 5, 5);

        h.Spawns.SpawnNpc(1, Map);
        h.Spawns.SpawnNpc(1, Map);

        Assert.That(watcher.Arrived, Has.Count.EqualTo(2));
    }

    /// <summary>An emptied map takes no arrivals, so nothing is announced for one either.</summary>
    [Test]
    public void AnEmptiedMapAnnouncesNothing()
    {
        var watcher = new Watcher();
        var h = Build(watcher);
        Post(h.World, Map, 5, 5);

        h.Spawns.Empty(Map);
        h.Spawns.SpawnNpc(1, Map);

        Assert.That(watcher.Arrived, Is.Empty);
    }

    /// <summary>A post with nothing authored on it spawns nothing, so it announces nothing.</summary>
    [Test]
    public void AnEmptyPostAnnouncesNothing()
    {
        var watcher = new Watcher();
        var h = Build(watcher);

        h.Spawns.SpawnNpc(1, Map);

        Assert.That(watcher.Arrived, Is.Empty);
    }

    /// <summary>A game's bug is a game's bug. The creature is in the world either way, and the observer
    /// after it still runs.</summary>
    [Test]
    public void AHandlerThatThrowsDoesNotStopTheSpawn()
    {
        var watcher = new Watcher();
        var h = Build(new Faulty(), watcher);
        Post(h.World, Map, 5, 5);

        h.Spawns.SpawnNpc(1, Map);

        Assert.Multiple(() =>
        {
            Assert.That(h.World.MapNpcs[Map, 1].Num, Is.EqualTo(Kind), "the body arrived anyway");
            Assert.That(watcher.Arrived, Has.Count.EqualTo(1), "and the one after it still ran");
        });
    }

    // ── The sweep ─────────────────────────────────────────────────────────────

    [Test]
    public void EveryCreatureOnAMapIsAnswered()
    {
        var h = Build();
        Post(h.World, Map, 2, 2);
        Post(h.World, Map, 9, 9);
        Post(h.World, Other, 3, 3);

        h.Spawns.SpawnNpc(1, Map);
        h.Spawns.SpawnNpc(2, Map);
        h.Spawns.SpawnNpc(1, Other);

        Assert.Multiple(() =>
        {
            Assert.That(h.Seam.NpcsOn(Map), Is.EquivalentTo(new[]
            {
                EntityHandle.ForNpc(Map, 1), EntityHandle.ForNpc(Map, 2),
            }));
            Assert.That(h.Seam.NpcsOn(Other), Is.EquivalentTo(new[] { EntityHandle.ForNpc(Other, 1) }));
        });
    }

    /// <summary>Distance does not come into it, which separates this from the neighborhood
    /// question: a body in the far corner is as much on the map as one standing next to you.</summary>
    [Test]
    public void DistanceIsBesideThePoint()
    {
        var h = Build();
        Post(h.World, Map, 0, 0);
        Post(h.World, Map, 15, 11);

        h.Spawns.SpawnNpc(1, Map);
        h.Spawns.SpawnNpc(2, Map);

        Assert.Multiple(() =>
        {
            Assert.That(h.Seam.NpcsOn(Map), Has.Count.EqualTo(2));
            Assert.That(h.Seam.NpcsNear(new WorldPlace(Map, 0, 0), 3), Has.Count.EqualTo(1),
                "the neighborhood question still answers about a neighborhood");
        });
    }

    /// <summary>A body away chasing on another map is answered for where it IS, so a walk of every map
    /// hands each creature back exactly once.</summary>
    [Test]
    public void ABodyAwayChasingIsCountedWhereItStands()
    {
        var h = Build();
        Post(h.World, Map, 2, 2);
        h.Spawns.SpawnNpc(1, Map);

        h.World.MapNpcs[Map, 1].IsReservedSlot = true;
        h.World.MapTraversalNpcs[Other].Add(new TraversalNpcRecord
        {
            Num = Kind,
            X = 4,
            Y = 4,
            SpawnMapNum = Map,
            SpawnSlot = 1,
        });

        Assert.Multiple(() =>
        {
            Assert.That(h.Seam.NpcsOn(Map), Is.Empty, "it is not standing here any more");
            Assert.That(h.Seam.NpcsOn(Other), Is.EqualTo(new[] { EntityHandle.ForNpc(Map, 1) }),
                "and it keeps the name its spawn post gave it");
        });
    }

    [Test]
    public void AMapThatIsNotThereAnswersWithNothing()
    {
        var h = Build();

        Assert.Multiple(() =>
        {
            Assert.That(h.Seam.NpcsOn(0), Is.Empty);
            Assert.That(h.Seam.NpcsOn(-1), Is.Empty);
            Assert.That(h.Seam.NpcsOn(int.MaxValue), Is.Empty);
        });
    }

    [Test]
    public void AnEmptyMapAnswersWithNothing()
    {
        var h = Build();

        Assert.That(h.Seam.NpcsOn(Other), Is.Empty);
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

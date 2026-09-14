using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Reflection;

namespace Mirage.Server.Tests.GameLogic;

/// <summary>
/// What the engine actually tells a game.
///
/// <para>An observer that is declared and never called is worse than no seam at all: a game built on it
/// does nothing, with no error to follow. So these drive the real systems and assert the event came out
/// the other side, rather than calling the raiser directly.</para>
/// </summary>
[TestFixture]
public class WorldEventTests
{
    const int Idx = 1, Map = 1;

    /// <summary>Records everything it is told, in order.</summary>
    private sealed class Watcher : IWorldObserver
    {
        public string Name => "Watcher";
        public readonly List<string> Seen = [];

        public void OnPlayerJoined(EntityHandle who) => Seen.Add($"joined {who.PlayerIndex}");
        public void OnPlayerLeft(EntityHandle who) => Seen.Add($"left {who.PlayerIndex}");
        public void OnPlayerMoved(EntityHandle who, in WorldPlace from, in WorldPlace to)
            => Seen.Add($"moved {from} -> {to}");
        public void OnPlayerWarped(EntityHandle who, in WorldPlace from, in WorldPlace to)
            => Seen.Add($"warped {from} -> {to}");
        public void OnContact(EntityHandle npc, EntityHandle target) => Seen.Add($"contact {npc} -> {target}");
        public void OnItemUsed(EntityHandle who, int itemNum, int invSlot)
            => Seen.Add($"used {itemNum} from {invSlot}");
    }

    private sealed class Thrower : IWorldObserver
    {
        public string Name => "Thrower";
        public void OnPlayerMoved(EntityHandle who, in WorldPlace from, in WorldPlace to)
            => throw new InvalidOperationException("a game's bug");
    }

    private sealed class Module(params IWorldObserver[] observers) : ICoreModule
    {
        public string Name => "Test";
        public void Configure(ICoreBuilder builder)
        {
            foreach (var observer in observers) builder.AddObserver(observer);
        }
    }

    private static WorldEvents EventsFor(params IWorldObserver[] observers)
        => new(CoreRegistry.Build([new Module(observers)]));

    // A walkable 1..8 square on map 1, which is all these need underfoot.
    private static (GameWorld World, PlayerManager Pm, MovementSystem Move, PlayerRecord P) Walkable(WorldEvents events)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var move = new MovementSystem(world, pm, new NoOpDispatcher(), events: events);

        world.Maps[Map] = new MapRecord(16, 12);
        for (int x = 0; x < 16; x++)
            for (int y = 0; y < 12; y++)
                world.Maps[Map].Tile[x, y] = new TileRecord { Type = TileType.Walkable };

        var sp = pm[Idx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Char.Map = Map;
        sp.Char.X = 5;
        sp.Char.Y = 5;
        return (world, pm, move, sp.Char);
    }

    // ── A step ────────────────────────────────────────────────────────────────

    [Test]
    public void AStep_IsReportedWithWhereItCameFromAndWentTo()
    {
        var watcher = new Watcher();
        var (_, _, move, _) = Walkable(EventsFor(watcher));

        move.PlayerMove(Idx, Direction.Down, MovementType.Walking);

        Assert.That(watcher.Seen.Single(), Is.EqualTo("moved map 1 (5,5) -> map 1 (5,6)"));
    }

    /// <summary>A refused step is not a step. A wall would otherwise report a move to the tile the player
    /// is already standing on, and a game counting steps would count them.</summary>
    [Test]
    public void ARefusedStep_IsNotReported()
    {
        var watcher = new Watcher();
        var (world, _, move, p) = Walkable(EventsFor(watcher));
        world.Maps[Map].Tile[p.X, p.Y + 1] = new TileRecord { Type = TileType.Blocked };

        move.PlayerMove(Idx, Direction.Down, MovementType.Walking);

        Assert.That(watcher.Seen, Is.Empty);
    }

    [Test]
    public void BeingPutSomewhere_IsAWarpRatherThanAStep()
    {
        var watcher = new Watcher();
        var (_, _, move, _) = Walkable(EventsFor(watcher));

        move.PlayerWarp(Idx, Map, 2, 3);

        Assert.That(watcher.Seen.Single(), Is.EqualTo("warped map 1 (5,5) -> map 1 (2,3)"));
    }

    // ── When nobody is listening, and when a listener breaks ──────────────────

    /// <summary>The busiest event runs on every step every player takes, so the cost of a seam nobody
    /// uses has to be nothing at all.</summary>
    [Test]
    public void WithNoObservers_NothingIsRaisedAndNothingBreaks()
    {
        var (_, _, move, p) = Walkable(WorldEvents.None);

        Assert.Multiple(() =>
        {
            Assert.That(WorldEvents.None.Any, Is.False);
            Assert.DoesNotThrow(() => move.PlayerMove(Idx, Direction.Down, MovementType.Walking));
            Assert.That(p.Y, Is.EqualTo(6), "and the step still happened");
        });
    }

    /// <summary>A game's bug is a game's bug. It is logged with the observer's name and the world carries
    /// on — the same answer the loop gives a module's tick work that throws.</summary>
    [Test]
    public void AnObserverThatThrows_DoesNotStopTheStepOrTheOthers()
    {
        var watcher = new Watcher();
        var (_, _, move, p) = Walkable(EventsFor(new Thrower(), watcher));

        Assert.DoesNotThrow(() => move.PlayerMove(Idx, Direction.Down, MovementType.Walking));

        Assert.Multiple(() =>
        {
            Assert.That(p.Y, Is.EqualTo(6), "the step happened");
            Assert.That(watcher.Seen, Has.Count.EqualTo(1), "and the observer behind it was still told");
        });
    }

    // ── Using something ───────────────────────────────────────────────────────

    [Test]
    public void UsingAnItem_IsReportedWithWhatAndFromWhere()
    {
        var watcher = new Watcher();
        var world = new GameWorld();
        var pm = new PlayerManager();
        var items = new ItemSystem(world, pm, new NoOpDispatcher(), persistence: null!, bg: null!,
                                   events: EventsFor(watcher));

        var sp = pm[Idx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Char.Map = Map;
        world.Items[7].Type = ItemType.Consumable;
        items.GiveItem(Idx, 7, 0);

        items.UseItem(Idx, 1);

        Assert.That(watcher.Seen.Single(), Is.EqualTo("used 7 from 1"));
    }

    // ── A pursuer arrives ────────────────────────────────────────────────────

    /// <summary>A chaser standing next to the player it is holding, one tick from being in reach.</summary>
    private static (NpcAiSystem Ai, MapNpcRecord Chaser) Chasing(WorldEvents events)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var movement = new MovementSystem(world, pm, dispatcher);
        var spawn = new SpawnSystem(world, pm, dispatcher, items: null!);
        var ai = new NpcAiSystem(world, pm, dispatcher, movement, spawn, items: null!, events: events);

        world.Maps[Map] = new MapRecord(16, 12);
        world.Npcs[1].Behavior = NpcBehavior.Pursue;
        world.Npcs[1].Range = 5;

        var sp = pm[Idx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Char.Map = Map;
        sp.Char.X = 8;
        sp.Char.Y = 5;
        world.MapObservers[Map].Add(Idx);

        var chaser = world.MapNpcs[Map, 1];
        chaser.Num = 1;
        chaser.X = 8;
        chaser.Y = 6;
        chaser.Target = Idx;
        chaser.NextMoveMs = 0;
        return (ai, chaser);
    }

    /// <summary>Core chases and arrives and has nothing to do next. This is the event a game's answer to
    /// "and then what" hangs off, so it has to actually come out.</summary>
    [Test]
    public void APursuerReachingItsTarget_IsReported()
    {
        var watcher = new Watcher();
        var (ai, _) = Chasing(EventsFor(watcher));

        ai.RunMovement(1_000_000);

        Assert.That(watcher.Seen.Single(), Is.EqualTo("contact npc:1/1 -> player:1"));
    }

    /// <summary>Contact is made once per engagement. Raised every tick it is HELD, a game would open a
    /// battle screen five times a second for as long as the mob stood there.</summary>
    [Test]
    public void HoldingContact_IsNotReportedAgain()
    {
        var watcher = new Watcher();
        var (ai, chaser) = Chasing(EventsFor(watcher));

        ai.RunMovement(1_000_000);
        chaser.NextMoveMs = 0;
        ai.RunMovement(1_000_500);

        Assert.Multiple(() =>
        {
            Assert.That(chaser.HasMadeContact, Is.True, "precondition: it is still in reach");
            Assert.That(watcher.Seen, Has.Count.EqualTo(1));
        });
    }

    // ── Every event has a raiser, and every raiser has a caller ─────────────────────────

    private static string RepoRoot()
    {
        string root = typeof(WorldEventTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!;
        Assert.That(Directory.Exists(root), Is.True, $"Repository root not found: {root}");
        return root;
    }

    /// <summary>A declared event nothing raises is the failure this whole seam has to avoid: a game built
    /// on it does nothing at all, with no error to follow. Derived from the interface rather than a list,
    /// so an event added to <see cref="IWorldObserver"/> is covered the moment it exists.</summary>
    [Test]
    public void EveryObserverMethod_HasARaiserOnWorldEvents()
    {
        var raisers = typeof(WorldEvents).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

        string[] unraised = [.. typeof(IWorldObserver).GetMethods()
            .Where(m => m.Name.StartsWith("On", StringComparison.Ordinal))
            .Select(m => m.Name[2..])
            .Where(name => !raisers.Contains(name))];

        Assert.That(unraised, Is.Empty,
            "these events can never happen, because nothing on WorldEvents raises them: "
            + string.Join(", ", unraised));
    }

    /// <summary>And a raiser nothing calls is the same failure one step later. Read out of the source
    /// because that is where the answer is: a call site is not something the type system records.</summary>
    [Test]
    public void EveryRaiser_IsCalledFromASystem()
    {
        string core = Path.Combine(RepoRoot(), "server", "src", "Mirage.Server.Core");
        string[] sources = [.. Directory.EnumerateFiles(core, "*.cs", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).Equals("WorldEvents.cs", StringComparison.Ordinal))
            .Select(File.ReadAllText)];

        string[] uncalled = [.. typeof(WorldEvents).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName && m.DeclaringType == typeof(WorldEvents))
            .Select(m => m.Name)
            .Where(name => !sources.Any(src => src.Contains($"_events.{name}(", StringComparison.Ordinal)))];

        Assert.That(uncalled, Is.Empty,
            "nothing in the engine raises these, so no game can ever be told about them: "
            + string.Join(", ", uncalled));
    }

    // ── Harness ──────────────────────────────────────────────────────────────

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

using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Host.Scripting;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.Modules;

/// <summary>
/// MSR's rules against a REAL world, rather than a double that says yes.
///
/// <para>🔴 <b>Every other test of these scripts runs them against a recording world, and that world
/// answers yes to everything.</b> Its Kill returns true for any handle, so a rule that calls Kill looks
/// like it worked whatever the engine would really have done — which is exactly the shape that hid the
/// creature-death hole, and exactly why a player could walk around at nought health with every suite
/// green.</para>
///
/// <para>So this loads the shipped scripts, hands them a real <see cref="ServerWorld"/> over a real
/// <see cref="GameWorld"/>, and registers them as the real death policy. What it asks is whether the
/// game and the engine agree when they are actually wired to each other.</para>
/// </summary>
[TestFixture]
public class MsrOnARealWorldTests
{
    private const int Home = 1, Away = 2, Index = 1;
    private string _dir = "";

    [SetUp]
    public void SetUp() => _dir = Directory.CreateTempSubdirectory("mirage-msr-real-").FullName;

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private sealed class Wired
    {
        public required ScriptedWorldModule Module { get; init; }
        public required ServerWorld Seam { get; init; }
        public required GameWorld World { get; init; }
        public required PlayerManager Pm { get; init; }
        public required DeathSystem Deaths { get; init; }
        public PlayerRecord Player => Pm[Index].Char;
    }

    /// <summary>The shipped scripts, loaded the way a server loads them, over a world that behaves like
    /// one. Two walkable maps: the one they stand on, and the one the map author boots them to.</summary>
    private Wired Load()
    {
        string worldDir = Path.Combine(_dir, Guid.NewGuid().ToString("N"));
        string scripts = Path.Combine(worldDir, ScriptedWorldModule.ScriptsFolder);
        Directory.CreateDirectory(scripts);

        string from = Path.Combine(Repository(), "modules", "msr", "world", "scripts");
        foreach (string file in Directory.EnumerateFiles(from, "*.cm", SearchOption.AllDirectories))
        {
            string landing = Path.Combine(scripts, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(landing)!);
            File.Copy(file, landing);
        }

        var module = new ScriptedWorldModule(worldDir);
        var registry = CoreRegistry.Build(module);

        var world = new GameWorld { Attributes = registry.Attributes };
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();

        foreach (int mapNum in (int[])[Home, Away])
        {
            world.Maps[mapNum] = new MapRecord(16, 12);
            for (int x = 0; x < 16; x++)
                for (int y = 0; y < 12; y++)
                    world.Maps[mapNum].Tile[x, y] = new TileRecord { Type = TileType.Walkable };
        }

        // The map they fall on boots to the other one, which is the first thing Death.SendHome reads.
        world.Maps[Away].ExitMap = Home;
        world.Maps[Away].ExitX = 3;
        world.Maps[Away].ExitY = 3;

        var movement = new MovementSystem(world, pm, dispatcher);
        var items = new ItemSystem(world, pm, dispatcher, persistence: null!, bg: null!);
        var spawns = new SpawnSystem(world, pm, dispatcher, items);
        var deaths = new DeathSystem(world, pm, dispatcher, movement, spawns, registry.DeathPolicies);
        var attributes = new AttributeSystem(world, pm, dispatcher);

        var sp = pm[Index];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Char.Name = "Matt";
        sp.Char.Map = Away;
        sp.Char.X = 9;
        sp.Char.Y = 9;
        world.MapObservers[Away].Add(Index);

        var seam = new ServerWorld(world, pm, attributes, deaths, movement, items,
                                   joinLeave: null!, decals: null!, ai: null!, guilds: null!,
                                   dispatcher: dispatcher, spawns: spawns);
        module.Start(seam);

        Assert.That(module.Problems.Where(p => p.Severity == Mirage.Scripting.ScriptSeverity.Error)
                          .Select(p => p.Message), Is.Empty, "the shipped scripts have to load");

        return new Wired { Module = module, Seam = seam, World = world, Pm = pm, Deaths = deaths };
    }

    private static string Repository()
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        while (here is not null && !File.Exists(Path.Combine(here.FullName, "Mirage.slnx"))) here = here.Parent;
        return here?.FullName ?? throw new InvalidOperationException("The repository root is not above here.");
    }

    private static EntityHandle Who => EntityHandle.ForPlayer(Index);

    private long Held(Wired w, string key) =>
        w.Seam.AttributesOf(Who) is { } bag && bag.TryGet(key, out var v) ? v.AsLong() : -1;

    // ── Dying, against the engine that owns it ────────────────────────────────

    /// <summary>The whole chain: the rules allow it, the rules are asked what it costs, and the engine
    /// moves the body. Nothing here is a double.</summary>
    [Test]
    public void APlayerActuallyDies()
    {
        var w = Load();
        ((IWorldObserver)w.Module).OnPlayerJoined(Who);

        bool died = w.Seam.Kill(Who, EntityHandle.None, "slain");

        Assert.That(died, Is.True, "the rules refused a death the game itself asked for");
    }

    /// <summary>🔴 The symptom that started this: a body at nought health, still standing, still being
    /// hit. Health and action go together — at nothing they are out of action, and by the time they can
    /// act again they are full. Neither half alone rules out the state that started this.</summary>
    [Test]
    public void TheyAreNeverBothEmptyAndAbleToAct()
    {
        var w = Load();
        ((IWorldObserver)w.Module).OnPlayerJoined(Who);
        w.Seam.SetAttribute(Who, "hp", AttributeValue.From(0L));

        w.Seam.Kill(Who, EntityHandle.None, "slain");

        Assert.That(w.Seam.IsDowned(Who), Is.True, "empty and still walking around");

        w.Player.RespawnReadyUtc = 0;
        w.Deaths.Rise(Who);

        Assert.Multiple(() =>
        {
            Assert.That(w.Seam.IsDowned(Who), Is.False, "still out of action after getting up");
            Assert.That(Held(w, "hp"), Is.EqualTo(Held(w, "maxhp")), "acting again on nothing");
        });
    }

    /// <summary>The body is put out of action while it waits, which is the state that stops a corpse
    /// walking, shopping and being shot at. Without it the player is simply alive again somewhere
    /// else.</summary>
    [Test]
    public void TheBodyIsPutOutOfAction()
    {
        var w = Load();
        ((IWorldObserver)w.Module).OnPlayerJoined(Who);

        w.Seam.Kill(Who, EntityHandle.None, "slain");

        Assert.That(w.Seam.IsDowned(Who), Is.True);
    }

    /// <summary>The corpse lies where it FELL, as the original's does. It moves when it gets up, which
    /// is the whole reason there is a wait to look at.</summary>
    [Test]
    public void TheBodyStaysWhereItFellUntilItGetsUp()
    {
        var w = Load();
        ((IWorldObserver)w.Module).OnPlayerJoined(Who);

        w.Seam.Kill(Who, EntityHandle.None, "slain");

        Assert.That(w.Player.Map, Is.EqualTo(Away), "it was moved before anybody asked to get up");
    }

    /// <summary>🔴 The way out, which did not exist. Core had the packet, the allow-list entry, the
    /// client sender and the panel - and no handler, nothing that ever cleared the state, and nothing
    /// that told the client it was in one. A body could go down and never get up.</summary>
    [Test]
    public void TheyGetUpWhenTheDeadlineHasPassed()
    {
        var w = Load();
        ((IWorldObserver)w.Module).OnPlayerJoined(Who);
        w.Seam.Kill(Who, EntityHandle.None, "slain");

        Assert.That(w.Seam.IsDowned(Who), Is.True, "they went down");

        // Wind the deadline back rather than waiting it out.
        w.Player.RespawnReadyUtc = 0;

        Assert.Multiple(() =>
        {
            Assert.That(w.Deaths.Rise(Who), Is.True, "the ask was refused");
            Assert.That(w.Seam.IsDowned(Who), Is.False, "they are still out of action");
            Assert.That(w.Player.Map, Is.EqualTo(Home), "and were not moved to where the map sends them");
        });
    }

    /// <summary>Asking early is ignored. The deadline is the server's, so a client that asks before it
    /// is up gets nothing rather than being trusted.</summary>
    [Test]
    public void AskingBeforeTheDeadlineIsRefused()
    {
        var w = Load();
        ((IWorldObserver)w.Module).OnPlayerJoined(Who);
        w.Seam.Kill(Who, EntityHandle.None, "slain");
        w.Player.RespawnReadyUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 600;

        Assert.Multiple(() =>
        {
            Assert.That(w.Deaths.Rise(Who), Is.False);
            Assert.That(w.Seam.IsDowned(Who), Is.True, "and they stay down");
        });
    }

    /// <summary>Getting up is when a game puts things back, so the pools are full at the end of it and
    /// not before. Nothing is restored while the body is still lying there.</summary>
    [Test]
    public void RisingIsWhenTheGameRestoresThem()
    {
        var w = Load();
        ((IWorldObserver)w.Module).OnPlayerJoined(Who);
        w.Seam.SetAttribute(Who, "hp", AttributeValue.From(0L));
        w.Seam.Kill(Who, EntityHandle.None, "slain");

        Assert.That(Held(w, "hp"), Is.Zero, "a body on the floor was handed a full bar");

        w.Player.RespawnReadyUtc = 0;
        w.Deaths.Rise(Who);

        Assert.That(Held(w, "hp"), Is.EqualTo(Held(w, "maxhp")), "they got up on nothing");
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

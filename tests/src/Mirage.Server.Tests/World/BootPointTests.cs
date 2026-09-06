using Mirage.Server.Core.Configuration;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.World;

/// <summary>
/// A map's boot point: where it puts a player who leaves it other than by walking off it — by dying, or by
/// logging out.
///
/// <para>It OUTRANKS the player's own Inn-purchased respawn point. What dying in a place costs is the map
/// author's call, and a player cannot buy their way out of it by setting a spawn somewhere friendlier.
/// A guild-war death still wins over both: a war has its own rules about where the fallen come back.</para>
/// </summary>
[TestFixture]
public class BootPointTests
{
    private const int Died = 3, Boot = 9, Inn = 5, Group = 2;
    private const int Idx = 1;

    private static readonly SpawnConfig Spawn = new() { Map = 1, X = 8, Y = 6 };

    private static (GameWorld world, PlayerManager pm, CombatSystem combat, PlayerRecord p) Setup()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var blood = new BloodSystem(world, dispatcher);
        var movement = new MovementSystem(world, pm, dispatcher, blood);
        // RespawnPlayer reads only world / pm / movement / dispatcher / config; the rest of the combat
        // dependency graph is untouched on this path.
        var combat = new CombatSystem(world, pm, dispatcher, items: null!, movement, joinLeave: null!, blood,
                                      objectives: new ObjectiveSystem(), guilds: null!, guildWar: null!,
                                      territory: null!, config: new ServerConfig { Spawn = Spawn });

        var sp = pm[Idx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        var p = sp.Char;
        p.Map = Died;
        p.X = 2;
        p.Y = 2;
        p.MaxHp = 100;
        p.Dead = true;
        world.MapObservers[Died].Add(Idx);
        return (world, pm, combat, p);
    }

    private static void GiveBootPoint(GameWorld world, int onMap, int map, int x, int y)
    {
        world.Maps[onMap].BootMap = map;
        world.Maps[onMap].BootX = x;
        world.Maps[onMap].BootY = y;
    }

    private static void GiveInnPoint(PlayerRecord p)
    {
        p.SpawnMap = Inn;
        p.SpawnX = 7;
        p.SpawnY = 7;
    }

    // ── Precedence ────────────────────────────────────────────────────────────

    [Test]
    public void DyingOnAMapWithABootPoint_SendsThemThere()
    {
        var (world, _, combat, p) = Setup();
        GiveBootPoint(world, Died, Boot, 4, 6);

        combat.RespawnPlayer(Idx);

        Assert.That((p.Map, p.X, p.Y), Is.EqualTo((Boot, 4, 6)));
    }

    /// <summary>The case the whole rule exists for: a player who bought a respawn point still pays the
    /// map's price for dying on it.</summary>
    [Test]
    public void ABootPoint_BeatsAPurchasedInnPoint()
    {
        var (world, _, combat, p) = Setup();
        GiveBootPoint(world, Died, Boot, 4, 6);
        GiveInnPoint(p);

        combat.RespawnPlayer(Idx);

        Assert.That((p.Map, p.X, p.Y), Is.EqualTo((Boot, 4, 6)));
    }

    [Test]
    public void WithNoBootPoint_APurchasedInnPointIsUsed()
    {
        var (_, _, combat, p) = Setup();
        GiveInnPoint(p);

        combat.RespawnPlayer(Idx);

        Assert.That((p.Map, p.X, p.Y), Is.EqualTo((Inn, 7, 7)));
    }

    [Test]
    public void WithNeither_TheServerSpawnIsUsed()
    {
        var (_, _, combat, p) = Setup();

        combat.RespawnPlayer(Idx);

        Assert.That((p.Map, p.X, p.Y), Is.EqualTo((Spawn.Map, Spawn.X, Spawn.Y)));
    }

    /// <summary>A war death answers to the war, not to the map it happened on.</summary>
    [Test]
    public void AWarDeath_OutranksTheBootPoint()
    {
        var (world, _, combat, p) = Setup();
        GiveBootPoint(world, Died, Boot, 4, 6);
        p.DiedInWar = true;

        combat.RespawnPlayer(Idx);

        Assert.That((p.Map, p.X, p.Y), Is.EqualTo((Died, 2, 2)), "a grudge war returns them to the tile they fell on");
    }

    // ── Inheritance ───────────────────────────────────────────────────────────

    /// <summary>One dungeon declares one exit on its group, and every map in it boots there.</summary>
    [Test]
    public void ABootPointIsInheritedFromTheMapGroup()
    {
        var (world, _, combat, p) = Setup();
        world.Maps[Died].MapGroup = Group;
        world.MapGroups[Group] = new MapGroupRecord { BootMap = Boot, BootX = 4, BootY = 6 };

        combat.RespawnPlayer(Idx);

        Assert.That((p.Map, p.X, p.Y), Is.EqualTo((Boot, 4, 6)));
    }

    /// <summary>The three fields travel as a set keyed on BootMap, so a map that names its own destination
    /// takes its own coordinates with it and never mixes in the group's.</summary>
    [Test]
    public void AMapsOwnBootPoint_OverridesItsGroupsWhole()
    {
        var (world, _, combat, p) = Setup();
        world.Maps[Died].MapGroup = Group;
        world.MapGroups[Group] = new MapGroupRecord { BootMap = Inn, BootX = 1, BootY = 1 };
        GiveBootPoint(world, Died, Boot, 4, 6);

        combat.RespawnPlayer(Idx);

        Assert.That((p.Map, p.X, p.Y), Is.EqualTo((Boot, 4, 6)));
    }

    // ── A boot point that names no tile ───────────────────────────────────────

    /// <summary>Coming back is the one warp that may never be refused, so a boot point past the edge of its
    /// own map lands them on the nearest real tile instead of throwing or leaving them dead.</summary>
    [Test]
    public void ABootPointPastTheEdge_StillBringsThemBack()
    {
        var (world, _, combat, p) = Setup();
        GiveBootPoint(world, Died, Boot, 999, 999);

        Assert.DoesNotThrow(() => combat.RespawnPlayer(Idx));
        Assert.Multiple(() =>
        {
            Assert.That(p.Dead, Is.False, "they are alive again either way");
            Assert.That((p.Map, p.X, p.Y), Is.EqualTo((Boot, Constants.MaxMapX, Constants.MaxMapY)));
        });
    }

    [Test]
    public void ABootPointOnAMapThatDoesNotExist_FallsBackToTheServerSpawn()
    {
        var (world, _, combat, p) = Setup();
        GiveBootPoint(world, Died, world.Limits.Maps + 1, 4, 6);

        Assert.DoesNotThrow(() => combat.RespawnPlayer(Idx));
        Assert.That((p.Map, p.X, p.Y), Is.EqualTo((Spawn.Map, Spawn.X, Spawn.Y)));
    }

    // ── Harness ───────────────────────────────────────────────────────────────

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

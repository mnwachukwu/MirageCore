using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Tests.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Text.Json;

namespace Mirage.Server.Tests.Ai;

// Dynamic map-NPC list + fixed placement. A map's NPCs are a dense List<MapNpcEntry>: entry
// [i] drives runtime spawn post i+1, and each entry carries the NPC type + an OPTIONAL pin (PinX/PinY). A pinned
// entry always spawns at its tile; an unpinned one spawns at a random walkable tile; a post past the list is
// empty. Covers placement, the index→post mapping, and the entry JSON round-trip (pins included).
[TestFixture]
public class NpcSpawnPlacementTests
{
    const int Map = 1, NpcNum = 1;

    static SpawnSystem NewSpawn(GameWorld world, PlayerManager pm)
        => new(world, pm, new NoOpDispatcher());

    // A minimal huntable NPC type in slot NpcNum. SpawnNpc only needs Name/Behavior + a valid EffectiveSize
    // (Size 0 clamps to 1) and pure Effective* vitals, so nothing else is required for placement assertions.
    static void DefineNpc(GameWorld world)
    {
        var npc = world.Npcs[NpcNum];
        npc.Name = "mob";
        npc.Behavior = NpcBehavior.AttackOnSight;
    }

    [Test]
    public void PinnedEntry_SpawnsAtItsTile()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        DefineNpc(world);
        world.Maps[Map].Npcs.Add(new MapNpcEntry(NpcNum, PinX: 9, PinY: 4));

        NewSpawn(world, pm).SpawnNpc(1, Map);

        var mn = world.MapNpcs[Map, 1];
        Assert.That(mn.Num, Is.EqualTo(NpcNum));
        Assert.That((mn.X, mn.Y), Is.EqualTo((9, 4)));
        Assert.That(mn.Layer, Is.EqualTo(WorldLayer.Ground), "a Ground pin spawns on the ground plane");
    }

    [Test]
    public void PinnedEntry_OnFringeLayer_SpawnsOnTheFringePlane()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        DefineNpc(world);
        // Fringe is walkable by default (uniform upper plane), so a bridge-top pin spawns the NPC up on the Fringe.
        world.Maps[Map].Npcs.Add(new MapNpcEntry(NpcNum, PinX: 9, PinY: 4, PinLayer: WorldLayer.Fringe));

        NewSpawn(world, pm).SpawnNpc(1, Map);

        var mn = world.MapNpcs[Map, 1];
        Assert.Multiple(() =>
        {
            Assert.That((mn.X, mn.Y), Is.EqualTo((9, 4)), "at its pinned tile");
            Assert.That(mn.Layer, Is.EqualTo(WorldLayer.Fringe), "spawned on the pinned FRINGE plane");
        });
    }

    [Test]
    public void PinnedEntry_TileOccupiedByAnotherNpc_StillSpawnsAtPost()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        DefineNpc(world);
        world.Maps[Map].Npcs.Add(new MapNpcEntry(NpcNum, 9, 4));
        // A different live NPC already standing on the post tile. The random path would avoid an occupied tile;
        // a fixed post must not — a passerby can't be allowed to block a placed spawn.
        var blocker = world.MapNpcs[Map, 2];
        blocker.Num = NpcNum;
        blocker.X = 9;
        blocker.Y = 4;

        NewSpawn(world, pm).SpawnNpc(1, Map);

        var mn = world.MapNpcs[Map, 1];
        Assert.That((mn.X, mn.Y), Is.EqualTo((9, 4)));
    }

    [Test]
    public void PinnedEntry_OnBlockedTile_FallsBackToRandomWalkable()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        DefineNpc(world);
        world.Maps[Map].EditTile(9, 4, t => t with { Type = TileType.Blocked });   // authoring error: the pin is on a wall
        world.Maps[Map].Npcs.Add(new MapNpcEntry(NpcNum, 9, 4));

        NewSpawn(world, pm).SpawnNpc(1, Map);

        var mn = world.MapNpcs[Map, 1];
        Assert.That(mn.Num, Is.EqualTo(NpcNum), "still spawned");
        Assert.That((mn.X, mn.Y), Is.Not.EqualTo((9, 4)), "not on the wall");
        Assert.That(world.Maps[Map].Tile[mn.X, mn.Y].Type, Is.EqualTo(TileType.Walkable));
    }

    [Test]
    public void UnpinnedEntry_SpawnsOnWalkableTile()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        DefineNpc(world);
        world.Maps[Map].Npcs.Add(new MapNpcEntry(NpcNum, PinX: null, PinY: null));   // no pin → random spawn

        NewSpawn(world, pm).SpawnNpc(1, Map);

        var mn = world.MapNpcs[Map, 1];
        Assert.That(mn.Num, Is.EqualTo(NpcNum));
        Assert.That(world.Maps[Map].Tile[mn.X, mn.Y].Type, Is.EqualTo(TileType.Walkable));
    }

    // The dense list maps by index: entry [i] drives post i+1, so its pin rides with it — the whole point of
    // folding the pin into the entry (a middle removal shifts later entries to lower posts, pin intact).
    [Test]
    public void SecondEntry_SpawnsAtSecondPostWithItsOwnPin()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        DefineNpc(world);
        world.Maps[Map].Npcs.Add(new MapNpcEntry(NpcNum, null, null));   // post 1, unpinned
        world.Maps[Map].Npcs.Add(new MapNpcEntry(NpcNum, 7, 3));         // post 2, pinned

        NewSpawn(world, pm).SpawnNpc(2, Map);

        var mn = world.MapNpcs[Map, 2];
        Assert.That(mn.Num, Is.EqualTo(NpcNum));
        Assert.That((mn.X, mn.Y), Is.EqualTo((7, 3)), "post 2 reads entries[1]'s pin");
    }

    // A runtime post past the end of the authored list is empty and spawns nothing (no IndexOutOfRange).
    [Test]
    public void PostPastList_SpawnsNothing()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        DefineNpc(world);
        world.Maps[Map].Npcs.Add(new MapNpcEntry(NpcNum, null, null));   // only post 1 is authored

        NewSpawn(world, pm).SpawnNpc(2, Map);

        Assert.That(world.MapNpcs[Map, 2].Num, Is.EqualTo(0), "no entry for post 2 → nothing spawns");
    }

    // The entry list round-trips through JSON with pins preserved (nullable coords), so a save/load keeps
    // exactly the authored rows — the "save only non-empty rows, reload only those rows" contract.
    [Test]
    public void Npcs_JsonRoundTrip_PreservesEntriesAndPins()
    {
        var entries = new List<MapNpcEntry>
        {
            new(7, 9, 4),
            new(3, null, null),
        };

        string json = JsonSerializer.Serialize(entries);
        var back = JsonSerializer.Deserialize<List<MapNpcEntry>>(json)!;

        Assert.That(back, Has.Count.EqualTo(2));
        Assert.That(back[0], Is.EqualTo(new MapNpcEntry(7, 9, 4)));
        Assert.That(back[1], Is.EqualTo(new MapNpcEntry(3, null, null)));
        Assert.That(back[0].HasPin, Is.True);
        Assert.That(back[1].HasPin, Is.False);
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

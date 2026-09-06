using Microsoft.Extensions.Logging.Abstractions;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.World;

/// <summary>Setting a personal spawn point at an Inn (ConfirmSetSpawn): only works standing in an Inn, costs
/// a level-scaled amount of gold (floored at SpawnCostMinimum), and on success charges the gold and records
/// the CURRENT tile as the spawn. Refusals (no Inn, too poor) leave both gold and the spawn untouched.</summary>
[TestFixture]
public class PlayerSpawnSystemTests
{
    const int Map = 1, ShopNum = 1, Idx = 1;
    const int Gold = Constants.GoldItemIndex;

    static (GameWorld world, PlayerSpawnSystem spawn, PlayerRecord p) Setup(int level, int gold, ShopType shopType)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var items = new ItemSystem(world, pm, dispatcher, persistence: null!, bg: null!);
        var saver = new PlayerSaver(persistence: null!, NullLogger<PlayerSaver>.Instance);
        var spawn = new PlayerSpawnSystem(world, pm, dispatcher, items, saver);

        world.Shops[ShopNum].ShopType = shopType;
        world.Items[Gold].Type = ItemType.Currency;

        var sp = pm[Idx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Login = "alice";
        var p = sp.Char;
        p.Map = Map;
        p.X = 7;
        p.Y = 8;
        p.Level = level;
        if (gold > 0)
        {
            p.Inv[1].Num = Gold;
            p.Inv[1].Quantity = gold;
        }
        // Setting a spawn happens at an Inn keeper: open the keeper's inn so ConfirmSetSpawn resolves
        // the active shop (keeper on the player's tile + observed + in range).
        OpenKeeperShop(world, sp, Idx);
        return (world, spawn, p);
    }

    const int KeeperNpc = 1, KeeperSlot = 1;
    static void OpenKeeperShop(GameWorld world, ServerPlayer sp, int idx)
    {
        world.Shops[ShopNum].Keeper = KeeperNpc;
        var mn = world.MapNpcs[sp.Char.Map, KeeperSlot];
        mn.Num = KeeperNpc;
        mn.X = sp.Char.X;
        mn.Y = sp.Char.Y;
        world.MapObservers[sp.Char.Map].Add(idx);
        sp.SetActiveShop(ShopNum, sp.Char.Map, KeeperSlot);
    }

    [Test]
    public void ConfirmSetSpawn_NotAtInn_Refused()
    {
        var (world, spawn, p) = Setup(level: 1, gold: 100, ShopType.Store);   // a Store, not an Inn
        spawn.ConfirmSetSpawn(Idx);
        Assert.Multiple(() =>
        {
            Assert.That(p.SpawnMap, Is.EqualTo(0), "spawn is not set away from an Inn");
            Assert.That(ItemSystem.CountItem(p, world.Items, Gold), Is.EqualTo(100), "no gold is charged");
        });
    }

    [Test]
    public void ConfirmSetSpawn_InsufficientGold_Refused()
    {
        var (world, spawn, p) = Setup(level: 1, gold: 4, ShopType.Inn);   // below the floor cost (5)
        spawn.ConfirmSetSpawn(Idx);
        Assert.Multiple(() =>
        {
            Assert.That(p.SpawnMap, Is.EqualTo(0), "spawn is not set when too poor");
            Assert.That(ItemSystem.CountItem(p, world.Items, Gold), Is.EqualTo(4), "no gold is charged");
        });
    }

    // Level 1's cost is exactly the floor (SpawnCostMinimum = 5); the current tile becomes the spawn.
    [Test]
    public void ConfirmSetSpawn_Success_ChargesFloorCost_AndRecordsCurrentTile()
    {
        var (world, spawn, p) = Setup(level: 1, gold: 100, ShopType.Inn);
        spawn.ConfirmSetSpawn(Idx);
        Assert.Multiple(() =>
        {
            Assert.That(p.SpawnMap, Is.EqualTo(Map));
            Assert.That(p.SpawnX, Is.EqualTo(7));
            Assert.That(p.SpawnY, Is.EqualTo(8));
            Assert.That(ItemSystem.CountItem(p, world.Items, Gold), Is.EqualTo(100 - Constants.SpawnCostMinimum),
                "charged the level-1 floor cost");
        });
    }

    // The cost climbs with level, so a level-20 respawn set costs more than a level-1 one.
    [Test]
    public void ConfirmSetSpawn_CostScalesWithLevel()
    {
        var (w1, s1, p1) = Setup(level: 1, gold: 100_000, ShopType.Inn);
        s1.ConfirmSetSpawn(Idx);
        long chargedL1 = 100_000 - ItemSystem.CountItem(p1, w1.Items, Gold);

        var (w20, s20, p20) = Setup(level: 20, gold: 100_000, ShopType.Inn);
        s20.ConfirmSetSpawn(Idx);
        long chargedL20 = 100_000 - ItemSystem.CountItem(p20, w20.Items, Gold);

        Assert.That(chargedL20, Is.GreaterThan(chargedL1), "the set-spawn cost scales up with level");
    }

    // ── Harness ──────────────────────────────────────────────────────────────────

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

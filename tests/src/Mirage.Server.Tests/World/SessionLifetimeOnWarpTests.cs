using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using NUnit.Framework;
using System.Collections.Generic;

namespace Mirage.Server.Tests.World;

/// <summary>
/// A shop session and a quest-menu session share ONE lifetime: both end when the player leaves the map
/// the keeper stands on, and neither ends for any other reason a warp can produce.
///
/// <para>Reach decides the opening of a keeper's panel and nothing after it, so a warp that lands back on
/// the same map keeps both sessions — the player is still on the keeper's map, the identity check still
/// holds, and the relocation was not something they walked. Same-map warps are not hypothetical: a
/// territory contest pushes non-defenders out of a capture radius with
/// <c>PlayerWarp(i, ch.Map, x, y)</c>, and an admin can warp to the map they are already on.</para>
///
/// <para>🔴 The two clears have to stay braced together under one <c>if</c>. Guarding only the first
/// drops the quest session on a same-map warp while keeping the shop one, and nothing downstream notices
/// the halves disagreeing. Both accessors are asserted together here for that reason: the invariant is
/// that they agree, not that either behaves a particular way.</para>
/// </summary>
[TestFixture]
public class SessionLifetimeOnWarpTests
{
    const int Here = 1, Elsewhere = 2, ShopNum = 3, KeeperNpc = 5, KeeperSlot = 1, Idx = 1;

    /// <summary>A player standing beside a keeper on <see cref="Here"/>, with both panels open.</summary>
    static (MovementSystem move, GameWorld world, ServerPlayer sp) Setup()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var chat = new NoOpDispatcher();
        var move = new MovementSystem(world, pm, chat, new BloodSystem(world, chat));

        world.Shops[ShopNum].ShopType = ShopType.Store;
        world.Shops[ShopNum].Keeper = KeeperNpc;
        var keeper = world.MapNpcs[Here, KeeperSlot];
        keeper.Num = KeeperNpc;
        keeper.X = 5;
        keeper.Y = 4;

        var sp = pm[Idx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Char.Map = Here;
        sp.Char.X = 5;
        sp.Char.Y = 5;
        sp.Char.MaxHp = 100;
        sp.Char.Hp = 100;                       // full HP => no blood trail during the move
        world.MapObservers[Here].Add(Idx);

        sp.SetActiveShop(ShopNum, Here, KeeperSlot);
        sp.SetActiveQuestNpc(Here, KeeperSlot);
        return (move, world, sp);
    }

    [Test]
    public void SameMapWarp_KeepsBothSessions()
    {
        var (move, world, sp) = Setup();
        Assume.That(sp.ActiveShop(world, Idx), Is.EqualTo(ShopNum));
        Assume.That(sp.ActiveQuestNpc(world), Is.EqualTo(KeeperNpc));

        move.PlayerWarp(Idx, Here, 10, 10);

        Assert.Multiple(() =>
        {
            Assert.That(sp.ActiveShop(world, Idx), Is.EqualTo(ShopNum),
                "a warp within the keeper's own map closed the shop");
            Assert.That(sp.ActiveQuestNpc(world), Is.EqualTo(KeeperNpc),
                "a warp within the keeper's own map closed the quest menu");
        });
    }

    [Test]
    public void LeavingTheMap_EndsBothSessions()
    {
        var (move, world, sp) = Setup();

        move.PlayerWarp(Idx, Elsewhere, 5, 5);

        Assert.Multiple(() =>
        {
            Assert.That(sp.ActiveShop(world, Idx), Is.EqualTo(0), "the shop outlived the map it was opened on");
            Assert.That(sp.ActiveQuestNpc(world), Is.EqualTo(0), "the quest menu outlived the map it was opened on");
        });
    }

    /// <summary>The invariant itself: whatever a warp does to one session it does to the other. Open means
    /// the accessor resolves to something, which is what every caller reads.</summary>
    [Test]
    public void BothSessionsAlwaysAgree([Values(Here, Elsewhere)] int destination)
    {
        var (move, world, sp) = Setup();

        move.PlayerWarp(Idx, destination, 7, 7);

        bool shopOpen = sp.ActiveShop(world, Idx) != 0;
        bool questOpen = sp.ActiveQuestNpc(world) != 0;
        Assert.That(shopOpen, Is.EqualTo(questOpen),
            $"warping to map {destination} left the shop {(shopOpen ? "open" : "closed")} "
            + $"and the quest menu {(questOpen ? "open" : "closed")}");
    }

    /// <summary>A refused warp is not a move, so it ends nothing.</summary>
    [Test]
    public void ARefusedWarp_EndsNeitherSession()
    {
        var (move, world, sp) = Setup();

        Assume.That(move.PlayerWarp(Idx, Elsewhere, 9999, 9999), Is.False, "the destination should not exist");

        Assert.Multiple(() =>
        {
            Assert.That(sp.ActiveShop(world, Idx), Is.EqualTo(ShopNum));
            Assert.That(sp.ActiveQuestNpc(world), Is.EqualTo(KeeperNpc));
        });
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

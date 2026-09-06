using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Reflection;

namespace Mirage.Server.Tests.World;

/// <summary>The Key-door auto-close sweep (NpcAiSystem.CheckDoorAutoClose). Every open door carries its own
/// TickCount64 stamp in TempTileState.DoorOpenedAt, so each shuts exactly DoorAutoCloseMs after IT opened —
/// opening a second door neither extends the first's window nor drags it shut early, and the two layers of one
/// tile keep separate clocks.</summary>
[TestFixture]
public class DoorAutoCloseTests
{
    const int Map = 1, Idx = 1;
    const long AutoCloseMs = 5_000;   // mirrors NpcAiSystem.DoorAutoCloseMs

    // ── Per-door timing ──────────────────────────────────────────────────────────

    // Why each door carries its own stamp: with a single per-map timer, opening B re-stamps the shared
    // clock and the sweep early-returns, so A stays open indefinitely on a map with steady door traffic.
    [Test]
    public void Sweep_SecondDoorOpening_DoesNotExtendTheFirstDoorsWindow()
    {
        var (world, ai, _) = Setup();
        var temp = world.TempTiles[Map];
        GroundDoor(world, 3, 3);
        GroundDoor(world, 9, 3);

        temp.OpenDoor(3, 3, WorldLayer.Ground, 1_000);   // A opens first…
        temp.OpenDoor(9, 3, WorldLayer.Ground, 4_000);   // …B 3s later, well inside A's window

        Sweep(ai, now: 6_000);                           // A is 5.0s old, B only 2.0s

        Assert.Multiple(() =>
        {
            Assert.That(temp.IsDoorOpen(3, 3, WorldLayer.Ground), Is.False, "A shuts on its own 5s clock");
            Assert.That(temp.IsDoorOpen(9, 3, WorldLayer.Ground), Is.True, "B keeps its full window");
        });
    }

    [Test]
    public void Sweep_DoorHoldsUntilItsOwnWindowElapses_ThenShuts()
    {
        var (world, ai, _) = Setup();
        var temp = world.TempTiles[Map];
        GroundDoor(world, 4, 4);
        temp.OpenDoor(4, 4, WorldLayer.Ground, 1_000);

        Sweep(ai, now: 1_000 + AutoCloseMs - 1);
        Assert.That(temp.IsDoorOpen(4, 4, WorldLayer.Ground), Is.True, "1ms short of the window, still open");

        Sweep(ai, now: 1_000 + AutoCloseMs);
        Assert.That(temp.IsDoorOpen(4, 4, WorldLayer.Ground), Is.False, "the window elapsed, shut");
    }

    // A door opened BEFORE the sweep's clock reaches it must not be dragged shut early just because another
    // door on the map is overdue — the sweep decides per cell, never per map.
    [Test]
    public void Sweep_OverdueDoor_DoesNotDragAFreshDoorShutWithIt()
    {
        var (world, ai, _) = Setup();
        var temp = world.TempTiles[Map];
        GroundDoor(world, 2, 2);
        GroundDoor(world, 12, 8);

        temp.OpenDoor(2, 2, WorldLayer.Ground, 1_000);     // long overdue by the sweep below
        temp.OpenDoor(12, 8, WorldLayer.Ground, 20_000);   // opened just before the sweep

        Sweep(ai, now: 20_100);

        Assert.Multiple(() =>
        {
            Assert.That(temp.IsDoorOpen(2, 2, WorldLayer.Ground), Is.False, "the overdue door shuts");
            Assert.That(temp.IsDoorOpen(12, 8, WorldLayer.Ground), Is.True, "the 100ms-old door is untouched");
        });
    }

    // §1b per-layer doors: DoorOpenedAt is indexed [x, y, layer], so a deck door and the ground door beneath it
    // hold independent clocks as well as independent open flags.
    [Test]
    public void Sweep_SameTileBothLayers_AgeOutIndependently()
    {
        var (world, ai, _) = Setup();
        var temp = world.TempTiles[Map];
        GroundDoor(world, 6, 6);
        world.Maps[Map].EditTile(6, 6, t => t with { FringeAttr = new FringeAttr { Type = TileType.Key } });   // deck door over it

        temp.OpenDoor(6, 6, WorldLayer.Ground, 1_000);
        temp.OpenDoor(6, 6, WorldLayer.Fringe, 4_000);

        Sweep(ai, now: 6_000);

        Assert.Multiple(() =>
        {
            Assert.That(temp.IsDoorOpen(6, 6, WorldLayer.Ground), Is.False, "the ground door's window elapsed");
            Assert.That(temp.IsDoorOpen(6, 6, WorldLayer.Fringe), Is.True, "the deck door above keeps its own clock");
        });
    }

    // ── Broadcast ────────────────────────────────────────────────────────────────

    [Test]
    public void Sweep_ClosingOneDoor_BroadcastsThatDoorOnly()
    {
        var (world, ai, dispatcher) = Setup();
        var temp = world.TempTiles[Map];
        GroundDoor(world, 3, 3);
        GroundDoor(world, 9, 3);
        temp.OpenDoor(3, 3, WorldLayer.Ground, 1_000);
        temp.OpenDoor(9, 3, WorldLayer.Ground, 4_000);

        Sweep(ai, now: 6_000);

        var keys = dispatcher.ToObservers.OfType<MapKeyPacket>().ToList();
        Assert.That(keys, Has.Count.EqualTo(1), "only the door that actually shut is broadcast");
        Assert.Multiple(() =>
        {
            Assert.That((keys[0].X, keys[0].Y), Is.EqualTo((3, 3)));
            Assert.That(keys[0].Layer, Is.EqualTo(WorldLayer.Ground));
            Assert.That(keys[0].Open, Is.False);
            Assert.That(keys[0].MapNum, Is.EqualTo(Map));
        });
    }

    [Test]
    public void Sweep_NoOpenDoors_BroadcastsNothing()
    {
        var (world, ai, dispatcher) = Setup();
        GroundDoor(world, 3, 3);   // authored but never opened

        Sweep(ai, now: 60_000);

        Assert.That(dispatcher.ToObservers, Is.Empty, "a shut door is not re-closed every tick");
    }

    // A tile the editor retyped away from Key while its door stood open keeps the stale stamp rather than
    // broadcasting a close for something the client no longer draws as a door.
    [Test]
    public void Sweep_TileNoLongerAKeyDoor_IsLeftAloneAndSilent()
    {
        var (world, ai, dispatcher) = Setup();
        var temp = world.TempTiles[Map];
        GroundDoor(world, 5, 5);
        temp.OpenDoor(5, 5, WorldLayer.Ground, 1_000);
        world.Maps[Map].EditTile(5, 5, t => t with { Type = TileType.Walkable });   // editor retyped it out from under the open door

        Sweep(ai, now: 60_000);

        Assert.That(dispatcher.ToObservers, Is.Empty, "no close packet for a tile that is no longer a door");
    }

    // ── Harness ──────────────────────────────────────────────────────────────────

    // CheckDoorAutoClose reads only _world and _dispatcher, so the combat/movement/spawn/item/blood
    // dependencies can be null (same shape as NpcChaseRoutingTests.NewWorldWithBlocker).
    static (GameWorld world, NpcAiSystem ai, CapturingDispatcher dispatcher) Setup()
    {
        var world = new GameWorld();
        var dispatcher = new CapturingDispatcher();
        var ai = new NpcAiSystem(world, new PlayerManager(), dispatcher, null!, null!, null!, null!, null!);
        world.MapObservers[Map].Add(Idx);
        return (world, ai, dispatcher);
    }

    static void GroundDoor(GameWorld world, int x, int y) => world.Maps[Map].EditTile(x, y, t => t with { Type = TileType.Key });

    static void Sweep(NpcAiSystem ai, long now)
    {
        var m = typeof(NpcAiSystem).GetMethod("CheckDoorAutoClose", BindingFlags.NonPublic | BindingFlags.Instance)!;
        m.Invoke(ai, new object[] { Map, now });
    }

    sealed class CapturingDispatcher : IPacketDispatcher
    {
        public readonly List<IPacket> ToObservers = new();

        public void SendToObservers(IReadOnlyCollection<int> observers, IPacket packet) => ToObservers.Add(packet);

        public void SendTo(int index, IPacket packet) { }
        public void SendToAll(IPacket packet) { }
        public void SendToAllBut(int exclude, IPacket packet) { }
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

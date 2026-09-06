using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.World;

/// <summary>
/// The bytes a blood pool goes out as.
///
/// <para>Written as literals on purpose, and the client's decode test writes the same ones: the layout is a
/// contract between two ends that never see each other's code, so it is pinned from both sides rather than
/// generated from one. The coordinates are 16-bit because a tile coordinate must be able to name any tile on
/// the map, and a byte stops one short of a 256-wide one.</para>
/// </summary>
[TestFixture]
public class BloodWireTests
{
    private const int Map = 1, Idx = 1;

    private static (GameWorld world, BloodSystem blood, CapturingDispatcher sent) Setup(int width = 16, int height = 12)
    {
        var world = new GameWorld();
        world.Maps[Map] = new MapRecord(width, height);
        var sent = new CapturingDispatcher();
        var blood = new BloodSystem(world, sent);
        world.MapObservers[Map].Add(Idx);
        return (world, blood, sent);
    }

    private static byte[] PoolsFor(BloodSystem blood, CapturingDispatcher sent)
    {
        blood.Tick();
        var packet = sent.Packets.OfType<BloodUpdatePacket>().LastOrDefault();
        Assert.That(packet, Is.Not.Null, "the map was dirtied, so a blood update should have gone out");
        return packet!.Pools;
    }

    [Test]
    public void APoolGoesOutAsTheStatedBytes()
    {
        var (_, blood, sent) = Setup();
        blood.Deposit(Map, 5, 6, intensity: 1f, size: 3);

        byte[] pools = PoolsFor(blood, sent);

        Assert.That(pools, Has.Length.EqualTo(BloodUpdatePacket.BytesPerPool));
        Assert.Multiple(() =>
        {
            Assert.That(pools[0], Is.EqualTo(5), "x, low byte");
            Assert.That(pools[1], Is.Zero, "x, high byte");
            Assert.That(pools[2], Is.EqualTo(6), "y, low byte");
            Assert.That(pools[3], Is.Zero, "y, high byte");
            Assert.That(pools[4], Is.EqualTo(3), "size");
            Assert.That(pools[7], Is.EqualTo((byte)WorldLayer.Ground), "layer");
        });
    }

    /// <summary>The reason the coordinates are two bytes. Packed as one, x = 300 would leave as 44 and the
    /// decal would land on the wrong tile with nothing to report it.</summary>
    [TestCase(300, 7)]
    [TestCase(7, 300)]
    [TestCase(1000, 999)]
    public void ACoordinateWiderThanAByte_SurvivesTheWire(int x, int y)
    {
        var (_, blood, sent) = Setup(width: 1024, height: 1024);
        blood.Deposit(Map, x, y, intensity: 1f);

        byte[] pools = PoolsFor(blood, sent);

        Assert.That(BloodUpdatePacket.PoolTileAt(pools, 0), Is.EqualTo((x, y)));
    }

    /// <summary>A deposit off the map is refused, so nothing is packed from a coordinate that names no tile.</summary>
    [Test]
    public void ADepositOffTheMap_IsRefused()
    {
        var (world, blood, _) = Setup();

        blood.Deposit(Map, 500, 500, intensity: 1f);

        Assert.That(world.MapBlood.ContainsKey(Map), Is.False);
    }

    private sealed class CapturingDispatcher : IPacketDispatcher
    {
        public List<IPacket> Packets { get; } = new();

        public void SendToObservers(IReadOnlyCollection<int> observers, IPacket packet) => Packets.Add(packet);
        public void SendTo(int index, IPacket packet) => Packets.Add(packet);

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

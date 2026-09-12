using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using NUnit.Framework;

namespace Mirage.Server.Tests.GameLogic;

/// <summary>
/// A game changing an attribute, and the change reaching exactly the right sockets.
///
/// <para>🔴 Two failures here are silent in opposite directions. Sending too much discloses a value the
/// game meant to keep — and looks identical, from the sending side, to sending the right amount. Sending
/// too little leaves a number frozen on somebody's screen, which reads as a stale display rather than as
/// a missing packet. Neither throws, so both are pinned from the outside: who received what.</para>
/// </summary>
[TestFixture]
public class AttributeSystemTests
{
    private const int Map = 1, Owner = 1, Onlooker = 2, Elsewhere = 3;

    private sealed record Fixture(AttributeSystem Attributes, CapturingDispatcher Sent, GameWorld World);

    private static Fixture Build()
    {
        var world = new GameWorld
        {
            Attributes = new AttributeSchema.Builder()
                .Declare("seed", AttributeVisibility.None)
                .Declare("gold", AttributeVisibility.Owner)
                .Declare("hp", AttributeVisibility.Viewport)
                .Build() };
        var pm = new PlayerManager();
        var dispatcher = new CapturingDispatcher();

        foreach (int i in new[] { Owner, Onlooker, Elsewhere })
        {
            var sp = pm[i];
            sp.IsConnected = true;
            sp.InGame = true;
            sp.CharNum = 1;
            sp.Char.Name = $"P{i}";
            sp.Char.Map = i == Elsewhere ? 2 : Map;
        }
        world.MapObservers[Map].Add(Owner);
        world.MapObservers[Map].Add(Onlooker);
        world.MapObservers[2].Add(Elsewhere);

        return new Fixture(new AttributeSystem(world, pm, dispatcher), dispatcher, world);
    }

    private static string[] KeysSentTo(Fixture f, int index)
    {
        var schema = f.World.Attributes;
        return [.. f.Sent.To(index).OfType<AttributeSyncPacket>()
            .SelectMany(p => p.Set)
            .Select(e => schema.TryGet(e.Ordinal, out var d) ? d.Key : "?")];
    }

    [Test]
    public void SettingAPublicValue_ReachesTheOwnerAndEveryOnlooker()
    {
        var f = Build();

        f.Attributes.Set(EntityHandle.ForPlayer(Owner), "hp", 40);

        Assert.Multiple(() =>
        {
            Assert.That(KeysSentTo(f, Owner), Is.EqualTo(new[] { "hp" }));
            Assert.That(KeysSentTo(f, Onlooker), Is.EqualTo(new[] { "hp" }));
            Assert.That(KeysSentTo(f, Elsewhere), Is.Empty, "somebody on another map is not an onlooker");
        });
    }

    [Test]
    public void SettingAnOwnerValue_ReachesTheOwnerAlone()
    {
        var f = Build();

        f.Attributes.Set(EntityHandle.ForPlayer(Owner), "gold", 250);

        Assert.Multiple(() =>
        {
            Assert.That(KeysSentTo(f, Owner), Is.EqualTo(new[] { "gold" }));
            Assert.That(KeysSentTo(f, Onlooker), Is.Empty, "an onlooker must never learn somebody's balance");
        });
    }

    [Test]
    public void SettingAHiddenValue_ReachesNobodyAndIsStillStored()
    {
        var f = Build();

        bool wrote = f.Attributes.Set(EntityHandle.ForPlayer(Owner), "seed", 99);

        Assert.Multiple(() =>
        {
            Assert.That(wrote, Is.True);
            Assert.That(f.Attributes.BagOf(EntityHandle.ForPlayer(Owner))!["seed"].AsLong(), Is.EqualTo(99));
            Assert.That(KeysSentTo(f, Owner), Is.Empty);
            Assert.That(KeysSentTo(f, Onlooker), Is.Empty);
        });
    }

    /// <summary>A change spanning two values arrives as one packet, so a client never renders a state
    /// that was true for nobody — gold spent and the thing not yet bought.</summary>
    [Test]
    public void SettingSeveralValues_ArrivesAsOneChange()
    {
        var f = Build();

        f.Attributes.SetMany(EntityHandle.ForPlayer(Owner),
        [
            new("gold", 100),
            new("hp", 12),
        ]);

        Assert.That(f.Sent.To(Owner).OfType<AttributeSyncPacket>().Count(), Is.EqualTo(1));
    }

    /// <summary>Nothing is watched, so nothing is sent — and the value is still stored. This is Core on
    /// its own, and it must cost no traffic at all.</summary>
    [Test]
    public void AWorldWhoseGameDeclaredNothing_PutsNothingOnTheWire()
    {
        var f = Build();
        f.World.Attributes = AttributeSchema.Empty;

        f.Attributes.Set(EntityHandle.ForPlayer(Owner), "hp", 40);

        Assert.Multiple(() =>
        {
            Assert.That(f.Sent.All, Is.Empty);
            Assert.That(f.Attributes.BagOf(EntityHandle.ForPlayer(Owner))!["hp"].AsLong(), Is.EqualTo(40));
        });
    }

    [Test]
    public void SettingOnABodyThatIsNotThere_WritesNothingAndSaysSo()
    {
        var f = Build();

        Assert.Multiple(() =>
        {
            Assert.That(f.Attributes.Set(EntityHandle.ForPlayer(9), "hp", 1), Is.False, "an empty slot");
            Assert.That(f.Attributes.Set(EntityHandle.None, "hp", 1), Is.False, "nobody at all");
            Assert.That(f.Attributes.Set(EntityHandle.ForNpc(Map, 1), "hp", 1), Is.False, "an unspawned slot");
            Assert.That(f.Sent.All, Is.Empty);
        });
    }

    /// <summary>A removal cannot be one entry, so the whole visible set is re-sent and the client
    /// replaces rather than patches. Without this a removed key would sit on every screen forever.</summary>
    [Test]
    public void RemovingAKey_ResendsWhatIsLeft()
    {
        var f = Build();
        f.Attributes.SetMany(EntityHandle.ForPlayer(Owner), [new("gold", 100), new("hp", 12)]);
        f.Sent.Clear();

        f.Attributes.Remove(EntityHandle.ForPlayer(Owner), "gold");

        Assert.That(KeysSentTo(f, Owner), Is.EqualTo(new[] { "hp" }));
    }

    [Test]
    public void ASpawnedNpcsValue_ReachesEveryOnlookerOnItsMap()
    {
        var f = Build();
        f.World.MapNpcs[Map, 1].Num = 1;

        bool wrote = f.Attributes.Set(EntityHandle.ForNpc(Map, 1), "hp", 40);

        Assert.Multiple(() =>
        {
            Assert.That(wrote, Is.True);
            Assert.That(KeysSentTo(f, Owner), Is.EqualTo(new[] { "hp" }));
            Assert.That(KeysSentTo(f, Onlooker), Is.EqualTo(new[] { "hp" }));
            Assert.That(KeysSentTo(f, Elsewhere), Is.Empty);
        });
    }

    private sealed class CapturingDispatcher : IPacketDispatcher
    {
        private readonly List<(int Index, IPacket Packet)> _sent = [];

        public IReadOnlyList<(int Index, IPacket Packet)> All => _sent;
        public IEnumerable<IPacket> To(int index) => _sent.Where(s => s.Index == index).Select(s => s.Packet);
        public void Clear() => _sent.Clear();

        public void SendTo(int index, IPacket packet) => _sent.Add((index, packet));

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
        public void SendLocalizedChatTo(int index, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAll(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAllBut(int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObservers(IReadOnlyCollection<int> observers, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObserversBut(IReadOnlyCollection<int> observers, int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewport(int speakerIndex, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewportAt(int mapNum, int x, int y, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAdmins(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToGuild(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToGuildOfficers(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendToEditor(int editorIndex, IPacket packet) { }
        public void SendToAllEditors(IPacket packet) { }
        public void Disconnect(int index) { }
        public void DisconnectEditor(int editorIndex) { }
        public void GracefulDisconnect(int index) { }
        public void GracefulDisconnectEditor(int editorIndex) { }
    }
}

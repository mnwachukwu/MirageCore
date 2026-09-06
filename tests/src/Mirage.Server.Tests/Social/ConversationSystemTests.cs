using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Tests.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mirage.Server.Tests.Social;

/// <summary>NPC conversations: the world resolver (<see cref="GameWorld.ConversationForNpc"/>), the per-character
/// visited-log (<see cref="ConversationSystem"/>.MarkSpoken / OnPlayerJoin — the source of the yellow→gray
/// "..." glyph), the record deep-copy + root resolution, and a wire round-trip of the three S→C packets (the
/// DEBUG registration check is compiled out of the Release test build, so this asserts it explicitly).</summary>
[TestFixture]
public class ConversationSystemTests
{
    static (GameWorld world, PlayerManager pm, CapturingDispatcher disp, ConversationSystem convs) Setup()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var disp = new CapturingDispatcher();
        var convs = new ConversationSystem(world, pm, disp);
        return (world, pm, disp, convs);
    }

    static ServerPlayer AddPlayer(PlayerManager pm, int idx)
    {
        var sp = pm[idx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Login = "acc" + idx;
        sp.Char.Name = "P" + idx;
        sp.Char.Map = 1;
        return sp;
    }

    // Define a real (non-empty-name) conversation attached to an NPC in a slot.
    static ConversationRecord Convo(GameWorld world, int num, int speakerNpc)
    {
        var c = world.Conversations[num];
        c.Name = "Conversation " + num;
        c.SpeakerNpc = speakerNpc;
        c.Nodes.Clear();
        c.Nodes.Add(new ConversationNode { Id = 1, Text = "Hello." });
        c.RootNodeId = 1;
        return c;
    }

    // ── GameWorld.ConversationForNpc ──────────────────────────────────────────────

    [Test]
    public void ConversationForNpc_ResolvesAttachedConversation()
    {
        var (world, _, _, _) = Setup();
        Convo(world, 5, speakerNpc: 42);
        Assert.That(world.ConversationForNpc(42), Is.EqualTo(5));
    }

    [Test]
    public void ConversationForNpc_NoneForUnattachedNpc()
    {
        var (world, _, _, _) = Setup();
        Convo(world, 5, speakerNpc: 42);
        Assert.That(world.ConversationForNpc(99), Is.EqualTo(0));
    }

    [Test]
    public void ConversationForNpc_IgnoresEmptyNamedConversation()
    {
        var (world, _, _, _) = Setup();
        world.Conversations[5].SpeakerNpc = 42;   // Name left blank → not a real row
        Assert.That(world.ConversationForNpc(42), Is.EqualTo(0));
    }

    [Test]
    public void ConversationForNpc_FirstNonEmptyMatchWins()
    {
        var (world, _, _, _) = Setup();
        Convo(world, 8, speakerNpc: 42);
        Convo(world, 3, speakerNpc: 42);
        Assert.That(world.ConversationForNpc(42), Is.EqualTo(3), "lowest-numbered match wins");
    }

    // ── ConversationSystem.MarkSpoken / OnPlayerJoin ──────────────────────────────

    [Test]
    public void MarkSpoken_AddsToLogAndPushes()
    {
        var (world, pm, disp, convs) = Setup();
        Convo(world, 5, speakerNpc: 42);
        var sp = AddPlayer(pm, 1);

        convs.MarkSpoken(1, 5);

        Assert.That(sp.Char.ConversationsSpoken, Is.EquivalentTo(new[] { 5 }));
        var log = disp.Sent.Select(x => x.Packet).OfType<ConversationLogPacket>().Last();
        Assert.That(log.Spoken, Is.EquivalentTo(new[] { 5 }));
    }

    [Test]
    public void MarkSpoken_IsIdempotent()
    {
        var (world, pm, disp, convs) = Setup();
        Convo(world, 5, speakerNpc: 42);
        var sp = AddPlayer(pm, 1);

        convs.MarkSpoken(1, 5);
        int pushesAfterFirst = disp.Sent.Count(x => x.Packet is ConversationLogPacket);
        convs.MarkSpoken(1, 5);   // already spoken — no state change, no re-push

        Assert.That(sp.Char.ConversationsSpoken, Has.Count.EqualTo(1));
        Assert.That(disp.Sent.Count(x => x.Packet is ConversationLogPacket), Is.EqualTo(pushesAfterFirst),
            "a repeat MarkSpoken doesn't re-push");
    }

    [Test]
    public void MarkSpoken_IgnoresNonexistentConversation()
    {
        var (_, pm, _, convs) = Setup();
        var sp = AddPlayer(pm, 1);
        convs.MarkSpoken(1, 5);   // slot 5 is a blank conversation
        Assert.That(sp.Char.ConversationsSpoken, Is.Empty);
    }

    [Test]
    public void OnPlayerJoin_PushesCurrentSpokenSet()
    {
        var (_, pm, disp, convs) = Setup();
        var sp = AddPlayer(pm, 1);
        sp.Char.ConversationsSpoken.Add(3);
        sp.Char.ConversationsSpoken.Add(7);

        convs.OnPlayerJoin(1);

        var log = disp.Sent.Select(x => x.Packet).OfType<ConversationLogPacket>().Last();
        Assert.That(log.Spoken, Is.EquivalentTo(new[] { 3, 7 }));
    }

    // ── Record deep-copy + root resolution ────────────────────────────────────────

    [Test]
    public void Clone_DeepCopiesNodesAndChoices()
    {
        var c = new ConversationRecord { Name = "C" };
        var node = new ConversationNode { Id = 1, Text = "Hi" };
        node.Choices.Add(new ConversationChoice { Label = "Bye", NextNodeId = 0 });
        c.Nodes.Add(node);

        var clone = c.Clone();
        clone.Nodes[0].Text = "Changed";
        clone.Nodes[0].Choices[0].Label = "Changed";

        Assert.Multiple(() =>
        {
            Assert.That(c.Nodes[0].Text, Is.EqualTo("Hi"), "node list deep-copied");
            Assert.That(c.Nodes[0].Choices[0].Label, Is.EqualTo("Bye"), "choice list deep-copied");
        });
    }

    [Test]
    public void RootNode_FallsBackToFirstNodeWhenIdUnresolvable()
    {
        var c = new ConversationRecord { Name = "C", RootNodeId = 99 };
        c.Nodes.Add(new ConversationNode { Id = 1, Text = "First" });
        Assert.That(c.RootNode?.Text, Is.EqualTo("First"));
    }

    // ── Wire round-trip (the DEBUG registration check is compiled out of Release) ──

    [Test]
    public void Packets_RoundTripThroughSerializer()
    {
        var send = new SendConversationsPacket
        {
            Conversations = new() { new SendConversationsPacket.ConvData { Num = 1, SpeakerNpc = 42, RootNodeId = 1 } }
        };
        var log = new ConversationLogPacket { Spoken = new[] { 3, 7 } };
        var open = new OpenNpcConversationPacket { MapNum = 2, NpcSlot = 4, ConvNum = 5 };

        Assert.Multiple(() =>
        {
            Assert.That(PacketSerializer.TryDeserialize(PacketSerializer.Serialize(send)), Is.TypeOf<SendConversationsPacket>());
            var rtLog = PacketSerializer.TryDeserialize(PacketSerializer.Serialize(log)) as ConversationLogPacket;
            Assert.That(rtLog?.Spoken, Is.EquivalentTo(new[] { 3, 7 }));
            var rtOpen = PacketSerializer.TryDeserialize(PacketSerializer.Serialize(open)) as OpenNpcConversationPacket;
            Assert.That(rtOpen?.ConvNum, Is.EqualTo(5));
        });
    }

    [Test]
    public void EditorPackets_RoundTripThroughSerializer()
    {
        // The 5 conversation EDITOR packets — asserts each is registered in PacketNames + PacketSerializer (the
        // DEBUG boot round-trip check that would catch a miss is compiled out of the Release test build).
        IPacket[] packets =
        {
            new EditorRequestConversationPacket { ConvNum = 3 },
            new EditorRequestAllConversationsPacket(),
            new EditorSaveConversationPacket { ConvNum = 3, Name = "C", SpeakerNpc = 7, RootNodeId = 1 },
            new UpdateConversationPacket { ConvNum = 3, Name = "C", SpeakerNpc = 7 },
            new EditorAllConversationsPacket { Conversations = new[] { new UpdateConversationPacket { ConvNum = 3 } } },
        };
        foreach (var p in packets)
        {
            Assert.That(PacketSerializer.TryDeserialize(PacketSerializer.Serialize(p))?.GetType(),
                Is.EqualTo(p.GetType()), $"{p.GetType().Name} round-trips");
        }
    }

    [Test]
    public void HandAuthoredJson_DeserializesWithServerConventions()
    {
        // The on-disk format for a hand-authored conversation{n}.json — camelCase fields + STRING enums, the
        // primary authoring path until the editor. Mirrors JsonPersistenceService's serializer options.
        const string json = """
        {
          "name": "Old Man's Musings",
          "speakerNpc": 83,
          "rootNodeId": 1,
          "nodes": [
            { "id": 1, "text": "Hello.", "choices": [ { "label": "Wares", "nextNodeId": 0, "action": "OpenShop" } ] }
          ]
        }
        """;
        var opts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };

        var rec = JsonSerializer.Deserialize<ConversationRecord>(json, opts);

        Assert.That(rec, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(rec!.SpeakerNpc, Is.EqualTo(83));
            Assert.That(rec.RootNodeId, Is.EqualTo(1));
            Assert.That(rec.Nodes, Has.Count.EqualTo(1));
            Assert.That(rec.Nodes[0].Id, Is.EqualTo(1));
            Assert.That(rec.Nodes[0].Choices[0].Action, Is.EqualTo(ConversationAction.OpenShop), "string enum parses");
        });
    }

    // ── Harness ──────────────────────────────────────────────────────────────────

    // Records SendTo so a test can assert the ConversationLogPacket push; everything else is a no-op.
    sealed class CapturingDispatcher : IPacketDispatcher
    {
        public readonly List<(int Index, IPacket Packet)> Sent = new();
        public void SendTo(int index, IPacket packet) => Sent.Add((index, packet));
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

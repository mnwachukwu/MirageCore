using Microsoft.Extensions.Logging.Abstractions;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using NUnit.Framework;

namespace Mirage.Server.Tests.Accounts;

/// <summary>
/// Authoring a family the server was never compiled against, over the wire.
///
/// <para>This is what makes a module's records real: a game declares a family, and an editor that has
/// never heard of it can list, open and save its records. Everything the handler does with the keys is
/// move them, so what these pin is the routing, the refusals, and that nothing is dropped in between.</para>
/// </summary>
[TestFixture]
public class EditorModuleRecordTests
{
    const int Editor = 1;
    const string Species = "Species";

    private sealed class SpeciesModule : ICoreModule
    {
        public string Name => "Pocket";

        public void Configure(ICoreBuilder builder) => builder.AddFamily(new RecordFamily
        {
            Id = Species,
            Directory = "species",
            DefaultLimit = 3,
            LimitIsFixed = true,
            Fields = [new FieldDescriptor { Key = "name", Kind = FieldKind.Text }],
        });
    }

    [Test]
    public void RequestingARecord_AnswersWithWhatTheWorldHolds()
    {
        var h = new Harness();
        h.World.ModuleRecords.Set(Species, 2, new AttributeBag().Set("name", "Vulpine"));

        h.Send(new EditorRequestRecordPacket { Family = Species, Num = 2 });

        var reply = h.Dispatcher.OneDirect<UpdateRecordPacket>();
        Assert.Multiple(() =>
        {
            Assert.That(reply.Family, Is.EqualTo(Species));
            Assert.That(reply.Num, Is.EqualTo(2));
            Assert.That(reply.Fields["name"].AsText(), Is.EqualTo("Vulpine"));
        });
    }

    [Test]
    public void RequestingAllRecords_AnswersWithEverySlotInOrder()
    {
        var h = new Harness();
        h.World.ModuleRecords.Set(Species, 3, new AttributeBag().Set("name", "Third"));

        h.Send(new EditorRequestAllRecordsPacket { Family = Species });

        var reply = h.Dispatcher.OneDirect<EditorAllRecordsPacket>();
        Assert.Multiple(() =>
        {
            Assert.That(reply.Records.Select(r => r.Num), Is.EqualTo(new[] { 1, 2, 3 }),
                        "blanks included — an author fills the slots they want");
            Assert.That(reply.Records[2].Fields["name"].AsText(), Is.EqualTo("Third"));
        });
    }

    [Test]
    public void SavingARecord_StoresIt_PersistsIt_AndTellsEveryEditor()
    {
        var h = new Harness();

        h.Send(new EditorSaveRecordPacket
        {
            Family = Species,
            Num = 1,
            Fields = new AttributeBag().Set("name", "Vulpine").Set("baseSpeed", 65),
        });

        Assert.Multiple(() =>
        {
            Assert.That(h.World.ModuleRecords.Get(Species, 1)!["name"].AsText(), Is.EqualTo("Vulpine"));
            Assert.That(h.Persistence.SavedModuleRecords.Single().Family, Is.EqualTo(Species));
            Assert.That(h.Persistence.SavedModuleRecords.Single().Num, Is.EqualTo(1));
            Assert.That(h.Dispatcher.AllEditors.OfType<UpdateRecordPacket>().Single().Num, Is.EqualTo(1));
        });
    }

    /// <summary>A key no field describes survives a save. That is what lets a world authored against a
    /// newer build of a game open in an older editor without being quietly stripped.</summary>
    [Test]
    public void AKeyTheFamilyDoesNotDescribe_IsStoredAnyway()
    {
        var h = new Harness();

        h.Send(new EditorSaveRecordPacket
        {
            Family = Species,
            Num = 1,
            Fields = new AttributeBag().Set("hiddenAbility", "Drizzle"),
        });

        Assert.That(h.World.ModuleRecords.Get(Species, 1)!["hiddenAbility"].AsText(), Is.EqualTo("Drizzle"));
    }

    // The stored record is the server's own copy: the bag that arrived belongs to the deserializer, and
    // an editor that kept editing its own would otherwise reach into the world.
    [Test]
    public void TheStoredRecord_IsNotTheBagThatArrived()
    {
        var h = new Harness();
        var sent = new AttributeBag().Set("name", "Vulpine");

        h.Send(new EditorSaveRecordPacket { Family = Species, Num = 1, Fields = sent });
        sent.Set("name", "Changed after the save");

        Assert.That(h.World.ModuleRecords.Get(Species, 1)!["name"].AsText(), Is.EqualTo("Vulpine"));
    }

    // ── What this path refuses ────────────────────────────────────────────────

    /// <summary>A Core family has its own typed packets, which normalize what they are handed; a bag
    /// written straight into the array would skip every one of those rules.</summary>
    [Test]
    public void ACoreFamily_CannotBeAuthoredThroughThisPath()
    {
        var h = new Harness();

        h.Send(new EditorSaveRecordPacket
        {
            Family = CoreRecordFamilies.Items,
            Num = 1,
            Fields = new AttributeBag().Set("name", "Smuggled"),
        });

        Assert.Multiple(() =>
        {
            Assert.That(h.World.Items[1].Name, Is.Empty);
            Assert.That(h.Persistence.SavedModuleRecords, Is.Empty);
            Assert.That(h.Dispatcher.AllEditors, Is.Empty);
        });
    }

    [Test]
    public void AFamilyNoModuleDeclared_IsRefused()
    {
        var h = new Harness();

        h.Send(new EditorSaveRecordPacket { Family = "Moves", Num = 1, Fields = new AttributeBag() });
        h.Send(new EditorRequestRecordPacket { Family = "Moves", Num = 1 });
        h.Send(new EditorRequestAllRecordsPacket { Family = "Moves" });

        Assert.Multiple(() =>
        {
            Assert.That(h.Persistence.SavedModuleRecords, Is.Empty);
            Assert.That(h.Dispatcher.Direct, Is.Empty);
            Assert.That(h.Dispatcher.AllEditors, Is.Empty);
        });
    }

    [Test]
    public void ASlotOutsideTheFamilysRange_IsRefused()
    {
        var h = new Harness();

        h.Send(new EditorSaveRecordPacket { Family = Species, Num = 9, Fields = new AttributeBag() });

        Assert.That(h.Persistence.SavedModuleRecords, Is.Empty);
    }

    /// <summary>The same tier boundary Core's own content saves sit behind: a Mapper may look, and only a
    /// Developer may write.</summary>
    [Test]
    public void AMapper_MayReadButNotSave()
    {
        var h = new Harness(AdminLevel.Mapper);

        h.Send(new EditorRequestRecordPacket { Family = Species, Num = 1 });
        h.Send(new EditorSaveRecordPacket
        {
            Family = Species, Num = 1, Fields = new AttributeBag().Set("name", "Refused"),
        });

        Assert.Multiple(() =>
        {
            Assert.That(h.Dispatcher.Direct, Has.Count.EqualTo(1), "the read went through");
            Assert.That(h.World.ModuleRecords.Get(Species, 1)!.IsEmpty, Is.True, "and the write did not");
        });
    }

    [Test]
    public void ARecordAnotherEditorHasLocked_IsNotSaved()
    {
        var h = new Harness();
        h.Locks.TryAcquire(Species, 1, editorIndex: 2, login: "someone-else", sessionId: "other");

        h.Send(new EditorSaveRecordPacket
        {
            Family = Species, Num = 1, Fields = new AttributeBag().Set("name", "Refused"),
        });

        Assert.That(h.World.ModuleRecords.Get(Species, 1)!.IsEmpty, Is.True);
    }

    // ── Harness ──────────────────────────────────────────────────────────────

    sealed class Harness
    {
        public readonly GameWorld World = new();
        public readonly PlayerManager Pm = new();
        public readonly EditorSessionManager Editors = new();
        public readonly EditorLockRegistry Locks = new();
        public readonly CapturingDispatcher Dispatcher = new();
        public readonly RecordingPersistence Persistence = new();
        private readonly EditorPacketHandler _handler;

        public Harness(AdminLevel access = AdminLevel.Creator)
        {
            var registry = CoreRegistry.Build([new SpeciesModule()]);
            var family = registry.Schema.Family(Species)!;
            World.ModuleRecords.Declare(family, World.Limits.For(family));

            Editors.GetSession(Editor)!.IsAuthenticated = true;
            Editors.GetSession(Editor)!.AdminLevel = access;
            _handler = new EditorPacketHandler(
                World, Pm, Editors, Locks, Dispatcher, Persistence, new NoOpBackground(),
                items: null!, joinLeave: null!, quests: null!, spawn: null!,
                saver: null!, gameLoop: null!,
                NullLogger<EditorPacketHandler>.Instance, registry);
        }

        public void Send<T>(T packet) where T : IPacket
            => _handler.HandleEditorPacket(Editor, PacketSerializer.Serialize(packet));
    }

    sealed class CapturingDispatcher : IPacketDispatcher
    {
        public readonly List<IPacket> Broadcasts = new();                 // SendToAll
        public readonly List<(int Index, IPacket Packet)> Direct = new(); // SendTo

        public T OneDirect<T>() where T : IPacket
        {
            var matches = Direct.Select(d => d.Packet).OfType<T>().ToList();
            Assert.That(matches, Has.Count.EqualTo(1), $"expected exactly one {typeof(T).Name} sent to the editor");
            return matches[0];
        }

        public void SendToAll(IPacket packet) => Broadcasts.Add(packet);
        public void SendTo(int index, IPacket packet) => Direct.Add((index, packet));

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
        public void SendToEditor(int editorIndex, IPacket packet) => Direct.Add((editorIndex, packet));
        public readonly List<IPacket> AllEditors = new();
        public void SendToAllEditors(IPacket packet) => AllEditors.Add(packet);
        public void Disconnect(int index) { }
        public void DisconnectEditor(int editorIndex) { }
        public void GracefulDisconnect(int index) { }
        public void GracefulDisconnectEditor(int editorIndex) { }
    }

    sealed class NoOpBackground : IBackgroundPersistence
    {
        public void Run(Task task, string operation) { }
        public Task DrainAsync() => Task.CompletedTask;
    }
}

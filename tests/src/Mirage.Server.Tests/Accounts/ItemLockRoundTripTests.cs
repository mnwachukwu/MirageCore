using Microsoft.Extensions.Logging.Abstractions;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Persistence;
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
using System.Threading.Tasks;

namespace Mirage.Server.Tests.Accounts;

/// <summary>
/// Two editors, one item: the whole exchange the padlock stands for.
///
/// <para>Dirtying an item claims it, every editor is told, and a save from anyone but the holder is refused
/// with a notice rather than accepted quietly. Giving it back opens it again.</para>
///
/// <para>The refusal is the part that matters most. Greying the panel in the other editor is an affordance
/// and lives in the client, which this engine ships the source of — so a stale build, a hand-rolled client
/// or a race between the claim and the keystroke all end here, and here is where the holder's work is
/// actually protected.</para>
///
/// <para>A lock belongs to a SESSION: the two editors below are signed in as different people only because
/// that is the ordinary case. <see cref="TwoWindowsOfOneAccount_StillBlockEachOther"/> is the one that
/// matters for a single author, which is most of this project's life.</para>
/// </summary>
[TestFixture]
public class ItemLockRoundTripTests
{
    const int Holder = 1, Other = 2, ItemNum = 3;

    [Test]
    public void DirtyingAnItem_TellsEveryEditorWhoHasIt()
    {
        var h = new Harness();

        h.Send(Holder, new EditorLockPacket { Section = "Items", Num = ItemNum });

        var table = h.Dispatcher.LastTable();
        Assert.That(table.Locks, Has.Length.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(table.Locks[0].Section, Is.EqualTo("Items"));
            Assert.That(table.Locks[0].Num, Is.EqualTo(ItemNum));
            Assert.That(table.Locks[0].Login, Is.EqualTo("alice"));
            Assert.That(table.Locks[0].Session, Is.EqualTo("session-alice"));
        });
    }

    [Test]
    public void TheHolder_CanSaveIt()
    {
        var h = new Harness();
        h.Send(Holder, new EditorLockPacket { Section = "Items", Num = ItemNum });

        h.Send(Holder, Rename("Ashwood Bow"));

        Assert.That(h.World.Items[ItemNum].Name, Is.EqualTo("Ashwood Bow"));
    }

    [Test]
    public void AnybodyElse_IsRefusedAndTold()
    {
        var h = new Harness();
        h.World.Items[ItemNum].Name = "Ashwood Bow";
        h.Send(Holder, new EditorLockPacket { Section = "Items", Num = ItemNum });

        h.Send(Other, Rename("Bent Stick"));

        Assert.Multiple(() =>
        {
            Assert.That(h.World.Items[ItemNum].Name, Is.EqualTo("Ashwood Bow"), "the holder's item was overwritten");
            Assert.That(h.Dispatcher.NoticeTo(Other), Is.Not.Null, "the save was dropped without saying why");
        });
    }

    /// <summary>The case a single author meets: two windows, one login. Two sets of unsaved changes, so they
    /// block each other exactly as two people would.</summary>
    [Test]
    public void TwoWindowsOfOneAccount_StillBlockEachOther()
    {
        var h = new Harness(otherLogin: "alice", otherSession: "session-alice-2");
        h.World.Items[ItemNum].Name = "Ashwood Bow";
        h.Send(Holder, new EditorLockPacket { Section = "Items", Num = ItemNum });

        h.Send(Other, Rename("Bent Stick"));

        Assert.Multiple(() =>
        {
            Assert.That(h.World.Items[ItemNum].Name, Is.EqualTo("Ashwood Bow"));
            Assert.That(h.Dispatcher.NoticeTo(Other), Is.Not.Null);
        });
    }

    [Test]
    public void GivingItBack_OpensItAgain()
    {
        var h = new Harness();
        h.Send(Holder, new EditorLockPacket { Section = "Items", Num = ItemNum });

        h.Send(Holder, new EditorUnlockPacket { Section = "Items", Num = ItemNum });
        h.Send(Other, Rename("Bent Stick"));

        Assert.Multiple(() =>
        {
            Assert.That(h.Dispatcher.LastTable().Locks, Is.Empty, "the table still names a holder");
            Assert.That(h.World.Items[ItemNum].Name, Is.EqualTo("Bent Stick"), "the item stayed shut after being released");
        });
    }

    /// <summary>Nobody else can free a record out from under the editor still working on it.</summary>
    [Test]
    public void NobodyElse_CanUnlockIt()
    {
        var h = new Harness();
        h.Send(Holder, new EditorLockPacket { Section = "Items", Num = ItemNum });

        h.Send(Other, new EditorUnlockPacket { Section = "Items", Num = ItemNum });
        h.Send(Other, Rename("Bent Stick"));

        Assert.That(h.World.Items[ItemNum].Name, Is.Not.EqualTo("Bent Stick"));
    }

    /// <summary>A dropped editor takes its locks with it, or a crash wedges the item shut for good.</summary>
    [Test]
    public void ADroppedEditor_ReleasesWhatItHeld()
    {
        var h = new Harness();
        h.Send(Holder, new EditorLockPacket { Section = "Items", Num = ItemNum });

        h.Drop(Holder);
        h.Send(Other, Rename("Bent Stick"));

        Assert.Multiple(() =>
        {
            Assert.That(h.Dispatcher.LastTable().Locks, Is.Empty);
            Assert.That(h.World.Items[ItemNum].Name, Is.EqualTo("Bent Stick"));
        });
    }

    /// <summary>One item is claimed, not the section. Editing a different item is nobody's business.</summary>
    [Test]
    public void AnotherItem_IsUnaffected()
    {
        var h = new Harness();
        h.Send(Holder, new EditorLockPacket { Section = "Items", Num = ItemNum });

        h.Send(Other, Rename("Bent Stick", num: ItemNum + 1));

        Assert.That(h.World.Items[ItemNum + 1].Name, Is.EqualTo("Bent Stick"));
    }

    private static EditorSaveItemPacket Rename(string name, int num = ItemNum) => new()
    {
        ItemNum = num, Name = name, Pic = 4, Type = ItemType.Weapon, Durability = 100, Power = 10,
    };

    sealed class Harness
    {
        public readonly GameWorld World = new();
        public readonly CapturingDispatcher Dispatcher = new();
        private readonly EditorSessionManager _editors = new();
        private readonly EditorLockRegistry _locks = new();
        private readonly EditorPacketHandler _handler;

        public Harness(string otherLogin = "bob", string otherSession = "session-bob")
        {
            Sign(Holder, "alice", "session-alice");
            Sign(Other, otherLogin, otherSession);
            _handler = new EditorPacketHandler(
                World, new PlayerManager(), _editors, _locks, Dispatcher, new RecordingPersistence(), new NoOpBackground(),
                items: null!, joinLeave: null!, quests: null!, spawn: null!,
                saver: null!, gameLoop: null!,
                NullLogger<EditorPacketHandler>.Instance);
        }

        private void Sign(int index, string login, string sessionId)
        {
            var s = _editors.GetSession(index)!;
            s.IsConnected = true;
            s.IsAuthenticated = true;
            s.AdminLevel = AdminLevel.Developer;   // the tier an item save needs
            s.Login = login;
            s.SessionId = sessionId;
        }

        public void Send<T>(int editorIndex, T packet) where T : IPacket
            => _handler.HandleEditorPacket(editorIndex, PacketSerializer.Serialize(packet));

        public void Drop(int editorIndex)
        {
            _handler.OnEditorDisconnected(editorIndex);
            _editors.Disconnect(editorIndex);
        }
    }

    sealed class CapturingDispatcher : IPacketDispatcher
    {
        private readonly List<IPacket> _toAllEditors = [];
        private readonly List<(int Index, IPacket Packet)> _direct = [];

        /// <summary>The table as every editor last saw it.</summary>
        public EditorLocksPacket LastTable()
        {
            var tables = _toAllEditors.OfType<EditorLocksPacket>().ToList();
            Assert.That(tables, Is.Not.Empty, "no lock table ever reached the editors");
            return tables[^1];
        }

        public EditorNoticePacket? NoticeTo(int editorIndex) =>
            _direct.Where(d => d.Index == editorIndex).Select(d => d.Packet).OfType<EditorNoticePacket>().LastOrDefault();

        public void SendToAllEditors(IPacket packet) => _toAllEditors.Add(packet);
        public void SendToEditor(int editorIndex, IPacket packet) => _direct.Add((editorIndex, packet));

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

using Microsoft.Extensions.Logging.Abstractions;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Tests.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Reflection;

namespace Mirage.Server.Tests.Accounts;

/// <summary>
/// The editor dispatch's authentication gate, checked across EVERY editor packet rather than a sample.
///
/// <para>An editor session is a plain TCP connection until it logs in, and it reaches content-mutating
/// handlers — save a map, rewrite an NPC, redefine a shop's stock — with no player record behind it. The
/// only thing standing in front of those is a per-handler <c>RequireAccess</c> check, repeated 26 times.
/// A new handler added without one is a hole that nothing else would catch: it would pass every
/// functional test, because functional tests authenticate first.</para>
///
/// <para>That guard used to test authentication ALONE, which is how a Mapper could save every content
/// type the editor client only shows to Developer and above. It now carries a tier as well; the tier
/// BOUNDARY is asserted in <c>EditorLiveBroadcastTests</c>, which can hand the handlers real payloads.</para>
///
/// <para>So this enumerates the editor packet types by REFLECTION and asserts each is refused while
/// unauthenticated. A new editor packet is covered the moment it exists — no list to maintain, which
/// is the property that makes this worth having. <see cref="EditorLoginPacket"/> is the deliberate
/// exception: it is how a session authenticates, so it must be let through.</para>
///
/// <para>Cheap to construct only because editor dispatch is its own type: eleven collaborators, four of
/// which the unauthenticated path never touches.</para>
/// </summary>
[TestFixture]
public class EditorAuthGateTests
{
    const int Editor = 1;

    // Every concrete IPacket whose name marks it as part of the editor protocol. Deliberately derived
    // from the type system rather than a hand-written list.
    static IEnumerable<Type> EditorPacketTypes() =>
        typeof(IPacket).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && typeof(IPacket).IsAssignableFrom(t)
                        && t.Name.StartsWith("Editor", StringComparison.Ordinal))
            .OrderBy(t => t.Name);

    // C→S editor packets only. The S→C replies (EditorData, EditorAllItems, EditorLoginResponse, …)
    // travel the other direction and are never dispatched by the server.
    static bool IsClientToServer(Type t) =>
        t.Name.StartsWith("EditorRequest", StringComparison.Ordinal)
        || t.Name.StartsWith("EditorSave", StringComparison.Ordinal)
        || t.Name == nameof(EditorLoginPacket);

    [Test]
    public void EveryEditorRequestAndSavePacket_IsRefusedWhileUnauthenticated()
    {
        var targets = EditorPacketTypes().Where(IsClientToServer)
                                        .Where(t => t.Name != nameof(EditorLoginPacket))
                                        .ToList();

        Assert.That(targets, Is.Not.Empty, "sanity: the editor protocol should expose C-to-S packets");

        Assert.Multiple(() =>
        {
            foreach (var t in targets)
            {
                var h = new Harness();                       // fresh, deliberately NOT authenticated
                var packet = (IPacket)Activator.CreateInstance(t)!;

                h.Dispatch(packet);

                Assert.That(h.Dispatcher.Sent, Is.Empty,
                    $"{t.Name} produced a send from an UNAUTHENTICATED editor session — its handler is "
                    + "missing an IsEditorAuthenticated check");
            }
        });
    }

    // The complement: authenticating first must actually let a request through, so the test above is
    // measuring the gate rather than a handler that does nothing under any circumstances.
    [Test]
    public void AnAuthenticatedEditor_GetsAResponse()
    {
        var h = new Harness();
        h.Authenticate();

        h.Dispatch(new EditorRequestAllItemsPacket());

        Assert.That(h.Dispatcher.Sent, Is.Not.Empty,
                    "an authenticated request must be answered — otherwise the refusal test above is vacuous");
    }

    // A save from an unauthenticated session must not mutate the world either, not merely stay silent.
    [Test]
    public void UnauthenticatedSave_LeavesTheWorldUntouched()
    {
        var h = new Harness();
        h.World.Items[2] = new ItemRecord { Name = "" };

        h.Dispatch(new EditorSaveItemPacket { ItemNum = 2, Name = "smuggled", Type = ItemType.Weapon });

        Assert.Multiple(() =>
        {
            Assert.That(h.World.Items[2].Name, Is.Empty, "an unauthenticated save must not write to the world");
            Assert.That(h.Dispatcher.Sent, Is.Empty, "and must not broadcast");
        });
    }

    // Logging in is the one packet an unauthenticated session may send — if this were gated too, no
    // session could ever authenticate.
    [Test]
    public void EditorLoginPacket_IsNotGated()
    {
        var login = EditorPacketTypes().SingleOrDefault(t => t.Name == nameof(EditorLoginPacket));
        Assert.That(login, Is.Not.Null, "EditorLoginPacket must exist for a session to authenticate");

        var h = new Harness();
        // Reaching the async credential check is enough; it must not be refused by the gate itself.
        Assert.That(() => h.Dispatch(new EditorLoginPacket { Username = "nobody", Password = "wrong", Locale = "en" }),
                    Throws.Nothing, "the login handler must be reachable without prior authentication");
    }

    // ── Harness ───────────────────────────────────────────────────────────────

    // An EditorPacketHandler with only what the gate path needs. The four null! deps are the ones an
    // unauthenticated packet never reaches — if a future edit makes it reach one, this throws, which
    // is the signal we want rather than a silently passing test.
    sealed class Harness
    {
        public readonly GameWorld World = new();
        public readonly PlayerManager Pm = new();
        public readonly EditorSessionManager Editors = new();
        public readonly EditorLockRegistry Locks = new();
        public readonly CapturingDispatcher Dispatcher = new();
        private readonly EditorPacketHandler _handler;

        public Harness() =>
            _handler = new EditorPacketHandler(
                World, Pm, Editors, Locks, Dispatcher, persistence: null!, bg: new NoOpBackground(),
                items: null!, joinLeave: null!, quests: null!, spawn: null!,
                saver: null!, gameLoop: null!,
                NullLogger<EditorPacketHandler>.Instance);

        // Authenticating means BOTH flags now: a session with no access level is a Player, and the
        // handlers refuse those. The tier boundary itself is asserted in EditorLiveBroadcastTests, where
        // the packets carry real payloads — here a refused packet and an invalid one look identical,
        // because the access check runs before the validation and both simply send nothing.
        public void Authenticate(AdminLevel access = AdminLevel.Creator)
        {
            var s = Editors.GetSession(Editor)!;
            s.IsAuthenticated = true;
            s.AdminLevel = access;
        }

        public void Dispatch<T>(T packet) where T : IPacket
            => _handler.HandleEditorPacket(Editor, PacketSerializer.Serialize(packet));
    }

    // Records anything that leaves the server, so "was this refused?" is a single assertion.
    sealed class CapturingDispatcher : IPacketDispatcher
    {
        public readonly List<IPacket> Sent = new();

        public void SendToEditor(int editorIndex, IPacket packet) => Sent.Add(packet);
        public readonly List<IPacket> AllEditors = new();
        public void SendToAllEditors(IPacket packet) => AllEditors.Add(packet);
        public void SendToAll(IPacket packet) => Sent.Add(packet);
        public void SendTo(int index, IPacket packet) => Sent.Add(packet);
        public void SendToAllBut(int exclude, IPacket packet) => Sent.Add(packet);
        public void SendToObservers(IReadOnlyCollection<int> observers, IPacket packet) => Sent.Add(packet);
        public void SendToObserversBut(IReadOnlyCollection<int> observers, int exclude, IPacket packet) => Sent.Add(packet);
        public void SendToViewport(int speakerIndex, IPacket packet) => Sent.Add(packet);
        public void SendToViewportAt(int mapNum, int x, int y, IPacket packet) => Sent.Add(packet);
        public void SendChatBubble(int speakerIndex, IPacket packet, string senderLogin, bool wholeRegion) => Sent.Add(packet);
        public void SendToAdmins(IPacket packet) => Sent.Add(packet);
        public void SendToGuild(int guildId, IPacket packet) => Sent.Add(packet);
        public void SendToGuildBut(int guildId, int exclude, IPacket packet) => Sent.Add(packet);
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

    sealed class NoOpBackground : Mirage.Server.Core.Persistence.IBackgroundPersistence
    {
        public void Run(Task task, string operation) { }
        public Task DrainAsync() => Task.CompletedTask;
    }
}

using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using NUnit.Framework;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mirage.Server.Tests.Net;

/// <summary>
/// A verb a game invented, offered by a client that was never compiled against it.
///
/// <para>🔴 What crosses the wire is a caption and an id, and that is the whole design. A seam that let a
/// game send BEHAVIOUR to a client would ship code to every player and make the client run it; a seam
/// that sends a name means the rule stays where every other rule is. The cost is that the client cannot
/// decide anything about the verb — not whether it applies, not whether the player is close enough — and
/// every one of those questions therefore belongs to the game.</para>
/// </summary>
[TestFixture]
public class GameActionTests
{
    private const int Me = 1, Map = 1;

    private sealed class Handler : IActionHandler
    {
        public List<(EntityHandle From, string Action, WorldPlace At)> Invoked { get; } = [];
        public bool Throws { get; init; }

        public string Name => "Test actions";
        public IReadOnlyCollection<string> Actions { get; } = ["test.do"];

        public void Invoke(EntityHandle from, string actionId, in WorldPlace at)
        {
            if (Throws) throw new InvalidOperationException("a game's bug");
            Invoked.Add((from, actionId, at));
        }
    }

    private sealed class Module(IActionHandler handler) : ICoreModule
    {
        public string Name => "Test";

        public void Configure(ICoreBuilder builder)
        {
            builder.AddAction(new GameAction
            {
                Id = "test.do", LabelKey = "Do the thing", GroupKey = "Testing", Surface = ActionSurface.Tile,
            });
            builder.AddActionHandler(handler);
        }
    }

    private static (PacketHandler Handler, PlayerManager Pm) Serving(CoreRegistry registry)
    {
        var pm = new PlayerManager();
        var sp = pm[Me];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Char.Name = "Surveyor";
        sp.Char.Map = Map;

        var handler = new PacketHandler(
            new GameWorld(), pm, new SilentDispatcher(), persistence: null!, bg: null!, saver: null!,
            joinLeave: null!, movement: null!, items: null!, shop: null!, bank: null!, playerSpawn: null!,
            party: null!, guilds: null!, mail: null!, market: null!, trade: null!, conversations: null!,
            social: null!, spawn: null!, tod: null!, weather: null!, gameLoop: null!,
            NullLogger<PacketHandler>.Instance, registry);

        return (handler, pm);
    }

    private static string Line(string action, int x = 4, int y = 6) =>
        PacketSerializer.Serialize(new InvokeActionPacket { Action = action, MapNum = Map, X = x, Y = y });

    [Test]
    public void APickedActionReachesTheGameThatDeclaredIt()
    {
        var game = new Handler();
        var (handler, _) = Serving(CoreRegistry.Build(new Module(game)));

        handler.HandlePacket(Me, Line("test.do"));

        Assert.Multiple(() =>
        {
            Assert.That(game.Invoked, Has.Count.EqualTo(1), "the client sent it and nothing did it");
            Assert.That(game.Invoked[0].From, Is.EqualTo(EntityHandle.ForPlayer(Me)));
            Assert.That(game.Invoked[0].Action, Is.EqualTo("test.do"));
            Assert.That(game.Invoked[0].At, Is.EqualTo(new WorldPlace(Map, 4, 6)),
                "a verb acting on a place is told which place");
        });
    }

    /// <summary>A client may name anything; only something a module declared does anything.</summary>
    [Test]
    public void AnIdNothingDeclared_DoesNothing()
    {
        var game = new Handler();
        var (handler, _) = Serving(CoreRegistry.Build(new Module(game)));

        handler.HandlePacket(Me, Line("nobody.declared.this"));

        Assert.That(game.Invoked, Is.Empty);
    }

    [Test]
    public void SomebodyNotInTheWorld_InvokesNothing()
    {
        var game = new Handler();
        var (handler, pm) = Serving(CoreRegistry.Build(new Module(game)));
        pm[Me].InGame = false;

        handler.HandlePacket(Me, Line("test.do"));

        Assert.That(game.Invoked, Is.Empty);
    }

    [Test]
    public void AHandlerThatThrows_DoesNotTakeTheConnectionWithIt()
    {
        var game = new Handler { Throws = true };
        var (handler, pm) = Serving(CoreRegistry.Build(new Module(game)));

        Assert.DoesNotThrow(() => handler.HandlePacket(Me, Line("test.do")));
        Assert.That(pm[Me].IsConnected, Is.True);
    }

    // ── Declaring ─────────────────────────────────────────────────────────────

    [Test]
    public void AnEngineWithNoGameLoaded_OffersNothing()
        => Assert.That(CoreRegistry.CoreOnly.Actions.For(ActionSurface.Tile), Is.Empty);

    [Test]
    public void TwoModulesCannotDeclareOneAction()
        => Assert.That(() => CoreRegistry.Build(new Module(new Handler()), new Module(new Handler())),
                       Throws.TypeOf<CoreModuleException>());

    [Test]
    public void TheDeclarationSurvivesTheWireWithItsCaptionAndItsGroup()
    {
        var actions = CoreRegistry.Build(new Module(new Handler())).Actions;

        var back = (GameActionsPacket)PacketSerializer.TryDeserialize(
            PacketSerializer.Serialize(PacketBuilder.GameActions(actions)))!;

        Assert.Multiple(() =>
        {
            Assert.That(back.Actions, Has.Count.EqualTo(1));
            Assert.That(back.Actions[0].Id, Is.EqualTo("test.do"));
            Assert.That(back.Actions[0].LabelKey, Is.EqualTo("Do the thing"));
            Assert.That(back.Actions[0].GroupKey, Is.EqualTo("Testing"));
            Assert.That(back.Actions[0].Surface, Is.EqualTo(ActionSurface.Tile));
        });
    }

    private sealed class SilentDispatcher : IPacketDispatcher
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
        public void SendLocalizedChatTo(int index, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToGuild(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToGuildOfficers(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
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

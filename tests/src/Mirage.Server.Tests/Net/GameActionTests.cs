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
        public List<(EntityHandle From, string Action, EntityHandle On, WorldPlace At)> Invoked { get; } = [];
        public bool Throws { get; init; }

        public string Name => "Test actions";
        public IReadOnlyCollection<string> Owns { get; init; } = ["test.do"];
        public IReadOnlyCollection<string> Actions => Owns;

        public void Invoke(EntityHandle from, string actionId, EntityHandle on, in WorldPlace at)
        {
            if (Throws) throw new InvalidOperationException("a game's bug");
            Invoked.Add((from, actionId, on, at));
        }
    }

    private sealed class Module(IActionHandler handler, ActionCondition when = default) : ICoreModule
    {
        public ActionCondition When { get; } = when;
        public string Name => "Test";

        public void Configure(ICoreBuilder builder)
        {
            builder.AddAction(new GameAction
            {
                Id = "test.do", LabelKey = "Do the thing", GroupKey = "Testing",
                Surface = ActionSurface.Tile, When = When,
            });
            builder.AddActionHandler(handler);
        }
    }

    private static (PacketHandler Handler, PlayerManager Pm, GameWorld World) Serving(
        CoreRegistry registry)
    {
        var pm = new PlayerManager();
        var sp = pm[Me];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Char.Name = "Surveyor";
        sp.Char.Map = Map;

        var world = new GameWorld();
        var handler = new PacketHandler(
            world, pm, new SilentDispatcher(), persistence: null!, bg: null!, saver: null!,
            joinLeave: null!, movement: null!, items: null!, shop: null!, bank: null!, playerSpawn: null!,
            party: null!, guilds: null!, mail: null!, market: null!, trade: null!, conversations: null!,
            social: null!, spawn: null!, tod: null!, weather: null!, gameLoop: null!,
            NullLogger<PacketHandler>.Instance, registry);

        return (handler, pm, world);
    }

    /// <summary>Somebody else, standing in the world, for a verb to be used on.</summary>
    private static void AlsoPlaying(PlayerManager pm, int index, string name)
    {
        var them = pm[index];
        them.IsConnected = true;
        them.InGame = true;
        them.CharNum = 1;
        them.Char.Name = name;
        them.Char.Map = Map;
    }

    private static string Line(string action, int x = 4, int y = 6, string on = "", int npcSlot = 0) =>
        PacketSerializer.Serialize(new InvokeActionPacket
        {
            Action = action, MapNum = Map, X = x, Y = y, TargetName = on, NpcSlot = npcSlot,
        });

    [Test]
    public void APickedActionReachesTheGameThatDeclaredIt()
    {
        var game = new Handler();
        var (handler, _, _) = Serving(CoreRegistry.Build(new Module(game)));

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

    /// <summary>A verb used on somebody names them, and one used on nowhere in particular names nobody.
    ///
    /// <para>The client sends a NAME and the game receives a handle: a game never handles a roster slot,
    /// so a slot reused between the click and the read cannot land the verb on a stranger.</para>
    /// </summary>
    [Test]
    public void AVerbUsedOnAPlayer_NamesThem()
    {
        var game = new Handler();
        var (handler, pm, _) = Serving(CoreRegistry.Build(new Module(game)));
        AlsoPlaying(pm, 2, "Quarry");

        handler.HandlePacket(Me, Line("test.do", on: "Quarry"));
        handler.HandlePacket(Me, Line("test.do"));

        Assert.Multiple(() =>
        {
            Assert.That(game.Invoked[0].On, Is.EqualTo(EntityHandle.ForPlayer(2)));
            Assert.That(game.Invoked[1].On, Is.EqualTo(EntityHandle.None),
                "a verb offered on a square is used on nobody");
        });
    }

    /// <summary>A name nobody answers to is nobody, not a refusal.
    ///
    /// <para>A target that logged out between the click and the read is an ordinary thing to happen, and
    /// what the verb should do about it is the game's answer rather than Core's.</para></summary>
    [Test]
    public void AVerbUsedOnSomebodyWhoLeft_StillReachesTheGame()
    {
        var game = new Handler();
        var (handler, _, _) = Serving(CoreRegistry.Build(new Module(game)));

        handler.HandlePacket(Me, Line("test.do", on: "Ghost"));

        Assert.Multiple(() =>
        {
            Assert.That(game.Invoked, Has.Count.EqualTo(1), "the verb still runs");
            Assert.That(game.Invoked[0].On, Is.EqualTo(EntityHandle.None));
        });
    }

    /// <summary>🔴 The condition is the SERVER's rule, not a hint to the client.
    ///
    /// <para>The client greys the entry out, and a client that did not — an old one, a modified one —
    /// still gets nowhere. A predicate only the client enforced would be a game's own declaration that
    /// anybody could opt out of, and the game would never learn it had happened.</para></summary>
    [Test]
    public void AVerbWhoseConditionDoesNotHold_IsRefusedByTheServer()
    {
        var game = new Handler();
        var (handler, pm, _) = Serving(CoreRegistry.Build(
            new Module(game, ActionCondition.AtLeast("test.tokens", 1))));

        handler.HandlePacket(Me, Line("test.do"));
        Assert.That(game.Invoked, Is.Empty, "carrying none of it, so the verb does not run");

        pm[Me].Char.Attributes.Set("test.tokens", 1);
        handler.HandlePacket(Me, Line("test.do"));

        Assert.That(game.Invoked, Has.Count.EqualTo(1), "and it runs once they are carrying one");
    }

    /// <summary>An id a handler owns but nothing declared never reaches the game.
    ///
    /// <para>No client could have offered it, so the only way it arrives is a hand-written packet — and
    /// running it would mean a verb with no declaration, and therefore no condition, being the one thing
    /// that cannot be gated.</para></summary>
    [Test]
    public void AnIdWithAHandlerButNoDeclaration_DoesNothing()
    {
        var game = new Handler { Owns = ["test.undeclared"] };
        var (handler, _, _) = Serving(CoreRegistry.Build(new Module(game)));

        handler.HandlePacket(Me, Line("test.undeclared"));

        Assert.That(game.Invoked, Is.Empty);
    }

    /// <summary>A client may name anything; only something a module declared does anything.</summary>
    [Test]
    public void AnIdNothingDeclared_DoesNothing()
    {
        var game = new Handler();
        var (handler, _, _) = Serving(CoreRegistry.Build(new Module(game)));

        handler.HandlePacket(Me, Line("nobody.declared.this"));

        Assert.That(game.Invoked, Is.Empty);
    }

    [Test]
    public void SomebodyNotInTheWorld_InvokesNothing()
    {
        var game = new Handler();
        var (handler, pm, _) = Serving(CoreRegistry.Build(new Module(game)));
        pm[Me].InGame = false;

        handler.HandlePacket(Me, Line("test.do"));

        Assert.That(game.Invoked, Is.Empty);
    }

    [Test]
    public void AHandlerThatThrows_DoesNotTakeTheConnectionWithIt()
    {
        var game = new Handler { Throws = true };
        var (handler, pm, _) = Serving(CoreRegistry.Build(new Module(game)));

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

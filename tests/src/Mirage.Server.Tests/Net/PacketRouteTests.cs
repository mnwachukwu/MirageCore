using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Extensibility;
using Mirage.Shared.Protocol;
using NUnit.Framework;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mirage.Server.Tests.Net;

/// <summary>
/// A module's own command, from the line on the socket to the module that owns it.
///
/// <para>🔴 Registering a command and handling it are two halves, and the missing one is silent. A
/// command registered with no route deserializes correctly, passes every gate, and is delivered to
/// nobody — no error, no log, and a game whose messages do nothing for a reason no log will ever
/// name.</para>
///
/// <para>The whole path is walked here rather than asserted a piece at a time, because every piece of it
/// passes on its own while the thing as a whole does nothing.</para>
/// </summary>
[TestFixture]
public class PacketRouteTests
{
    private const int Me = 1;

    public sealed record PingPacket : IPacket
    {
        public const string Command = "test.ping";

        [JsonPropertyName("cmd")] public string Cmd => Command;

        [JsonPropertyName("n")] public int N { get; init; }
    }

    private sealed class Route : IPacketRoute
    {
        public List<(EntityHandle From, int N)> Heard { get; } = [];
        public bool Dead { get; init; }
        public bool Throws { get; init; }

        public string Name => "Test route";
        public IReadOnlyCollection<string> Commands { get; } = [PingPacket.Command];
        public bool AllowedWhileDead => Dead;

        public void Handle(EntityHandle from, IPacket packet)
        {
            if (Throws) throw new InvalidOperationException("a game's bug");
            if (packet is PingPacket p) Heard.Add((from, p.N));
        }
    }

    private sealed class Module(IPacketRoute route) : ICoreModule
    {
        public string Name => "Test";

        public void Configure(ICoreBuilder builder)
        {
            builder.Packets.Register<PingPacket>(PingPacket.Command);
            builder.AddPacketRoute(route);
        }
    }

    /// <summary>The handler, holding only what the route path reaches. Everything else is null because
    /// a module command never touches it — which is itself worth knowing about the seam.</summary>
    private static (PacketHandler Handler, PlayerManager Pm) Serving(CoreRegistry registry)
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        PacketSerializer.Registry = registry.Packets;

        var sp = pm[Me];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Char.Name = "Surveyor";
        sp.Char.Map = 1;

        var handler = new PacketHandler(
            world, pm, new SilentDispatcher(), persistence: null!, bg: null!, saver: null!,
            joinLeave: null!, movement: null!, items: null!, shop: null!, bank: null!, playerSpawn: null!,
            party: null!, guilds: null!, mail: null!, market: null!, trade: null!, conversations: null!,
            social: null!, spawn: null!, tod: null!, weather: null!, gameLoop: null!,
            NullLogger<PacketHandler>.Instance, registry);

        return (handler, pm);
    }

    [TearDown]
    public void RestoreRegistry() => PacketSerializer.Registry = CorePackets.Build();

    private static string Line(int n) => PacketSerializer.Serialize(new PingPacket { N = n });

    [Test]
    public void ARegisteredCommandReachesTheModuleThatOwnsIt()
    {
        var route = new Route();
        var (handler, _) = Serving(CoreRegistry.Build(new Module(route)));

        handler.HandlePacket(Me, Line(7));

        Assert.Multiple(() =>
        {
            Assert.That(route.Heard, Has.Count.EqualTo(1), "the packet parsed and was delivered to nobody");
            Assert.That(route.Heard[0].N, Is.EqualTo(7), "the line's own fields reached the module");
            Assert.That(route.Heard[0].From, Is.EqualTo(EntityHandle.ForPlayer(Me)),
                "a route is told who sent it, by the same handle every other seam uses");
        });
    }

    /// <summary>🔴 The corpse rule is an allow-list for Core's own commands, and a module's inherit that
    /// default. A game that means a dead player may still send something says so; one that says nothing
    /// gets the safe answer rather than the convenient one.</summary>
    [Test]
    public void ADeadPlayersCommandIsRefusedUnlessTheGameSaysOtherwise()
    {
        var refused = new Route();
        var (handler, pm) = Serving(CoreRegistry.Build(new Module(refused)));
        pm[Me].Char.Downed = true;

        handler.HandlePacket(Me, Line(1));

        Assert.That(refused.Heard, Is.Empty);
    }

    [Test]
    public void AGameThatAllowsItWhileDead_IsStillDelivered()
    {
        var allowed = new Route { Dead = true };
        var (handler, pm) = Serving(CoreRegistry.Build(new Module(allowed)));
        pm[Me].Char.Downed = true;

        handler.HandlePacket(Me, Line(1));

        Assert.That(allowed.Heard, Has.Count.EqualTo(1));
    }

    /// <summary>A game's bug drops its own packet and nothing else. The connection survives, because a
    /// player whose game module threw has done nothing wrong.</summary>
    [Test]
    public void ARouteThatThrows_DoesNotTakeTheConnectionWithIt()
    {
        var route = new Route { Throws = true };
        var (handler, pm) = Serving(CoreRegistry.Build(new Module(route)));

        Assert.DoesNotThrow(() => handler.HandlePacket(Me, Line(1)));
        Assert.That(pm[Me].IsConnected, Is.True);
    }

    // ── Declaring ─────────────────────────────────────────────────────────────

    [Test]
    public void TwoModulesCannotOwnOneCommand()
    {
        Assert.That(() => CoreRegistry.Build(new Module(new Route()), new Module(new Route())),
                    Throws.TypeOf<CoreModuleException>());
    }

    [Test]
    public void ARouteOwningNothingIsRefused()
    {
        var empty = new EmptyRoute();

        Assert.That(() => CoreRegistry.Build(new Module(empty)), Throws.TypeOf<CoreModuleException>());
    }

    [Test]
    public void AnEngineWithNoGameLoaded_RoutesNothing()
        => Assert.That(CoreRegistry.CoreOnly.PacketRoutes.For(PingPacket.Command), Is.Null);

    private sealed class EmptyRoute : IPacketRoute
    {
        public string Name => "Owns nothing";
        public IReadOnlyCollection<string> Commands { get; } = [];
        public void Handle(EntityHandle from, IPacket packet) { }
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

using Mirage.Shared.Protocol;

namespace Mirage.Shared.Extensibility;

/// <summary>
/// Where a module's own packets go.
///
/// <para><b>Registering a command and handling it are two halves.</b> A command registered on
/// <see cref="ICoreBuilder.Packets"/> can be READ — a line naming it deserializes into the module's own
/// type instead of being dropped. That is the whole of what registration buys. Without a route, the
/// packet then arrives, parses, and is delivered to nobody: no error, no log, and a game whose messages
/// silently do nothing.</para>
///
/// <para><b>A route owns commands, rather than being offered everything.</b> Dispatch is a lookup by
/// command, so two modules cannot both half-handle a packet and a route is never asked about something
/// that is not its business. Two routes claiming one command is refused at startup.</para>
///
/// <para>Runs on the game thread, inside the read that raised it, so a route does its work and returns.
/// A route that throws is logged with its name and the connection survives — a game's bug does not drop
/// a player.</para>
/// </summary>
public interface IPacketRoute
{
    /// <summary>What this route is called, for the log line that names it when it throws.</summary>
    string Name { get; }

    /// <summary>The commands it owns. Each must also be registered on
    /// <see cref="ICoreBuilder.Packets"/>, or nothing will ever deserialize into a packet this route
    /// could be handed.</summary>
    IReadOnlyCollection<string> Commands { get; }

    /// <summary>Whether a dead player's copy of these packets is still delivered.
    ///
    /// <para><b>False by default, deliberately.</b> Core's own list of what survives a death is
    /// an allow-list precisely so a command added later is refused rather than permitted by whoever
    /// forgot to guard it. A game that means it says so here.</para></summary>
    bool AllowedWhileDead => false;

    /// <summary>Handle one packet from one player.
    ///
    /// <para><b>Core gates the connection, not the command.</b> The flood limits and the corpse rule
    /// stand between a line and this method; who may do what is the game's own question, and Core has no
    /// opinion about the access a module's commands need.</para></summary>
    void Handle(EntityHandle from, IPacket packet);
}

/// <summary>
/// Every module route, indexed by the command it owns.
///
/// <para>Built once at load because the question is asked on every inbound line.</para>
/// </summary>
public sealed class PacketRoutes
{
    /// <summary>No module routes anything. What Core alone describes, and then every command belongs to
    /// Core's own handler.</summary>
    public static readonly PacketRoutes Empty = new([]);

    private readonly Dictionary<string, IPacketRoute> _byCommand;

    public PacketRoutes(IReadOnlyList<IPacketRoute> routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        Routes = routes;
        _byCommand = new Dictionary<string, IPacketRoute>(StringComparer.Ordinal);
        foreach (var route in routes)
            foreach (string command in route.Commands)
                _byCommand[command] = route;
    }

    /// <summary>The routes, in the order their modules were configured.</summary>
    public IReadOnlyList<IPacketRoute> Routes { get; }

    public int Count => Routes.Count;

    /// <summary>The route owning <paramref name="command"/>, or null when it belongs to Core.</summary>
    public IPacketRoute? For(string? command)
        => command is not null && _byCommand.TryGetValue(command, out var route) ? route : null;
}

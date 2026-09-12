using Mirage.Shared.Extensibility;

namespace Mirage.Server.Host;

/// <summary>
/// <b>The modules this server loads. This is where a game plugs into the engine.</b>
///
/// <para>Add an <see cref="ICoreModule"/> to the list below and everything it declares — its record
/// families, its attribute keys, its packet commands, its work on the tick — is part of the server.
/// Nothing else in Core has to be edited to make room for it, and nothing in Core names it.</para>
///
/// <para><b>The list is explicit rather than discovered.</b> Scanning assemblies for modules would make
/// load order an accident of type ordering and make a missing module look like a feature that silently
/// did nothing. Modules are configured in the order written here, and two of them claiming the same
/// family id, attribute key or packet command stops the server at startup with the second one named.</para>
///
/// <para>An empty list is the engine by itself: a server that runs, accepts players, and moves them
/// around a world with no game rules in it at all. That is a supported configuration, and it is what
/// this ships as.</para>
/// </summary>
public static class GameModules
{
    /// <summary>The modules to load, in order.</summary>
    public static IReadOnlyList<ICoreModule> Load() => [];
}

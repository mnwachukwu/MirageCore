using Mirage.Server.Host.Scripting;
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
/// <para><b>An empty list is the engine by itself</b>: a server that runs, accepts players, and moves
/// them around a world with no game rules in it at all. That is a supported configuration and the one to
/// start a new game from — delete the entry below, drop the project reference beside it, and the server
/// is Core alone.</para>
///
/// <para><b>Nothing ships in this list, and that is the shipped configuration.</b> The game this
/// source carries — Survey, a small game about cataloguing plants — is a SCRIPT rather than an
/// assembly: it is content the world folder carries, read by <see cref="ScriptedWorldModule"/>, so it
/// changes without a rebuild and ships without a toolchain. <c>modules/survey/</c> holds the same game
/// written the other way, as the worked example for the compiled route; it is built and tested and
/// deliberately not loaded, because a game declared twice would collide with itself over every
/// attribute key it owns.</para>
///
/// <para><b>Both routes are supported and neither is the poor cousin.</b> A module written here is
/// checked by the compiler and stepped through in a debugger, which is what somebody building a large
/// game from this source wants. See <c>modules/README.md</c>.</para>
/// </summary>
public static class GameModules
{
    /// <summary>The modules to load, in order.</summary>
    /// <param name="worldDir">The world being served, whose <c>scripts/</c> folder is a module of its own.</param>
    public static IReadOnlyList<ICoreModule> Load(string worldDir) =>
    [
        // Last, so a world's own rules are told about things after any compiled game has had its say.
        // A world with no scripts loads this and it does nothing.
        new ScriptedWorldModule(worldDir),
    ];
}

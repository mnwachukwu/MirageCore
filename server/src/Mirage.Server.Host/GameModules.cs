using Mirage.Modules.Survey;
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
/// <para>What ships here is the Survey module, a small game about cataloguing plants. It is LOADED rather
/// than merely provided, because a seam only a test has ever run is a seam nobody has run.</para>
///
/// <para><b>This is the compile-time route, and it is not the only one intended.</b> A module written here
/// is checked by the compiler and stepped through in a debugger, which is what somebody building a game
/// from this source wants. A game that is a SCRIPT needs none of this list: it is content the host reads,
/// so it changes without a rebuild and ships without a toolchain. See <c>modules/README.md</c>.</para>
/// </summary>
public static class GameModules
{
    /// <summary>The modules to load, in order.</summary>
    public static IReadOnlyList<ICoreModule> Load() => [new SurveyModule()];
}

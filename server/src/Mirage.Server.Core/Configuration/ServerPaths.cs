using Mirage.Shared;

namespace Mirage.Server.Core.Configuration;

/// <summary>
/// Where the server keeps state that has to outlive its own binaries, and the one place the two
/// configurable folders are resolved.
///
/// <para>🔴 The defaults are per-user directories, NOT folders beside the executable. An installed
/// server runs out of a Velopack <c>current/</c> folder, and applying an update replaces that folder
/// wholesale with the contents of the new package — anything the server wrote into it is gone. A
/// default data dir under it therefore held the accounts for exactly one version, and the identity
/// for exactly one version, which showed up as every pinned client refusing to connect. The same
/// folder is a read-only mount when the server runs from an AppImage or a <c>.app</c>.</para>
///
/// <para>The client and editor anchor their writable state the same way — see <c>AppPaths</c> and
/// <c>EditorPaths</c>. This is the server's version of that, and it lives in Core rather than in the
/// host so the shell resolves the same folders the host does: a scratch server that guessed at the
/// data dir separately would point at one the server had stopped using.</para>
/// </summary>
public static class ServerPaths
{
    // Its own name, so the host's state stays separate from the shell's ("… Server Shell") and the
    // client's. The host and the shell ship in one installer but they are different apps.
    private static readonly UserPaths Paths = new($"{Constants.GameName} Server");

    /// <summary>Absolute path under the server's per-user data dir. Creates nothing.</summary>
    public static string Data(params string[] parts) => Paths.Data(parts);

    /// <summary>Where THIS INSTALLATION's state lives: the configured folder, or the per-user default.</summary>
    public static string ResolveDataDir(ServerConfig config) =>
        config.DataDir is { Length: > 0 } configured ? configured : Data("data");

    /// <summary>Where the world lives: the configured folder, or the per-user default.</summary>
    public static string ResolveWorldDir(ServerConfig config) =>
        config.WorldDir is { Length: > 0 } configured ? configured : Data("world");
}

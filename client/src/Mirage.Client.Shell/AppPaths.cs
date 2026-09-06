using Mirage.Shared;

namespace Mirage.Client.Shell;

/// <summary>
/// Centralizes filesystem locations so the client works regardless of its current working
/// directory. Launched as a Linux AppImage or a macOS .app (e.g. from Steam or a desktop
/// shortcut), the working directory is wherever the launcher sat — not the install dir — so any
/// path resolved relative to the CWD lands in the wrong place. Three anchors keep this honest:
///
///   - <see cref="Asset"/>: read-only content shipped inside the application bundle (music,
///     graphics, lang files). Anchored to <see cref="AppContext.BaseDirectory"/>, i.e. the
///     executable's own folder. On an AppImage/.app that folder is a read-only mount.
///   - <see cref="Config"/>: per-user writable settings (appsettings.json, account configs).
///   - <see cref="Cache"/>: per-user writable, regenerable data (the downloaded map cache).
///
/// The per-user config/cache locations are resolved by the shared <see cref="UserPaths"/> (the
/// platform conventions live there). Writable state can't live next to the executable because the
/// bundle is read-only on an AppImage/.app (and writing into a macOS bundle breaks its signature).
/// </summary>
public static class AppPaths
{
    private static readonly UserPaths Paths = new(Constants.GameName);

    /// <summary>Absolute path to a read-only bundled asset, e.g. <c>Asset("assets", "music", "music1.ogg")</c>.</summary>
    public static string Asset(params string[] relativeParts) => Combine(AppContext.BaseDirectory, relativeParts);

    /// <summary>Absolute path under the per-user writable config dir (settings, account configs).</summary>
    public static string Config(params string[] relativeParts) => Paths.Config(relativeParts);

    /// <summary>Absolute path under the per-user writable cache dir (regenerable data, e.g. maps).</summary>
    public static string Cache(params string[] relativeParts) => Paths.Cache(relativeParts);

    private static string Combine(string root, string[] parts)
    {
        if (parts.Length == 0) return root;
        var all = new string[parts.Length + 1];
        all[0] = root;
        Array.Copy(parts, 0, all, 1, parts.Length);
        return Path.Combine(all);
    }
}

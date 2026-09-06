using System.Runtime.InteropServices;

namespace Mirage.Shared;

/// <summary>The kind of per-user directory, which maps to a different OS location per platform.</summary>
public enum UserDirectoryKind
{
    /// <summary>Settings. Win <c>%AppData%</c> (roaming), macOS Application Support, Linux <c>$XDG_CONFIG_HOME</c>.</summary>
    Config,
    /// <summary>Persistent app data. Win <c>%LocalAppData%</c>, macOS Application Support, Linux <c>$XDG_DATA_HOME</c>.</summary>
    Data,
    /// <summary>Regenerable cache. Win <c>%LocalAppData%</c>, macOS Caches, Linux <c>$XDG_CACHE_HOME</c>.</summary>
    Cache,
}

/// <summary>
/// Resolves per-user, per-application writable directories following each OS's conventions
/// (Windows known folders, macOS <c>~/Library</c>, the Linux XDG Base Directory spec). Shared by
/// the client and editor so this platform logic lives in exactly one place; each app constructs its
/// own instance with its own name (and thus its own per-user folder).
/// </summary>
public sealed class UserPaths
{
    private readonly string _appName;   // human-readable, for the Windows/macOS folder name
    private readonly string _xdgName;   // lowercase, dash-separated, for the Linux/XDG folder name

    /// <summary>
    /// An absolute directory that replaces every per-user root, for a test run or a portable install.
    /// Each kind gets its own subfolder under it, so Config and Data stay as separate as they are on the
    /// OS. Null — the default — resolves the real per-user locations.
    ///
    /// <para>🔴 Set this BEFORE anything reads a path. Settings are cached on first access, so a single
    /// earlier read pins the real file for the rest of the process.</para>
    ///
    /// <para>Tests need it because the editor's own settings are a live user document: a test that opens
    /// or creates a world writes the folder it used into <c>RecentWorlds</c> and <c>LastWorldBrowsePath</c>
    /// and saves. Run against the real file, the suite fills the recent-worlds menu with dead temp
    /// directories and points the next Open World at whichever one it happened to make last.</para>
    /// </summary>
    public static string? RootOverride { get; set; }

    public UserPaths(string appName)
    {
        _appName = appName;
        // Linux/XDG dirs use a lowercase, dash-separated name by convention; Windows and macOS
        // per-user folders conventionally carry the human-readable name (spaces and capitals).
        _xdgName = appName.Replace(' ', '-').ToLowerInvariant();
    }

    /// <summary>Absolute path under the per-user config dir (settings).</summary>
    public string Config(params string[] parts) => Combine(Root(UserDirectoryKind.Config), parts);

    /// <summary>Absolute path under the per-user data dir (persistent app data).</summary>
    public string Data(params string[] parts) => Combine(Root(UserDirectoryKind.Data), parts);

    /// <summary>Absolute path under the per-user cache dir (regenerable data).</summary>
    public string Cache(params string[] parts) => Combine(Root(UserDirectoryKind.Cache), parts);

    private string Root(UserDirectoryKind kind)
    {
        if (RootOverride is { Length: > 0 } overrideRoot)
            return Path.Combine(overrideRoot, kind.ToString().ToLowerInvariant(), _appName);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Roaming (%AppData%) for settings that should follow the user; Local (%LocalAppData%)
            // for the bulkier, machine-local data and cache.
            var folder = kind == UserDirectoryKind.Config
                ? Environment.SpecialFolder.ApplicationData
                : Environment.SpecialFolder.LocalApplicationData;
            return Path.Combine(Environment.GetFolderPath(folder), _appName);
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // macOS keeps settings and app data under Application Support; only caches go in Caches.
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string library = kind == UserDirectoryKind.Cache ? "Caches" : "Application Support";
            return Path.Combine(home, "Library", library, _appName);
        }
        // Linux and other Unix: follow the XDG Base Directory spec, honoring the env override.
        (string envVar, string fallback) = kind switch
        {
            UserDirectoryKind.Config => ("XDG_CONFIG_HOME", ".config"),
            UserDirectoryKind.Cache => ("XDG_CACHE_HOME", ".cache"),
            _ => ("XDG_DATA_HOME", Path.Combine(".local", "share")),
        };
        string root = Environment.GetEnvironmentVariable(envVar) is { Length: > 0 } overridden
            ? overridden
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), fallback);
        return Path.Combine(root, _xdgName);
    }

    private static string Combine(string root, string[] parts)
    {
        if (parts.Length == 0) return root;
        var all = new string[parts.Length + 1];
        all[0] = root;
        Array.Copy(parts, 0, all, 1, parts.Length);
        return Path.Combine(all);
    }
}

using Mirage.Shared;

namespace Mirage.Editor;

/// <summary>
/// Filesystem locations for the editor, resolved against the install dir or the per-user
/// config/data dirs — never the current working directory. Launched as a Linux AppImage or a
/// macOS .app (e.g. from Steam or a desktop shortcut), the CWD is the launcher's dir, not the
/// install dir, so CWD-relative paths land in the wrong place. The per-user config/data locations
/// are resolved by the shared <see cref="UserPaths"/>; the editor just supplies its own app name
/// so it gets its own per-user folder, distinct from the client's.
/// </summary>
internal static class EditorPaths
{
    private static readonly UserPaths Paths = new($"{Constants.GameName} Editor");

    /// <summary>
    /// Editable tile/sprite/item art. Operator-configurable via the AssetsDir setting; defaults
    /// to the per-user data dir so users can replace the shipped sheets or add their own. Populated
    /// from the bundled defaults by <see cref="SeedAssets"/>. A relative configured path is anchored
    /// to the install dir (never the CWD).
    ///
    /// <para>It holds the sheet folders and the recycle bin directly. The game nests its art under a
    /// <c>graphics/</c> folder because it also carries music and interface art beside it; the editor
    /// reads only sheets, so that level would be one folder deep on the way to everything.</para>
    /// </summary>
    public static string Assets
    {
        get
        {
            string? configured = AppSettings.Current.AssetsDir;
            return string.IsNullOrWhiteSpace(configured)
                ? Paths.Data("assets")
                : Path.GetFullPath(configured, AppContext.BaseDirectory);
        }
    }

    /// <summary>Where deleted sheets go: alongside the sheet folders, inside the assets dir.</summary>
    public static string RecycleBin => RecycleBinFor(Assets);

    /// <summary>The bin that belongs to one assets folder.</summary>
    internal static string RecycleBinFor(string assetsDir) =>
        Path.Combine(assetsDir, Services.SheetLibrary.RecycleFolder);

    /// <summary>The editor's per-user config dir (holds its appsettings.json).</summary>
    public static string Config => Paths.Config();

    /// <summary>The editor's log files. Under the DATA dir rather than config: logs are bulky and belong to
    /// the machine they were produced on, so on Windows they stay in %LocalAppData% and out of the roaming
    /// profile.</summary>
    public static string Logs => Paths.Data("logs");

    /// <summary>
    /// The open world: a directory holding maps/, npcs/, items/ and the rest.
    ///
    /// <para>Empty until one is opened. The editor keeps no world of its own and writes none into the
    /// per-user data dir — a world lives wherever the person editing it put it, and several can sit side by
    /// side. Only settings, logs and the editable graphics are the editor's own.</para>
    /// </summary>
    public static string Data { get; private set; } = "";

    /// <summary>Whether a world is open. Everything that reads records is meaningless until it is.</summary>
    public static bool HasWorld => Data.Length > 0 && Directory.Exists(Data);

    /// <summary>Points the editor at a world folder, or at nothing when given an empty path.</summary>
    public static void OpenWorld(string path) =>
        Data = string.IsNullOrWhiteSpace(path) ? "" : Path.GetFullPath(path);

    /// <summary>The world shipped beside the executable. Not copied anywhere and never opened on its own —
    /// it is where the folder picker starts, so a first run has something to open.</summary>
    public static string BundledWorld => Path.Combine(AppContext.BaseDirectory, "seed-world");

    // The bundled default graphics shipped next to the executable (read-only on AppImage/.app);
    // the seed source for the editable assets dir.
    internal static string BundledAssets => Path.Combine(AppContext.BaseDirectory, "assets");

    /// <summary>
    /// Ensures the editable assets dir holds the bundled defaults. Copies each bundled file that is
    /// missing at the destination (per-file, if-missing): populates on first run, fills in new
    /// defaults from an app update, and never overwrites a sheet the user has replaced or added.
    /// </summary>
    public static void SeedAssets() => SeedAssetsFrom(BundledAssets, Assets);

    /// <summary>The seeding rule itself, over explicit folders so it can be exercised without an install
    /// beside it.</summary>
    internal static void SeedAssetsFrom(string source, string dest)
    {
        if (!Directory.Exists(source)) return;

        // Sheets deliberately deleted through the asset manager. Without this the seeder undoes every one
        // of them: it restores whatever is missing, so a shipped sheet moved to the recycle bin is back
        // before anyone sees it gone.
        var tombstoned = Services.SheetLibrary.ReadTombstones(RecycleBinFor(dest));

        foreach (string srcFile in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            try
            {
                // Recorded with forward slashes so a world deleted on one platform stays deleted on another.
                string relative = Path.GetRelativePath(source, srcFile).Replace('\\', '/');
                if (tombstoned.Contains(relative)) continue;
                string destFile = Path.Combine(dest, relative);
                if (File.Exists(destFile)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
                File.Copy(srcFile, destFile);
            }
            catch { /* best-effort seeding; a missing sheet just won't render */ }
        }
    }
}

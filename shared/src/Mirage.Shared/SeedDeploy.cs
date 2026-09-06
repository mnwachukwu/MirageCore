namespace Mirage.Shared;

/// <summary>
/// Lays a shipped folder down on a machine that has none.
///
/// <para>A package carries its content as <c>seed-world/</c> and <c>seed-data/</c> — each named for the
/// folder it becomes, and neither named the folder itself, so nothing an installer or an update writes can
/// land on top of an authored world or a running server's state. Putting either into play is this one copy,
/// made once.</para>
/// </summary>
public static class SeedDeploy
{
    /// <summary>
    /// Copies one shipped file to <paramref name="targetFile"/>, and only when the target does NOT EXIST.
    /// Returns true when it laid one down.
    ///
    /// <para>The file equivalent of <see cref="SeedIfAbsent"/>, for the operator-facing config files: they
    /// ship as defaults beside the executable and become the installation's own on first run. An existing
    /// file is never touched — including an empty one, for the same reason an empty world dir is left
    /// alone.</para>
    /// </summary>
    public static bool SeedFileIfAbsent(string seedFile, string targetFile)
    {
        if (File.Exists(targetFile)) return false;
        if (!File.Exists(seedFile)) return false;

        Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
        // overwrite: false loses the race rather than winning it, matching SeedIfAbsent — whoever wrote
        // first has the file the server will read.
        try { File.Copy(seedFile, targetFile, overwrite: false); }
        catch (IOException) { return false; }
        return true;
    }

    /// <summary>
    /// Copies <paramref name="seedDir"/> into <paramref name="targetDir"/>, and only when the target
    /// directory does NOT EXIST.
    ///
    /// <para>An existing directory is left alone <b>even when it is empty</b>. An empty world dir is a
    /// deliberate state — a blank world someone means to author into, or one they cleared on purpose — and a
    /// rule that read emptiness as "fresh install" would refill it on the very next launch, which is the one
    /// thing seeding must never do. Presence of the folder is the whole test.</para>
    ///
    /// <para>Staged through a temporary folder and moved into place, so a copy that fails part-way leaves NO
    /// world dir behind. Without that, a half-written world would look "already seeded" forever after.</para>
    ///
    /// <para>Returns how many files were laid down; 0 means there was nothing to do.</para>
    /// </summary>
    public static int SeedIfAbsent(string seedDir, string targetDir)
    {
        if (Directory.Exists(targetDir)) return 0;
        if (!Directory.Exists(seedDir)) return 0;

        string staging = targetDir + ".seeding";
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);

        int copied = 0;
        try
        {
            foreach (string src in Directory.EnumerateFiles(seedDir, "*", SearchOption.AllDirectories))
            {
                string dest = Path.Combine(staging, Path.GetRelativePath(seedDir, src));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(src, dest);
                copied++;
            }

            // Last possible moment to lose a race with another instance starting at the same time: if one
            // won, its world is the one that stands and this copy is discarded.
            if (Directory.Exists(targetDir))
            {
                Directory.Delete(staging, recursive: true);
                return 0;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetDir)!);
            Directory.Move(staging, targetDir);
            return copied;
        }
        catch
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); } catch { }
            throw;
        }
    }
}

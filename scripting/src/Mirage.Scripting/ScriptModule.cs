namespace Mirage.Scripting;

/// <summary>One named piece of Compass source, and nothing about where it came from.</summary>
/// <param name="Name">What to call it in a problem — a file name, usually.</param>
/// <param name="Text">The source itself.</param>
public readonly record struct ScriptSource(string Name, string Text);

/// <summary>
/// What a game module is made of.
///
/// <para>🔴 <b>Deciding which files make up a program is this engine's business, not the compiler's.</b>
/// Compass is handed a set of sources and has no opinion about where they came from — its own
/// command-line tool keeps that decision in its driver for exactly this reason. So the rule lives here,
/// where it can be the rule a GAME wants rather than the one a build tool wants.</para>
///
/// <para><b>The rule: a module is a folder, and every <c>.cm</c> under it belongs to it.</b> Recursively,
/// and with no manifest. A build tool is right to make an author list their sources — what a release
/// contains should be readable off one file. A game module is the opposite case: it is authored by
/// somebody arranging their own folder, it ships as that folder, and asking them to maintain a second
/// list of files they can already see is the kind of bookkeeping this engine exists to remove. Records
/// work the same way — drop one in the world folder and it is there.</para>
///
/// <para><b>Reading is a delegate, and that is the load-bearing part.</b> Scripts live in the world
/// folder, and a world folder is the thing somebody zips up and hands to another machine. Wiring this to
/// <c>File.ReadAllText</c> directly would tie a game's rules to loose files on a disk forever; behind
/// <see cref="ScriptReader"/>, the same module loads out of an archive, a database, or an editor holding
/// something not yet saved.</para>
/// </summary>
public static class ScriptModule
{
    /// <summary>Where the text of a source comes from.</summary>
    /// <param name="path">A path this reader understands, as listed by the matching lister.</param>
    public delegate string ScriptReader(string path);

    /// <summary>Which sources a module holds.</summary>
    /// <param name="root">The module, named the way its reader understands.</param>
    public delegate IEnumerable<string> ScriptLister(string root);

    /// <summary>The extension a Compass source file carries.</summary>
    public const string Extension = ".cm";

    /// <summary>Every <c>.cm</c> under a folder on disk, in a stable order.</summary>
    public static IEnumerable<string> OnDisk(string root) =>
        Directory.EnumerateFiles(root, "*" + Extension, SearchOption.AllDirectories)
                 // Ordered, because the order files are handed over is the order problems are reported
                 // in, and a listing that changed between two machines would make one log unreadable
                 // against the other.
                 .OrderBy(p => p, StringComparer.Ordinal);

    /// <summary>
    /// Reads a module's sources, named relative to the module so a problem says <c>rules/combat.cm</c>
    /// rather than half a machine's directory tree.
    /// </summary>
    public static IReadOnlyList<ScriptSource> Read(
        string root, ScriptLister? list = null, ScriptReader? read = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        list ??= OnDisk;
        read ??= File.ReadAllText;

        var sources = new List<ScriptSource>();
        foreach (string path in list(root))
        {
            sources.Add(new ScriptSource(Relative(root, path), read(path)));
        }
        return sources;
    }

    private static string Relative(string root, string path)
    {
        try { return Path.GetRelativePath(root, path).Replace('\\', '/'); }
        catch (ArgumentException) { return path; }
    }
}

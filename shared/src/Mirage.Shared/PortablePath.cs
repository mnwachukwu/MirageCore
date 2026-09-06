namespace Mirage.Shared;

/// <summary>
/// Splitting a path that TRAVELS, where the machine reading it is no guide to the machine that wrote it.
///
/// <para>🔴 <c>Path.DirectorySeparatorChar</c> and <c>Path.AltDirectorySeparatorChar</c> are BOTH <c>/</c> on
/// Linux and macOS, so every API keyed on them is blind to a backslash there: a path written on Windows has
/// no separator those can see, and the whole string reads as one segment. Windows hides this completely,
/// because there <c>/</c> is the alt separator and both shapes already work.</para>
///
/// <para>Stored settings are where this bites — the recent-worlds list is written by whichever machine last
/// opened a world and read by whichever opens the editor next. So both separators are recognized on every
/// platform, always.</para>
///
/// <para>This is for READING a path as text. To build one, or to work on a path that never leaves the
/// machine that made it, <c>Path.Combine</c> and the rest of <c>System.IO.Path</c> are correct.</para>
/// </summary>
public static class PortablePath
{
    /// <summary>Both separators, whatever this platform calls its own.</summary>
    public static readonly char[] Separators = ['\\', '/'];

    /// <summary>The last segment — the folder a path ends in, which is what tells two unnamed worlds apart.
    /// A trailing separator is not a segment. A string with no separator at all is its own leaf.</summary>
    public static string Leaf(string path)
    {
        string trimmed = path.TrimEnd(Separators);
        int cut = trimmed.LastIndexOfAny(Separators);
        return cut < 0 ? trimmed : trimmed[(cut + 1)..];
    }
}

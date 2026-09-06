namespace Mirage.Shared;

/// <summary>
/// Whether two paths name the same place — a question only the platform can answer.
///
/// <para>🔴 Windows and macOS match filenames case-INSENSITIVELY; Linux matches them case-SENSITIVELY. So
/// <c>/srv/World</c> and <c>/srv/world</c> are one folder on two of the three platforms the game ships to
/// and two folders on the third, and a fixed choice is wrong somewhere: comparing case-insensitively on
/// Linux merges worlds that are genuinely different, and comparing case-sensitively on Windows lists one
/// world twice and fails to forget it when it goes missing.</para>
///
/// <para>This reads the DEFAULT behaviour of each platform's usual filesystem. A case-sensitive volume on
/// macOS, or a case-insensitive mount on Linux, is not detected — that needs probing the volume itself, a
/// filesystem round trip per comparison, which buys nothing for the recent-worlds list this serves.</para>
/// </summary>
public static class PathComparison
{
    /// <summary>The rule matching this platform's usual filesystem.</summary>
    public static StringComparison Rule =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>The same rule as a comparer, for sets and dictionaries keyed by path.</summary>
    public static StringComparer Comparer =>
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// True when both name the same place.
    ///
    /// <para>A trailing separator carries no meaning, so a path ending in one matches the same path without:
    /// folder pickers and stored settings disagree about it routinely. Textual otherwise — neither side is
    /// resolved against the filesystem, so <c>.</c> and <c>..</c> segments and symlinks stand as written, and
    /// two different spellings of one folder still read as two.</para>
    /// </summary>
    public static bool SameLocation(string? a, string? b)
    {
        if (a is null || b is null) return a is null && b is null;
        return string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), Rule);
    }
}

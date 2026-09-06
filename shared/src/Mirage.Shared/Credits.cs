namespace Mirage.Shared;

/// <summary>
/// Who made this, in one place. The client's credits screen, the editor's About dialog, the server
/// shell's About dialog and the server console's <c>/credits</c> all read from here, so there is one
/// spelling of the name and one URL rather than four that drift.
///
/// <para>The names and the URL live here rather than in each app's string files: they are the same in
/// every language, and a translator has no business retyping a person's name or a domain.</para>
/// </summary>
public static class Credits
{
    /// <summary>Creator and developer.</summary>
    public const string Author = "Matt Nwachukwu";

    /// <summary>In-game handles, kept because the original credits screen carried them.</summary>
    public const string AuthorHandles = "(Silver / Vandestelka)";

    public const string Studio = "Pluperfect Development";
    public const string SiteUrl = "https://pluperfect.dev";

    /// <summary>The year the copyright runs from.</summary>
    public const int CopyrightFrom = 2026;

    /// <summary>"2026" in the first year, "2026-2031" after that. Takes the year rather than reading the
    /// clock so it can be tested, and so a caller that already knows "now" does not disagree with it.</summary>
    public static string CopyrightYears(int currentYear) =>
        currentYear > CopyrightFrom ? $"{CopyrightFrom}-{currentYear}" : $"{CopyrightFrom}";

    /// <summary>"Copyright (c) 2026-2031 Pluperfect Development".
    /// <para>Spelled "(c)" rather than the © sign: this string reaches the game client, whose SpriteFonts
    /// are the one renderer here that cannot be given a new glyph at runtime.</para></summary>
    public static string CopyrightLine(int currentYear) =>
        $"Copyright (c) {CopyrightYears(currentYear)} {Studio}";
}

namespace Mirage.Shared.Extensibility;

/// <summary>
/// The glyphs a game may name, and the one place that says which those are.
///
/// <para><b>A name crosses the wire; the geometry never does.</b> A game cannot ship code to a client
/// or to the editor, and vector markup out of a world folder is arbitrary input to a path parser. So a
/// game picks a name from this list and each surface draws its own shape for it — the same bargain
/// <see cref="GameKey"/> makes, for the same reason.</para>
///
/// <para><b>Two halves, and a name with no shape behind it draws nothing.</b> The editor's rail and the
/// game client each hold a table keyed by these names. A test holds all three sets equal, because a
/// glyph added here and drawn nowhere is an icon that silently disappears.</para>
///
/// <para>⚠ <b>Refused when declared, tolerated when drawn.</b> A game naming a glyph that does not
/// exist is told so at load, with the list — a typo is otherwise a family that looks like every other
/// family. But a renderer meeting an unknown name falls back to <see cref="Default"/> rather than
/// failing, because a client from before the glyph existed should still draw a usable rail.</para>
/// </summary>
public static class GameIcon
{
    /// <summary>What a game gets when it names nothing, and what a renderer falls back to.</summary>
    public const string Default = "quads";

    /// <summary>Every glyph a game may name, grouped as a reader would scan them.
    ///
    /// <para>Chosen so that two families in one game look different and a game of any genre finds
    /// something close enough. They are deliberately generic: <c>leaf</c> is plants, farming, nature
    /// and ecology, not one of those.</para></summary>
    public static readonly IReadOnlyList<string> Offered =
    [
        // Places and collections
        "grid", "quads", "pin", "flag",

        // Things somebody carries, makes, or spends
        "bag", "gem", "coin", "sword", "flask", "cog",

        // Living things
        "person", "paw", "leaf", "heart",

        // What is written down
        "book", "scroll", "list", "bubble",

        // Everything else a game counts
        "key", "shield", "star", "spark", "flame", "clock", "note", "dice", "shop",
    ];

    /// <summary>Whether a game may name this glyph. Blank is true: most of what a game declares
    /// wants no icon at all, so it cannot be the answer that fails.</summary>
    public static bool IsOffered(string? icon) =>
        string.IsNullOrEmpty(icon) || Offered.Contains(icon, StringComparer.Ordinal);

    /// <summary>The list as a sentence, for the refusal that names it. A message saying only "that
    /// glyph is not offered" leaves the author guessing at a set they cannot see.</summary>
    public static string Listed => string.Join(", ", Offered);

    /// <summary>This glyph, or <see cref="Default"/> for a blank or unknown one. What a renderer calls
    /// before it looks a name up in its own table.</summary>
    public static string Or(string? icon) => IsOffered(icon) && !string.IsNullOrEmpty(icon)
        ? icon
        : Default;
}

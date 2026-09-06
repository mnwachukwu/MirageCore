namespace Mirage.Shared;

/// <summary>
/// Character and string validation for user-supplied text.
/// The valid set matches the character regions compiled into the game's SpriteFont atlases
/// (Content/fonts/*.spritefont) — keep the two in lockstep, or text passes validation and then
/// renders as a missing glyph. Covers every letter and mark French, Spanish, and Portuguese need:
/// ASCII printable (U+0020–U+007E), Latin-1 Supplement from the inverted marks up
/// (U+00A1–U+00FF: the accents plus ¡ ¿ « » ª º), and the French Œ/œ ligature (U+0152–U+0153),
/// which sits in Latin Extended-A outside Latin-1.
/// </summary>
public static class TextValidation
{
    /// <summary>True if <paramref name="c"/> falls within the supported character range.</summary>
    public static bool IsValidChar(char c) =>
        (c >= ' ' && c <= '~') || (c >= '¡' && c <= 'ÿ') || c is 'Œ' or 'œ';

    /// <summary>True when every character in <paramref name="text"/> passes <see cref="IsValidChar"/>.</summary>
    public static bool IsValidText(string text) => text.All(IsValidChar);

    /// <summary>
    /// Returns <paramref name="s"/> with any out-of-range character replaced by
    /// <paramref name="replacement"/>. Returns <paramref name="s"/> unchanged (no allocation)
    /// when all characters are already valid.
    /// </summary>
    public static string Filter(string s, char replacement = '?')
    {
        if (s.All(IsValidChar)) return s;
        var buf = new char[s.Length];
        for (int i = 0; i < s.Length; i++)
            buf[i] = IsValidChar(s[i]) ? s[i] : replacement;
        return new string(buf);
    }
}

namespace Mirage.Shared;

/// <summary>
/// What a graphics sheet's filename means: which files count as sheets, and how a leading number names
/// the sheet's index.
///
/// <para>A sheet is named <c>N_something.bmp</c>, and only the <c>N</c> is data. Every painted tile stores
/// that number and nothing else — no name, no path, no mapping table — so the text after it is a label for
/// people and may be changed freely, while the number may not.</para>
///
/// <para>This lives here because the editor and the game both answer the same question about the same
/// folder, and answered separately they drift. They already did: the rule was written twice, once in the
/// editor's bitmap cache and once in the client's content loader, as two identical private copies.</para>
/// </summary>
public static class SheetFile
{
    /// <summary>File types a sheet may be authored as.</summary>
    public static readonly string[] Extensions = [".bmp", ".png"];

    /// <summary>Whether a path names a file the sheet loaders will read. Extension only — nothing here
    /// opens the file.</summary>
    public static bool IsSupported(string path)
    {
        string ext = Path.GetExtension(path);
        foreach (string allowed in Extensions)
            if (ext.Equals(allowed, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>
    /// Whether this file's transparency comes from a color key rather than from an alpha channel.
    /// </summary>
    /// <remarks>
    /// The extension decides, and it decides the same way in the editor and in the game: a BMP names its
    /// transparent color with its top-left pixel, and a PNG carries its own alpha. Stated by the extension
    /// rather than sniffed from the pixels so that an author can hold the rule in their head, and so that
    /// an edit which happens to remove the last transparent pixel cannot silently change how a sheet loads.
    /// </remarks>
    public static bool UsesColorKey(string path) =>
        Path.GetExtension(path).Equals(".bmp", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The sheet index a filename claims, or -1 when it claims none.
    /// </summary>
    /// <param name="fileNameNoExt">A filename with its extension already removed.</param>
    /// <remarks>
    /// The leading run of digits, so <c>3_forest</c> is 3 and <c>12dungeon</c> is also 12 — the separator
    /// is a convention for readability, not part of the rule. A name starting with anything else has no
    /// index and is not a sheet. Leading zeros parse, so <c>07_x</c> and <c>7_x</c> both claim 7 and
    /// collide.
    /// </remarks>
    public static int ParseIndex(string fileNameNoExt)
    {
        int i = 0;
        while (i < fileNameNoExt.Length && char.IsAsciiDigit(fileNameNoExt[i])) i++;
        return i > 0 && int.TryParse(fileNameNoExt[..i], out int n) ? n : -1;
    }

    /// <summary>
    /// The part of a filename meant for a person: <c>0_Tiles</c> reads as <c>Tiles</c>.
    /// </summary>
    /// <param name="fileNameNoExt">A filename with its extension already removed.</param>
    /// <remarks>Strips the leading digits and one following separator. A name that is nothing but digits
    /// keeps them, because a label of "" would leave the sheet with no name at all.</remarks>
    public static string DisplayName(string fileNameNoExt)
    {
        int i = 0;
        while (i < fileNameNoExt.Length && char.IsAsciiDigit(fileNameNoExt[i])) i++;
        if (i > 0 && i < fileNameNoExt.Length && fileNameNoExt[i] is '_' or '-' or ' ') i++;
        string name = fileNameNoExt[i..];
        return name.Length > 0 ? name : fileNameNoExt;
    }

    /// <summary>Builds the filename a sheet at <paramref name="index"/> with this label should have.</summary>
    public static string FileName(int index, string displayName, string extension) =>
        $"{index}_{displayName}{extension}";
}

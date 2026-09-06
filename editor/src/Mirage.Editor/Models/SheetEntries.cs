using Mirage.Editor.Localization;

namespace Mirage.Editor.Models;

/// <summary>
/// Typeahead entries for an art-sheet picker: Id is the sheet number, Name is the sheet's display
/// name (its filename minus the numeric prefix).
///
/// <para>A gap in the numbering still gets an entry. The number is what tiles, NPCs, classes and items
/// store, so closing a gap here would silently repoint every record past it.</para>
/// </summary>
public static class SheetEntries
{
    public static NamedEntry[] Build(int count, IReadOnlyList<string> names)
    {
        var arr = new NamedEntry[Math.Max(0, count)];
        for (int i = 0; i < arr.Length; i++) arr[i] = new NamedEntry(i, DisplayName(names, i));
        return arr;
    }

    /// <summary>A sheet's name, falling back to the shared "(unnamed)" label for a gap or a file whose
    /// name is nothing but its index.</summary>
    public static string DisplayName(IReadOnlyList<string> names, int index) =>
        index >= 0 && index < names.Count && !string.IsNullOrWhiteSpace(names[index])
            ? names[index]
            : EditorStrings.Get(EditorStrings.Editor_SheetUnnamed);
}

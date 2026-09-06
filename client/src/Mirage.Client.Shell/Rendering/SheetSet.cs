using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Shared.Records;

namespace Mirage.Client.Shell.Rendering;

/// <summary>
/// Reading one sheet out of a numbered set.
///
/// <para>Tiles, sprites and items are all indexed collections whose gaps are null, and every record that
/// names art names a sheet number that may not be there — a world can reference a sheet the player's
/// install has not got. Every read goes through here so that answer is the same everywhere: nothing.</para>
/// </summary>
public static class SheetSet
{
    /// <summary>The sheet at <paramref name="index"/>, or null when the set has no such sheet.</summary>
    public static Texture2D? Sheet(this IReadOnlyList<Texture2D?> sheets, int index) =>
        (uint)index < (uint)sheets.Count ? sheets[index] : null;

    /// <summary>Draws one item's icon from the sheet that item names.
    ///
    /// <para>Every icon in the interface goes through here so the sheet is read off the item rather than
    /// assumed: an item's picture number is a row, and which book that row is in is the item's to say.
    /// An item with no picture, or one naming a sheet this install has not got, draws nothing.</para></summary>
    public static void DrawItemIcon(this SpriteBatch sb, IReadOnlyList<Texture2D?> sheets,
        ItemRecord? item, Rectangle dest, Color tint)
    {
        if (item is not null) sb.DrawItemIcon(sheets, item.Pic, item.ItemSheet, dest, tint);
    }

    /// <inheritdoc cref="DrawItemIcon(SpriteBatch, IReadOnlyList{Texture2D}, ItemRecord, Rectangle, Color)"/>
    public static void DrawItemIcon(this SpriteBatch sb, IReadOnlyList<Texture2D?> sheets,
        int pic, int sheet, Rectangle dest, Color tint)
    {
        if (pic < 0) return;
        var tex = sheets.Sheet(sheet);
        if (tex is null) return;
        var src = ItemAtlas.GetSourceRect((short)pic);
        if (src == Rectangle.Empty) return;
        sb.Draw(tex, dest, src, tint);
    }
}

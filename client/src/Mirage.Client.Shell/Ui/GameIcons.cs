using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;

namespace Mirage.Client.Shell.Ui;

/// <summary>
/// Draws the glyph a game named, on a client that was never compiled for that game.
///
/// <para>A game sends a NAME and nothing else — it cannot ship geometry to a client it does not
/// control. <see cref="GameIconArt"/> holds what this build draws for each name, rasterized from the
/// editor's own paths so one name is not two pictures.</para>
///
/// <para><b>One texture per glyph, built the first time it is asked for and kept.</b> A glyph is a
/// 32x32 alpha mask; turning that into a texture costs one upload, and drawing it into a smaller
/// rectangle lets the sampler do the smoothing. Drawn a pixel at a time instead, a glyph scaled to
/// fit beside a caption would alias into a smear.</para>
/// </summary>
public static class GameIcons
{
    private static readonly Dictionary<string, Texture2D> Made = new(System.StringComparer.Ordinal);

    /// <summary>Draws a glyph inside <paramref name="bounds"/>, or nothing for a blank name.
    ///
    /// <para>Blank draws nothing rather than the default: a menu of verbs all wearing the same
    /// placeholder is worse than a menu of captions.</para></summary>
    public static void Draw(SpriteBatch sb, string? icon, Rectangle bounds, Color color)
    {
        if (string.IsNullOrEmpty(icon)) return;

        sb.Draw(TextureFor(sb.GraphicsDevice, icon), bounds, color);
    }

    private static Texture2D TextureFor(GraphicsDevice device, string icon)
    {
        if (Made.TryGetValue(icon, out Texture2D? made) && !made.IsDisposed) return made;

        uint[] rows = GameIconArt.For(icon);
        const int Size = GameIconArt.Size;
        var pixels = new Color[Size * Size];

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                // White where the mask is set, so the draw call's color tints it.
                bool on = (rows[y] & (1u << (Size - 1 - x))) != 0;
                pixels[y * Size + x] = on ? Color.White : Color.Transparent;
            }
        }

        var texture = new Texture2D(device, Size, Size);
        texture.SetData(pixels);

        Made[icon] = texture;
        return texture;
    }

    /// <summary>Throws away every glyph made so far. For a device reset, which invalidates them
    /// all.</summary>
    public static void Forget()
    {
        foreach (Texture2D texture in Made.Values) texture.Dispose();
        Made.Clear();
    }
}

using Avalonia.Media;

namespace Mirage.Editor.Models;

/// <summary>Shared hex string ↔ Avalonia <see cref="Color"/> conversions for the light color inputs
/// (map-editor light dialog + NPC light block), keeping the color picker and hex box in agreement.</summary>
internal static class ColorHex
{
    public static Color ToColor(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    public static uint ToRgb(Color c) => (uint)((c.R << 16) | (c.G << 8) | c.B);

    public static bool TryParse(string s, out Color color)
    {
        s = s.Trim().TrimStart('#');
        if (s.Length == 6 && uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out uint rgb))
        {
            color = ToColor(rgb);
            return true;
        }
        color = Colors.Black;
        return false;
    }
}

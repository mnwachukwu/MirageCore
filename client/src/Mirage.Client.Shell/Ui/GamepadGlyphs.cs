using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Mirage.Client.Shell.Ui;

/// <summary>
/// PlayStation face-button glyphs, and the decision of whether to use them.
///
/// <para><b>Why these are drawn rather than typed.</b> The Xbox faces are letters, so they cost nothing —
/// the spritefont already has A, B, X and Y. Sony's are shapes, and □ △ ○ ✕ are not in the font: the
/// localization guard test rejects any string carrying a codepoint the font can't render (it caught an em
/// dash in this very feature). Adding four codepoints to the font is a content change; four shapes drawn
/// from lines and a rect is a dozen lines of code that can never fall back to a blank box.</para>
///
/// <para>Detection is best-effort and deliberately conservative: MonoGame surfaces only a driver-supplied
/// display name, so this looks for Sony's well-known product strings and otherwise assumes Xbox, which is
/// also what an unrecognized third-party pad reports itself as. Getting it wrong shows the right button in
/// the wrong alphabet, never the wrong button.</para>
/// </summary>
public static class GamepadGlyphs
{
    public enum PsFace { Cross, Circle, Square, Triangle }

    // Sony's own colors, muted enough to read on a dark plate rather than glow off it.
    private static readonly Color CrossColor = new(150, 175, 235);
    private static readonly Color CircleColor = new(226, 116, 116);
    private static readonly Color SquareColor = new(214, 148, 200);
    private static readonly Color TriangleColor = new(122, 206, 178);

    // Re-probed on a cadence rather than every frame (GetCapabilities hits the driver) and rather than
    // once at startup (a pad plugged in mid-session should still be recognized).
    private const long ReprobeIntervalMs = 2000;
    private static long _lastProbeMs = long.MinValue;
    private static bool _preferPs;

    /// <summary>Whether the connected pad looks like a PlayStation controller.</summary>
    public static bool PreferPlayStation
    {
        get
        {
            long now = Environment.TickCount64;
            if (now - _lastProbeMs >= ReprobeIntervalMs)
            {
                _lastProbeMs = now;
                _preferPs = Probe();
            }
            return _preferPs;
        }
    }

    /// <summary>Force the style, for tests and for a future Options setting — an explicit choice should
    /// always beat a guess at a driver string.</summary>
    public static void Override(bool? playStation)
    {
        if (playStation is { } v)
        {
            _preferPs = v;
            _lastProbeMs = long.MaxValue;   // stop re-probing over the top of a deliberate choice
        }
        else
        {
            _lastProbeMs = long.MinValue;   // back to auto-detect on the next read
        }
    }

    private static bool Probe()
    {
        try
        {
            var caps = GamePad.GetCapabilities(PlayerIndex.One);
            if (!caps.IsConnected) return false;
            return LooksLikeSony(caps.DisplayName) || LooksLikeSony(caps.Identifier);
        }
        catch
        {
            // No pad, no driver, or a platform whose backend does not implement capabilities — all of which
            // just mean "assume Xbox", never a crash on the draw path.
            return false;
        }
    }

    internal static bool LooksLikeSony(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        // "Wireless Controller" is what a DualShock 4 reports through several drivers, and nothing Xbox
        // reports itself that way — it is a weak signal but a one-sided one.
        foreach (string marker in new[] { "sony", "playstation", "dualshock", "dualsense", "ps3", "ps4", "ps5", "wireless controller" })
            if (name.Contains(marker, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>Draw one face glyph centered in <paramref name="plate"/>. Sized off the plate so the badge
    /// can be resized without the shapes drifting out of it.</summary>
    public static void DrawPlayStationFace(SpriteBatch sb, Rectangle plate, PsFace face)
    {
        var c = new Vector2(plate.Center.X, plate.Center.Y);
        // Inset so a stroke never touches the plate edge, which would read as a smudge at this size.
        float r = Math.Min(plate.Width, plate.Height) / 2f - 2f;
        if (r <= 0f) return;

        switch (face)
        {
            case PsFace.Cross:
            {
                float d = r * 0.85f;
                UiHelper.DrawLine(sb, c + new Vector2(-d, -d), c + new Vector2(d, d), CrossColor, 1.6f);
                UiHelper.DrawLine(sb, c + new Vector2(-d, d), c + new Vector2(d, -d), CrossColor, 1.6f);
                break;
            }
            case PsFace.Circle:
                UiHelper.DrawCircleOutline(sb, c, r * 0.9f, CircleColor, 1.6f, segments: 16);
                break;

            case PsFace.Square:
            {
                int s = (int)MathF.Round(r * 1.6f);
                var box = new Rectangle((int)(c.X - s / 2f), (int)(c.Y - s / 2f), s, s);
                UiHelper.DrawBorder(sb, box, SquareColor);
                break;
            }
            case PsFace.Triangle:
            {
                // Point-up, centered on the plate rather than on the triangle's bounding box — a geometric
                // centroid sits low and reads as sunken at 11px.
                float h = r * 1.7f, w = r * 1.8f;
                var top = c + new Vector2(0f, -h / 2f);
                var left = c + new Vector2(-w / 2f, h / 2f);
                var right = c + new Vector2(w / 2f, h / 2f);
                UiHelper.DrawLine(sb, top, left, TriangleColor, 1.6f);
                UiHelper.DrawLine(sb, top, right, TriangleColor, 1.6f);
                UiHelper.DrawLine(sb, left, right, TriangleColor, 1.6f);
                break;
            }
        }
    }
}

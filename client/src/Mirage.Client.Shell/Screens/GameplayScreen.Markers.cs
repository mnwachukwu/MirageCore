using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Core.Logic;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;

namespace Mirage.Client.Shell.Screens;

/// <summary>
/// Drawing what a game has marked on the ground.
///
/// <para>Four parts, each drawn only when the mark carries it: the ring around the marked ground, a
/// pennant on the tile itself, a label over the pennant, and a meter under the label. A mark with only a
/// tile and a color is a bare pin, and that is a supported mark.</para>
/// </summary>
public sealed partial class GameplayScreen
{
    private const float MarkerFlagMargin = 4f;
    private const float MarkerFlagPoleInset = 6f;
    private const float MarkerPennantW = 12f;
    private const float MarkerPennantApexY = 5f;
    private const float MarkerPennantBotY = 10f;
    private const float MarkerLabelGap = 4f;
    private const float MarkerRingAlpha = 0.85f;
    private const float MarkerRingThickness = 2f;
    private const float MarkerMeterW = 30f;
    private const float MarkerMeterH = 4f;
    private const float MarkerMeterGap = 2f;

    private void DrawMarkers(SpriteBatch sb, WorldLayer group, SpriteFont nameFont,
                             float nameCellW, float nameLineH)
    {
        foreach (var m in _renderFrame.Markers)
        {
            if (m.Layer != group) continue;

            var color = new Color(GameColor.RedOf(m.Rgb), GameColor.GreenOf(m.Rgb), GameColor.BlueOf(m.Rgb));

            if (m.Radius > 0) DrawMarkerRing(sb, m, color);

            float poleX = m.ScreenX + Constants.PicX / 2f - MarkerFlagPoleInset;
            float poleTop = m.ScreenY + MarkerFlagMargin;
            float poleBottom = m.ScreenY + Constants.PicY - MarkerFlagMargin;

            UiHelper.DrawLine(sb, new Vector2(poleX, poleTop), new Vector2(poleX, poleBottom), Color.Black, 2f);

            var apex = new Vector2(poleX, poleTop);
            var point = new Vector2(poleX + MarkerPennantW, poleTop + MarkerPennantApexY);
            var foot = new Vector2(poleX, poleTop + MarkerPennantBotY);

            UiHelper.FillTriangle(sb, apex, point, foot, color);
            UiHelper.DrawLine(sb, apex, point, Color.Black, 1f);
            UiHelper.DrawLine(sb, point, foot, Color.Black, 1f);

            float labelY = poleTop - MarkerLabelGap;

            if (m.Ceiling > 0)
            {
                labelY -= MarkerMeterH + MarkerMeterGap;
                DrawMarkerMeter(sb, m, color, m.ScreenX + Constants.PicX / 2f, labelY + MarkerMeterGap);
            }

            if (m.Label.Length > 0)
            {
                var text = new TextDrawCmd(m.ScreenX + Constants.PicX / 2f, labelY, m.Label, 0,
                                           AlignBottom: true, RgbOverride: m.Rgb);
                DrawWorldName(sb, nameFont, text, nameCellW, nameLineH);
            }
        }
    }

    /// <summary>The boundary between the tiles a mark covers and the tiles it does not.
    ///
    /// <para>🔴 <b>A STAIRCASE, never a circle.</b> The engine answers "is this square inside" a whole
    /// tile at a time, so the edge of the covered set runs along tile sides. A smooth curve puts the line
    /// through the middle of tiles, where it disagrees with the rule in both directions at once — a tile
    /// covered with half of it outside the line, and a tile the line crosses covered by nothing. Drawn as
    /// the staircase, "inside the line" and "this tile is inside" are one statement.</para>
    ///
    /// <para>Only the edge is drawn. The interior needs no marking: the pennant and the label name the
    /// place, and the only question that is ever close is which side of the line a particular tile is
    /// on.</para></summary>
    private static void DrawMarkerRing(SpriteBatch sb, MarkerDrawCmd m, Color color)
    {
        var origin = new Vector2(m.ScreenX, m.ScreenY);

        foreach (var (a, b) in MarkerRingEdges(m.Radius))
            UiHelper.DrawLine(sb, origin + a, origin + b, color * MarkerRingAlpha, MarkerRingThickness);
    }

    /// <summary>The boundary segments, in pixels relative to the marked tile's own origin. Fixed for a
    /// radius, so it is built once rather than per mark per frame.</summary>
    private static List<(Vector2 A, Vector2 B)> MarkerRingEdges(int radius)
    {
        if (_markerRingRadius == radius) return _markerRingEdges;

        _markerRingEdges.Clear();

        // The same test the engine makes: center to center, inclusive.
        static bool Inside(int dx, int dy, int r) => dx * dx + dy * dy <= r * r;

        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (!Inside(dx, dy, radius)) continue;

                float x0 = dx * Constants.PicX, y0 = dy * Constants.PicY;
                float x1 = x0 + Constants.PicX, y1 = y0 + Constants.PicY;

                // A side is on the boundary when the tile across it is outside. Corners need no special
                // case: each is where two of these segments meet.
                if (!Inside(dx - 1, dy, radius)) _markerRingEdges.Add((new Vector2(x0, y0), new Vector2(x0, y1)));
                if (!Inside(dx + 1, dy, radius)) _markerRingEdges.Add((new Vector2(x1, y0), new Vector2(x1, y1)));
                if (!Inside(dx, dy - 1, radius)) _markerRingEdges.Add((new Vector2(x0, y0), new Vector2(x1, y0)));
                if (!Inside(dx, dy + 1, radius)) _markerRingEdges.Add((new Vector2(x0, y1), new Vector2(x1, y1)));
            }
        }

        _markerRingRadius = radius;
        return _markerRingEdges;
    }

    private static int _markerRingRadius = -1;
    private static readonly List<(Vector2 A, Vector2 B)> _markerRingEdges = [];

    /// <summary>How far along the mark is, as a filled bar over the pennant. Dark behind and outlined, so
    /// it reads over whatever ground the mark is standing on.</summary>
    private static void DrawMarkerMeter(SpriteBatch sb, MarkerDrawCmd m, Color color, float centerX, float top)
    {
        float left = centerX - MarkerMeterW / 2f;
        float share = m.Ceiling > 0 ? Math.Clamp((float)m.Value / m.Ceiling, 0f, 1f) : 0f;

        UiHelper.DrawFilledRect(sb, left, top, MarkerMeterW, MarkerMeterH, Color.Black * 0.7f);
        if (share > 0f) UiHelper.DrawFilledRect(sb, left, top, MarkerMeterW * share, MarkerMeterH, color);
        UiHelper.DrawBorder(sb, left, top, MarkerMeterW, MarkerMeterH, Color.Black, 1f);
    }
}

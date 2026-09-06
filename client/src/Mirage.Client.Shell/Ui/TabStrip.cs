using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Mirage.Client.Shell.Ui;

/// <summary>Shared visuals for the manually-drawn tab strips. Holds the common palette and the
/// "centered label, gold-active, hover-aware" per-tab draw used by ControlsPanel and SocialPanel (its main
/// strip + the guild sub-strip). ChatPanel's tabs share this palette but draw themselves (left-aligned labels
/// + a flash highlight + close/add buttons), so they don't use <see cref="DrawCenteredTab"/>. Each panel keeps
/// its own tab LAYOUT (rect computation) — only the palette + this one draw style are shared.</summary>
public static class TabStrip
{
    public static readonly Color ActiveBg = new(60, 60, 100);  // brighter than the strip, picks the eye
    public static readonly Color InactiveBg = new(20, 20, 40);   // recedes into the strip
    public static readonly Color HoverBg = new(40, 40, 70);   // mid-tone — clearly responding to the mouse

    /// <summary>Draws one tab: fill by state, gold (active) / dim (else) border, and a centered label colored
    /// gold (active) / white (hovered) / gray.</summary>
    public static void DrawCenteredTab(SpriteBatch sb, SpriteFont font, Rectangle r, string label, bool active, bool hovered)
    {
        UiHelper.DrawFilledRect(sb, r, active ? ActiveBg : (hovered ? HoverBg : InactiveBg));
        UiHelper.DrawBorder(sb, r, active ? Color.Gold : Color.DimGray);
        var size = font.MeasureString(label);
        float lx = r.X + r.Width / 2f - size.X / 2f;
        float ly = r.Y + (r.Height - font.LineSpacing) / 2f;
        sb.DrawString(font, label, new Vector2(lx, ly), active ? Color.Gold : (hovered ? Color.White : Color.Gray));
    }
}

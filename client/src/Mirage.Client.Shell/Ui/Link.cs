using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Shell.Input;

namespace Mirage.Client.Shell.Ui;

/// <summary>
/// Clickable inline text-link widget — the in-game cousin of <see cref="Button"/>. Renders a
/// label that brightens on hover, requests the OS hand cursor while hovered (via the shared
/// <see cref="UiHelper"/> cursor bus), and exposes <see cref="IsClicked"/> for callers to
/// dispatch. <see cref="WrapInBrackets"/> auto-decorates the label with square brackets so
/// callers don't have to bake them into every localized string. Callers size <see cref="Bounds"/>
/// to the rendered text (via <see cref="MeasureSize"/>) so the clickable area matches the glyphs
/// exactly — position that tight box wherever the link should sit.
/// </summary>
public sealed class Link
{
    public Rectangle Bounds { get; set; }
    public string Label { get; set; } = "";
    // Default true — every existing sidebar/help link renders as "[Label]". Set false for
    // bare-word links (e.g. inline links inside a paragraph) without modifying the strings.
    public bool WrapInBrackets { get; set; } = true;
    public Color IdleColor { get; set; } = Color.Gray;
    public Color HoverColor { get; set; } = Color.White;

    /// <summary>The fully decorated string actually rendered — <see cref="Label"/> wrapped in
    /// square brackets when <see cref="WrapInBrackets"/> is true.</summary>
    public string DisplayText => WrapInBrackets ? "[" + Label + "]" : Label;

    /// <summary>Static helper for layout code that needs the rendered text width without
    /// holding a Link instance (e.g. centering a paired link group inside a strip).</summary>
    public static Vector2 MeasureSize(SpriteFont font, string label, bool wrapInBrackets = true)
        => font.MeasureString(wrapInBrackets ? "[" + label + "]" : label);

    public bool IsHovered(InputState input)
        => input.IsHoverIn(Bounds);

    public bool IsClicked(InputState input)
        => input.IsClickIn(Bounds);

    public void Draw(SpriteBatch sb, SpriteFont font, InputState input)
    {
        bool hovered = IsHovered(input);
        if (hovered) UiHelper.RequestHandCursor();
        string text = DisplayText;
        var size = font.MeasureString(text);
        // Bounds are sized to the text, so the box IS the link — draw flush-left within it,
        // vertically centered against any extra strip height.
        float y = Bounds.Y + (Bounds.Height - size.Y) / 2f;
        sb.DrawString(font, text, new Vector2(Bounds.X, y), hovered ? HoverColor : IdleColor);
    }
}

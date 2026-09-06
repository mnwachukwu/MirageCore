using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Shell.Input;

namespace Mirage.Client.Shell.Ui;

/// <summary>Immediate-mode push button: it holds no click state of its own, so callers poll
/// <see cref="IsClicked"/> each frame and call <see cref="Draw"/> to paint it. A label too wide for
/// the bounds is truncated and shown in full as a hover tooltip.</summary>
public sealed class Button
{
    public Rectangle Bounds { get; set; }
    public string Label { get; set; } = "";
    public bool Enabled { get; set; } = true;
    private readonly string _tooltipScope = UiHelper.NextTooltipScope("btn");

    /// <summary>True on the frame the button is clicked. Always false while disabled.</summary>
    public bool IsClicked(InputState input)
        => Enabled && input.IsClickIn(Bounds);

    /// <summary>True while the pointer is over the button, disabled or not.</summary>
    public bool IsHovered(InputState input)
        => input.IsHoverIn(Bounds);

    /// <summary>Paint the button in its normal, hovered, or disabled colors. <paramref name="normalColor"/>
    /// and <paramref name="hoverColor"/> override the respective backgrounds; null uses the shared theme
    /// color.</summary>
    public void Draw(SpriteBatch sb, SpriteFont font, InputState input,
        Color? normalColor = null, Color? hoverColor = null)
    {
        Color bg = !Enabled ? UiHelper.ButtonDisabledBg :
                   IsHovered(input) ? (hoverColor ?? UiHelper.ButtonHoverBg) :
                                      (normalColor ?? UiHelper.ButtonNormalBg);
        UiHelper.DrawFilledRect(sb, Bounds, bg);
        UiHelper.DrawBorder(sb, Bounds, Color.Gray);

        string display = UiHelper.FitText(font, Label, Bounds.Width - 4);
        var pos = UiHelper.CenterText(font, display, Bounds);
        sb.DrawString(font, display, pos, Enabled ? Color.White : Color.DarkGray);
        UiHelper.LabelTooltip(font, Label, new Rectangle(Bounds.X + 2, Bounds.Y, Bounds.Width - 4, Bounds.Height),
            input.MousePosition, _tooltipScope, 0);
    }
}

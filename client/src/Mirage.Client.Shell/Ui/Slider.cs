using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Shell.Input;

namespace Mirage.Client.Shell.Ui;

/// <summary>Horizontal drag slider over the integer range [<see cref="Min"/>, <see cref="Max"/>].
/// <para>A drag captures the pointer for its duration, so moving outside the track keeps adjusting the
/// value and the release doesn't leak a click to whatever is underneath.</para></summary>
public sealed class Slider
{
    public Rectangle Bounds { get; set; }
    public string Label { get; set; } = "";
    public int Min { get; set; } = 0;
    public int Max { get; set; } = 100;
    public int Value { get; set; }
    private bool _dragging;

    private static readonly Color TrackBg = new(50, 50, 50);
    private static readonly Color TrackBorder = new(90, 90, 90);
    private static readonly Color TrackDisabledBorder = new(45, 45, 45);
    private static readonly Color ThumbDisabledBg = new(55, 55, 55);
    private static readonly Color ThumbDisabledFill = new(70, 70, 70);

    /// <summary>Begin, continue, or end a drag and map the pointer's X to a value. Returns true only
    /// on the frames <see cref="Value"/> actually changed, so callers can persist on real edits.</summary>
    public bool Update(InputState input)
    {
        bool mouseDown = input.IsMouseDown();

        if (!_dragging)
        {
            // Start only on a genuine press-edge inside the track, then capture the pointer so the drag
            // owns the mouse (no hover bleed, no stray release-click) until the button goes up.
            if (!input.IsPressIn(Bounds)) return false;
            _dragging = true;
            input.CaptureMouse(this);
        }
        else if (!mouseDown)
        {
            _dragging = false;   // capture auto-releases the frame after button-up
            return false;
        }

        int trackW = Math.Max(1, Bounds.Width);
        float t = Math.Clamp((input.MousePosition.X - Bounds.X) / (float)trackW, 0f, 1f);
        int newValue = Min + (int)MathF.Round(t * (Max - Min));
        if (newValue == Value) return false;
        Value = newValue;
        return true;
    }

    /// <summary>Paint the "Label: value" caption, the track with its filled portion, and the thumb.
    /// The thumb is clamped inside the track so it stays fully visible at either end.</summary>
    public void Draw(SpriteBatch sb, SpriteFont font, InputState input, bool disabled = false)
    {
        bool hovered = !disabled && input.IsHoverIn(Bounds);
        Color textColor = disabled ? UiHelper.DisabledColor : ((hovered || _dragging) ? Color.White : Color.Gray);
        string labelText = UiHelper.FitText(font, $"{Label}: {Value}", Bounds.Width);
        sb.DrawString(font, labelText, new Vector2(Bounds.X, Bounds.Y), textColor);

        const int TrackY = 15;
        const int TrackH = 6;
        var track = new Rectangle(Bounds.X, Bounds.Y + TrackY, Bounds.Width, TrackH);
        UiHelper.DrawFilledRect(sb, track, disabled ? UiHelper.ButtonDisabledBg : TrackBg);

        float fill = Max > Min ? (Value - Min) / (float)(Max - Min) : 0f;
        int fillW = (int)(track.Width * fill);
        if (fillW > 0)
        {
            UiHelper.DrawFilledRect(sb, new Rectangle(track.X, track.Y, fillW, track.Height),
                disabled ? ThumbDisabledBg : (hovered || _dragging ? Color.White : Color.Gray));
        }

        UiHelper.DrawBorder(sb, track, disabled ? TrackDisabledBorder : TrackBorder);

        int thumbCx = track.X + fillW;
        thumbCx = Math.Clamp(thumbCx, track.X + 3, track.Right - 3);
        UiHelper.DrawFilledRect(sb, new Rectangle(thumbCx - 3, track.Y - 1, 6, track.Height + 2),
            disabled ? ThumbDisabledFill : Color.White);
    }
}

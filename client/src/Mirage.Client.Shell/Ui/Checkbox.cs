using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Shell.Input;

namespace Mirage.Client.Shell.Ui;

/// <summary>Immediate-mode checkbox drawn as "[X] Label" / "[ ] Label". Unlike
/// <see cref="Button"/> it owns its <see cref="Checked"/> state and toggles it in
/// <see cref="Update"/>.</summary>
public sealed class Checkbox
{
    public Rectangle Bounds { get; set; }
    private string _label = "";
    private string _checkedStr = "[X] ";
    private string _uncheckedStr = "[ ] ";

    /// <summary>Caption text. Setting it re-caches both rendered forms, so the draw path never
    /// builds a string per frame.</summary>
    public string Label
    {
        get => _label;
        set
        {
            _label = value;
            _checkedStr = $"[X] {value}";
            _uncheckedStr = $"[ ] {value}";
        }
    }

    public bool Checked { get; set; }

    /// <summary>Toggle on a click inside the bounds, consuming the click so nothing behind the
    /// checkbox also reacts. Returns true only on the frame the value changed.</summary>
    public bool Update(InputState input)
    {
        if (!input.IsClickIn(Bounds)) return false;
        Checked = !Checked;
        input.ConsumeMouseClick();
        return true;
    }

    /// <summary>Paint the checkbox; <paramref name="disabled"/> grays it and suppresses hover.</summary>
    public void Draw(SpriteBatch sb, SpriteFont font, InputState input, bool disabled = false)
    {
        bool hovered = !disabled && input.IsHoverIn(Bounds);
        Color color = disabled ? UiHelper.DisabledColor : (hovered ? Color.White : Color.Gray);
        string text = UiHelper.FitText(font, Checked ? _checkedStr : _uncheckedStr, Bounds.Width);
        sb.DrawString(font, text, new Vector2(Bounds.X, Bounds.Y), color);
    }
}

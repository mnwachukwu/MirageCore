using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Shared;
using System;

namespace Mirage.Client.Shell.Ui;

/// <summary>Reusable HSV color-picker overlay: a saturation/value box, a hue slider, three R/G/B number
/// fields, and a live preview swatch. The visual gamut is the primary control; the number fields are the
/// "alternative entry", kept two-way in sync with the box. Modeled on <see cref="NumberPromptDialog"/> —
/// self-contained, modal while open, Confirm/Cancel with Esc-cancels.
///
/// It knows nothing about what a color is "for": an optional <c>validate</c> callback lets the host reject
/// a chosen color on Confirm (returning an error message keeps the dialog open) — the seam the guild color
/// uses to forbid reserved palette colors without this control depending on guild rules.
///
/// The gradient textures are built lazily from the SpriteBatch's GraphicsDevice on first Draw (and the SV
/// box is rebuilt when the hue changes), so the dialog needs no GraphicsDevice at construction.</summary>
public sealed class ColorPickerDialog
{
    private const int Pad = 8;
    private const int SvSide = 150;
    private const int HueW = 18;
    private const int RowH = 22;
    private const int PreviewH = 26;
    private const int SwatchGen = 128; // generated-texture resolution (stretched to the on-screen rects)

    public bool IsOpen { get; private set; }
    public bool IsCapturingInput => IsOpen; // modal while open — gates world hotkeys like the other prompts

    private float _hue, _sat, _val; // HSV is authoritative for the box/slider; RGB is derived
    private readonly TextInputField[] _rgb =
    {
        new() { MaxLength = 3 }, new() { MaxLength = 3 }, new() { MaxLength = 3 },
    };
    private int _focusField = -1;
    private enum Drag { None, Sv, Hue }
    private Drag _drag;

    private Texture2D? _hueTex;
    private Texture2D? _svTex;
    private float _svTexHue = float.NaN;

    private readonly Button _confirmBtn = new();
    private readonly Button _cancelBtn = new();
    private InputState _input = new();
    private int _labelsGeneration = -1;
    private string _title = "";
    private string _error = "";
    private Func<int, string?>? _validate;
    private Action<int>? _onConfirm;

    private Rectangle _svRect, _hueRect, _previewRect;
    private readonly Rectangle[] _fieldRects = new Rectangle[3];

    /// <summary>Open the picker seeded with <paramref name="initialRgb"/> (packed 0xRRGGBB).
    /// <paramref name="validate"/> (optional) returns an error string to reject a color on Confirm, or null
    /// to allow it. <paramref name="onConfirm"/> receives the accepted packed color.</summary>
    public void Open(string title, int initialRgb, Func<int, string?>? validate, Action<int> onConfirm)
    {
        _title = title;
        _validate = validate;
        _onConfirm = onConfirm;
        _error = "";
        _focusField = -1;
        _drag = Drag.None;
        SetFromRgb(GameColor.RedOf(initialRgb), GameColor.GreenOf(initialRgb), GameColor.BlueOf(initialRgb));
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
        _onConfirm = null;
        _validate = null;
    }

    private int CurrentRgb()
    {
        var (r, g, b) = ColorConversion.HsvToRgb(_hue, _sat, _val);
        return GameColor.Pack(r, g, b);
    }

    private void SetFromRgb(int r, int g, int b)
    {
        (_hue, _sat, _val) = ColorConversion.RgbToHsv(r, g, b);
        WriteFields(r, g, b);
    }

    private void WriteFields(int r, int g, int b)
    {
        _rgb[0].SetText(r.ToString());
        _rgb[1].SetText(g.ToString());
        _rgb[2].SetText(b.ToString());
    }

    public void Update(InputState input, Rectangle host, long nowMs)
    {
        if (!IsOpen) return;
        _input = input;
        Layout(host);

        bool dragged = UpdateDrag(input);
        if (dragged)
        {
            // Box/slider moved → RGB is derived; push it into the fields.
            var (r, g, b) = ColorConversion.HsvToRgb(_hue, _sat, _val);
            WriteFields(r, g, b);
            _focusField = -1; // dragging steals focus from any field
        }
        else
        {
            UpdateFields(input, nowMs);
        }

        if (_confirmBtn.IsClicked(input) || input.IsKeyPressed(Keys.Enter))
        {
            int rgb = CurrentRgb();
            string? err = _validate?.Invoke(rgb);
            if (err is not null)
            {
                _error = err;
                return;
            }  // rejected — keep the dialog open
            _onConfirm?.Invoke(rgb);
            Close();
            return;
        }
        if (_cancelBtn.IsClicked(input) || input.IsKeyPressed(Keys.Escape))
        {
            input.ConsumeKey(Keys.Escape);
            Close();
        }
    }

    // Drag the SV box or the hue slider. Returns true if either moved this frame.
    private bool UpdateDrag(InputState input)
    {
        if (_drag == Drag.None)
        {
            if (input.IsPressIn(_svRect))
            {
                _drag = Drag.Sv;
                input.CaptureMouse(this);
            }
            else if (input.IsPressIn(_hueRect))
            {
                _drag = Drag.Hue;
                input.CaptureMouse(this);
            }
            else
            {
                return false;
            }
        }

        if (!input.IsMouseDown())
        {
            _drag = Drag.None;
            return false;
        }

        var m = input.MousePosition;
        if (_drag == Drag.Sv)
        {
            _sat = Math.Clamp((m.X - _svRect.X) / (float)_svRect.Width, 0f, 1f);
            _val = Math.Clamp(1f - (m.Y - _svRect.Y) / (float)_svRect.Height, 0f, 1f);
        }
        else // Hue
        {
            _hue = Math.Clamp((m.Y - _hueRect.Y) / (float)_hueRect.Height, 0f, 1f) * 360f;
        }
        return true;
    }

    private void UpdateFields(InputState input, long nowMs)
    {
        bool shift = input.IsKeyDown(Keys.LeftShift) || input.IsKeyDown(Keys.RightShift);
        for (int i = 0; i < 3; i++)
        {
            if (input.IsClickIn(_fieldRects[i]))
            {
                _focusField = i;
                _rgb[i].HandleMouseClick(input.MousePosition.X, shift);
            }
        }

        if (_focusField >= 0) _rgb[_focusField].Feed(input, nowMs);

        // A manual edit that yields a different color re-derives HSV so the box/slider follow. Comparing to
        // the current derived RGB means the fields we wrote during a drag parse back identically → no loop.
        if (TryReadFields(out int r, out int g, out int b) && GameColor.Pack(r, g, b) != CurrentRgb())
            (_hue, _sat, _val) = ColorConversion.RgbToHsv(r, g, b);
    }

    private bool TryReadFields(out int r, out int g, out int b)
    {
        r = g = b = 0;
        if (!int.TryParse(_rgb[0].Text, out r) || !int.TryParse(_rgb[1].Text, out g) || !int.TryParse(_rgb[2].Text, out b))
            return false;
        if (r is < 0 or > 255 || g is < 0 or > 255 || b is < 0 or > 255) return false;
        return true;
    }

    public void Draw(SpriteBatch sb, SpriteFont font, Rectangle host, long nowMs)
    {
        if (!IsOpen) return;
        if (_labelsGeneration != ClientStrings.Generation)
        {
            _labelsGeneration = ClientStrings.Generation;
            _confirmBtn.Label = ClientStrings.Get(ClientStrings.Common_Confirm);
            _cancelBtn.Label = ClientStrings.Get(ClientStrings.Common_Cancel);
        }
        Layout(host);
        EnsureTextures(sb.GraphicsDevice);

        var bg = new Rectangle(host.X + 2, host.Y + 2, host.Width - 4, host.Height - 4);
        UiHelper.DrawFilledRect(sb, bg, UiHelper.ConfirmOverlayBg);
        UiHelper.DrawBorder(sb, bg, UiHelper.ConfirmOverlayBorder);
        UiHelper.DrawLabel(sb, font, _title, new Vector2(host.X + Pad, host.Y + 6), Color.Yellow, host.Width - Pad * 2);

        // SV box + its marker.
        if (_svTex is not null) sb.Draw(_svTex, _svRect, Color.White);
        UiHelper.DrawBorder(sb, _svRect, Color.Gray);
        int mx = _svRect.X + (int)(_sat * _svRect.Width);
        int my = _svRect.Y + (int)((1f - _val) * _svRect.Height);
        UiHelper.DrawBorder(sb, new Rectangle(mx - 4, my - 4, 8, 8), _val > 0.5f ? Color.Black : Color.White);

        // Hue slider + its marker.
        if (_hueTex is not null) sb.Draw(_hueTex, _hueRect, Color.White);
        UiHelper.DrawBorder(sb, _hueRect, Color.Gray);
        int hy = _hueRect.Y + (int)(_hue / 360f * _hueRect.Height);
        UiHelper.DrawFilledRect(sb, new Rectangle(_hueRect.X - 2, hy - 1, _hueRect.Width + 4, 2), Color.White);

        // Preview swatch.
        int rgb = CurrentRgb();
        UiHelper.DrawFilledRect(sb, _previewRect, new Color(GameColor.RedOf(rgb), GameColor.GreenOf(rgb), GameColor.BlueOf(rgb)));
        UiHelper.DrawBorder(sb, _previewRect, Color.Gray);

        // R/G/B fields.
        string[] letters = { "R", "G", "B" };
        for (int i = 0; i < 3; i++)
        {
            sb.DrawString(font, letters[i], new Vector2(_fieldRects[i].X - 12, _fieldRects[i].Y + 3), Color.LightGray);
            _rgb[i].Draw(sb, font, _fieldRects[i], focused: _focusField == i, nowMs);
        }

        if (_error.Length > 0)
        {
            UiHelper.DrawLabel(sb, font, _error, new Vector2(host.X + Pad, _confirmBtn.Bounds.Y - 18),
                UiHelper.DangerButtonHover, host.Width - Pad * 2);
        }

        _confirmBtn.Draw(sb, font, _input, normalColor: UiHelper.PrimaryButtonNormal, hoverColor: UiHelper.PrimaryButtonHover);
        _cancelBtn.Draw(sb, font, _input);
    }

    private void Layout(Rectangle host)
    {
        int side = Math.Min(SvSide, host.Height - PreviewH - RowH * 3 - 40);
        side = Math.Max(60, Math.Min(side, host.Width - HueW - 120));
        int top = host.Y + 26;
        _svRect = new Rectangle(host.X + Pad, top, side, side);
        _hueRect = new Rectangle(_svRect.Right + 8, top, HueW, side);
        int rightX = _hueRect.Right + 18; // room for the R/G/B letter
        int rightW = host.Right - Pad - rightX;
        _previewRect = new Rectangle(rightX, top, Math.Max(20, rightW), PreviewH);
        for (int i = 0; i < 3; i++)
            _fieldRects[i] = new Rectangle(rightX, _previewRect.Bottom + 8 + i * (RowH + 4), Math.Max(20, rightW), RowH);
        _confirmBtn.Bounds = UiHelper.PanelBottomButton(host, 0);
        _cancelBtn.Bounds = UiHelper.PanelBottomButton(host, 1);
    }

    private void EnsureTextures(GraphicsDevice gd)
    {
        _hueTex ??= BuildHueTexture(gd);
        if (_svTex is null || Math.Abs(_svTexHue - _hue) >= 1f)
        {
            _svTex?.Dispose();
            _svTex = BuildSvTexture(gd, _hue);
            _svTexHue = _hue;
        }
    }

    private static Texture2D BuildHueTexture(GraphicsDevice gd)
    {
        var tex = new Texture2D(gd, 1, SwatchGen);
        var px = new Color[SwatchGen];
        for (int y = 0; y < SwatchGen; y++)
        {
            var (r, g, b) = ColorConversion.HsvToRgb(y / (float)(SwatchGen - 1) * 360f, 1f, 1f);
            px[y] = new Color(r, g, b);
        }
        tex.SetData(px);
        return tex;
    }

    private static Texture2D BuildSvTexture(GraphicsDevice gd, float hue)
    {
        var tex = new Texture2D(gd, SwatchGen, SwatchGen);
        var px = new Color[SwatchGen * SwatchGen];
        for (int y = 0; y < SwatchGen; y++)
        {
            float v = 1f - y / (float)(SwatchGen - 1);
            for (int x = 0; x < SwatchGen; x++)
            {
                float s = x / (float)(SwatchGen - 1);
                var (r, g, b) = ColorConversion.HsvToRgb(hue, s, v);
                px[y * SwatchGen + x] = new Color(r, g, b);
            }
        }
        tex.SetData(px);
        return tex;
    }
}

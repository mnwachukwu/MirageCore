using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Shell.Input;
using Mirage.Shared;
using TextCopy;

namespace Mirage.Client.Shell.Ui;

/// <summary>Single-line text entry field with caret, selection, clipboard, and key-repeat support.</summary>
public sealed class TextInputField
{
    public string Text => _text;
    public int MaxLength { get; init; } = 64;
    public bool IsPassword { get; init; }

    private string _text = "";
    private int _caretIndex;
    private int _viewOffset;
    private int _anchorIndex = -1;

    // Cached from the last Draw() call so HandleMouseClick() can hit-test without extra parameters.
    private SpriteFont? _cachedFont;
    private Rectangle _cachedBounds;

    public void Clear()
    {
        _text = "";
        _caretIndex = 0;
        _viewOffset = 0;
        _anchorIndex = -1;
    }

    public void SetText(string value)
    {
        _text = value[..Math.Min(value.Length, MaxLength)];
        _caretIndex = _text.Length;
        _viewOffset = 0;
        _anchorIndex = -1;
    }

    /// <summary>Call each frame while this field has focus to process keyboard input.</summary>
    public void Feed(InputState input, long nowMs)
    {
        bool ctrl = input.IsKeyDown(Keys.LeftControl) || input.IsKeyDown(Keys.RightControl);
        bool shift = input.IsKeyDown(Keys.LeftShift) || input.IsKeyDown(Keys.RightShift);

        foreach (char c in input.TextInput)
        {
            if (c == '\b')
            {
                if (_anchorIndex >= 0)
                {
                    DeleteSelection();
                }
                else if (_caretIndex > 0)
                {
                    _text = _text.Remove(_caretIndex - 1, 1);
                    _caretIndex--;
                }
            }
            else if (!char.IsControl(c) && _text.Length < MaxLength)
            {
                if (_anchorIndex >= 0) DeleteSelection();
                _text = _text.Insert(_caretIndex, c.ToString());
                _caretIndex++;
            }
        }

        // Ctrl+A — select all
        if (ctrl && input.IsKeyPressed(Keys.A))
        {
            _anchorIndex = 0;
            _caretIndex = _text.Length;
        }

        // Ctrl+V — paste
        if (ctrl && input.IsKeyPressed(Keys.V))
        {
            string? clip = ClipboardService.GetText();
            if (clip is not null)
            {
                if (_anchorIndex >= 0) DeleteSelection();
                string clean = FilterPaste(clip.Replace("\r", "").Replace("\n", ""));
                int room = MaxLength - _text.Length;
                if (room > 0)
                {
                    string ins = clean[..Math.Min(clean.Length, room)];
                    _text = _text.Insert(_caretIndex, ins);
                    _caretIndex += ins.Length;
                }
            }
        }

        // Ctrl+X — cut
        if (ctrl && input.IsKeyPressed(Keys.X) && _anchorIndex >= 0 && _anchorIndex != _caretIndex)
        {
            int s = Math.Min(_caretIndex, _anchorIndex);
            int e = Math.Max(_caretIndex, _anchorIndex);
            ClipboardService.SetText(_text[s..e]);
            DeleteSelection();
        }

        // Ctrl+C — copy
        if (ctrl && input.IsKeyPressed(Keys.C) && _anchorIndex >= 0 && _anchorIndex != _caretIndex)
        {
            int s = Math.Min(_caretIndex, _anchorIndex);
            int e = Math.Max(_caretIndex, _anchorIndex);
            ClipboardService.SetText(_text[s..e]);
        }

        // Ctrl+Left — jump word left
        if (ctrl && input.IsKeyPressedOrRepeating(Keys.Left, nowMs))
        {
            if (_anchorIndex >= 0 && !shift)
            {
                _caretIndex = Math.Min(_caretIndex, _anchorIndex);
                _anchorIndex = -1;
            }
            else
            {
                if (shift && _anchorIndex < 0) _anchorIndex = _caretIndex;
                _caretIndex = PrevWordBoundary(_text, _caretIndex);
                if (!shift) _anchorIndex = -1;
            }
        }

        // Ctrl+Right — jump word right
        if (ctrl && input.IsKeyPressedOrRepeating(Keys.Right, nowMs))
        {
            if (_anchorIndex >= 0 && !shift)
            {
                _caretIndex = Math.Max(_caretIndex, _anchorIndex);
                _anchorIndex = -1;
            }
            else
            {
                if (shift && _anchorIndex < 0) _anchorIndex = _caretIndex;
                _caretIndex = NextWordBoundary(_text, _caretIndex);
                if (!shift) _anchorIndex = -1;
            }
        }

        // Left arrow
        if (!ctrl && input.IsKeyPressedOrRepeating(Keys.Left, nowMs))
        {
            if (_anchorIndex >= 0 && !shift)
            {
                _caretIndex = Math.Min(_caretIndex, _anchorIndex);
                _anchorIndex = -1;
            }
            else
            {
                if (shift && _anchorIndex < 0) _anchorIndex = _caretIndex;
                if (_caretIndex > 0) _caretIndex--;
                if (!shift) _anchorIndex = -1;
            }
        }

        // Right arrow
        if (!ctrl && input.IsKeyPressedOrRepeating(Keys.Right, nowMs))
        {
            if (_anchorIndex >= 0 && !shift)
            {
                _caretIndex = Math.Max(_caretIndex, _anchorIndex);
                _anchorIndex = -1;
            }
            else
            {
                if (shift && _anchorIndex < 0) _anchorIndex = _caretIndex;
                if (_caretIndex < _text.Length) _caretIndex++;
                if (!shift) _anchorIndex = -1;
            }
        }

        // Home
        if (input.IsKeyPressed(Keys.Home))
        {
            if (shift && _anchorIndex < 0) _anchorIndex = _caretIndex;
            _caretIndex = 0;
            if (!shift) _anchorIndex = -1;
        }

        // End
        if (input.IsKeyPressed(Keys.End))
        {
            if (shift && _anchorIndex < 0) _anchorIndex = _caretIndex;
            _caretIndex = _text.Length;
            if (!shift) _anchorIndex = -1;
        }

        // Delete
        if (input.IsKeyPressedOrRepeating(Keys.Delete, nowMs))
        {
            if (_anchorIndex >= 0)
                DeleteSelection();
            else if (_caretIndex < _text.Length)
                _text = _text.Remove(_caretIndex, 1);
        }
    }

    /// <summary>
    /// Call when a mouse click lands within this field's bounds to place the caret.
    /// Pass the raw mouse X and whether Shift is held (for click-extend selection).
    /// </summary>
    public void HandleMouseClick(int mouseX, bool shift)
    {
        if (_cachedFont is null) return;
        string display = DisplayText();
        int newCaret = HitTestCaret(display, mouseX - (_cachedBounds.X + 4));
        if (shift && _anchorIndex < 0) _anchorIndex = _caretIndex;
        _caretIndex = newCaret;
        if (!shift) _anchorIndex = -1;
    }

    public void Draw(SpriteBatch sb, SpriteFont font, Rectangle bounds, bool focused, long nowMs)
    {
        _cachedFont = font;
        _cachedBounds = bounds;

        UiHelper.DrawFilledRect(sb, bounds, UiHelper.TextInputBg);
        UiHelper.DrawBorder(sb, bounds, focused ? Color.CornflowerBlue : Color.Gray);

        string display = DisplayText();
        const int pad = 4;
        int availW = bounds.Width - pad * 2;

        _caretIndex = Math.Clamp(_caretIndex, 0, _text.Length);
        if (_anchorIndex >= 0) _anchorIndex = Math.Clamp(_anchorIndex, 0, _text.Length);

        // Scroll viewOffset to keep caret visible
        _viewOffset = Math.Min(_viewOffset, _caretIndex);
        while (availW > 0 && _viewOffset < _caretIndex &&
               font.MeasureString(display[_viewOffset.._caretIndex]).X > availW)
        {
            _viewOffset++;
        }

        // Trim right until visible portion fits within availW
        string allVis = display[_viewOffset..];
        int visCnt = allVis.Length;
        while (visCnt > 0 && font.MeasureString(allVis[..visCnt]).X > availW)
            visCnt--;
        string visText = allVis[..visCnt];
        float textStartX = bounds.X + pad;
        float textY = bounds.Y + (bounds.Height - font.LineSpacing) / 2f;

        // Selection highlight
        if (focused && _anchorIndex >= 0 && _anchorIndex != _caretIndex)
        {
            int selS = Math.Clamp(Math.Min(_caretIndex, _anchorIndex) - _viewOffset, 0, visCnt);
            int selE = Math.Clamp(Math.Max(_caretIndex, _anchorIndex) - _viewOffset, 0, visCnt);
            if (selS < selE)
            {
                float px = selS > 0 ? font.MeasureString(visText[..selS]).X : 0f;
                float pw = font.MeasureString(visText[selS..selE]).X;
                UiHelper.DrawFilledRect(sb,
                    new Rectangle((int)(textStartX + px), bounds.Y + 2, (int)pw, bounds.Height - 4),
                    UiHelper.TextInputSelectionHighlight);
            }
        }

        sb.DrawString(font, visText, new Vector2(textStartX, textY), Color.White);

        // Blinking caret
        if (focused && (nowMs / 500) % 2 == 0)
        {
            int caretOff = Math.Clamp(_caretIndex - _viewOffset, 0, visCnt);
            float cx = textStartX + (caretOff > 0 ? font.MeasureString(visText[..caretOff]).X : 0f);
            UiHelper.DrawFilledRect(sb,
                new Rectangle((int)cx, bounds.Y + 2, 1, bounds.Height - 4),
                Color.White);
        }
    }

    private string DisplayText() => IsPassword ? new string('*', _text.Length) : _text;

    private int HitTestCaret(string display, int pixelOffset)
    {
        if (_cachedFont is null || pixelOffset <= 0) return _viewOffset;
        string visText = display[_viewOffset..];
        for (int i = 1; i <= visText.Length; i++)
        {
            float left = i > 1 ? _cachedFont.MeasureString(visText[..(i - 1)]).X : 0f;
            float right = _cachedFont.MeasureString(visText[..i]).X;
            if (pixelOffset < (left + right) / 2f) return _viewOffset + i - 1;
        }
        return display.Length;
    }

    private void DeleteSelection()
    {
        if (_anchorIndex < 0) return;
        int start = Math.Min(_caretIndex, _anchorIndex);
        int end = Math.Max(_caretIndex, _anchorIndex);
        _text = _text.Remove(start, end - start);
        _caretIndex = start;
        _anchorIndex = -1;
    }

    private static int PrevWordBoundary(string text, int pos)
    {
        while (pos > 0 && char.IsWhiteSpace(text[pos - 1])) pos--;
        while (pos > 0 && !char.IsWhiteSpace(text[pos - 1])) pos--;
        return pos;
    }

    private static string FilterPaste(string s) => TextValidation.Filter(s);

    private static int NextWordBoundary(string text, int pos)
    {
        while (pos < text.Length && !char.IsWhiteSpace(text[pos])) pos++;
        while (pos < text.Length && char.IsWhiteSpace(text[pos])) pos++;
        return pos;
    }
}

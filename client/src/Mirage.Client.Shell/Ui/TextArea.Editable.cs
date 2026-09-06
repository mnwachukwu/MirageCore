using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Shell.Input;
using Mirage.Shared;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using TextCopy;

namespace Mirage.Client.Shell.Ui;

/// <summary>The editable mode — dormant while ReadOnly — that backs the multi-line inputs: caret
/// motion, insertion and deletion, and its own wrap and scroll bookkeeping.</summary>
public sealed partial class TextArea
{
    // ── Editable-mode implementation (dormant while ReadOnly) ──────────────────

    private bool HasSelection => _editSelAnchor >= 0 && _editSelAnchor != _caretIndex;

    private void EditableUpdate(InputState input, bool keyboardActive)
    {
        long nowMs = Environment.TickCount64;
        var logRect = LogRect();
        _lastMousePos = input.MousePosition;

        // Click places the caret (and starts a drag-selection); shift-click extends the selection.
        if (input.IsPressIn(logRect))
        {
            _editFocused = true;
            int idx = PixelToCaret(input.MousePosition, logRect);
            bool shift = input.IsKeyDown(Keys.LeftShift) || input.IsKeyDown(Keys.RightShift);
            if (!shift) _editSelAnchor = idx;
            else if (_editSelAnchor < 0) _editSelAnchor = _caretIndex;
            _caretIndex = idx;
            _dragging = true;
            input.CaptureMouse(this);
        }
        else if (input.IsMouseJustPressed() && !logRect.Contains(input.MousePosition))
        {
            _editFocused = false;
        }

        if (_dragging)
        {
            if (input.IsMouseDown())
            {
                _caretIndex = PixelToCaret(input.MousePosition, logRect);
                if (input.MousePosition.Y < logRect.Y) _editScroll = Math.Max(0, _editScroll - 1);
                else if (input.MousePosition.Y > logRect.Bottom) _editScroll++;
            }
            else
            {
                _dragging = false;
                if (_editSelAnchor == _caretIndex) _editSelAnchor = -1;   // a plain click, no span
            }
        }

        int wheel = input.ScrollWheelDelta();
        if (wheel != 0 && logRect.Contains(input.MousePosition))
            _editScroll = Math.Max(0, _editScroll - wheel / 120);

        if (keyboardActive && _editFocused)
            EditableKeys(input, nowMs);
    }

    private void EditableKeys(InputState input, long nowMs)
    {
        bool ctrl = input.IsKeyDown(Keys.LeftControl) || input.IsKeyDown(Keys.RightControl);
        bool shift = input.IsKeyDown(Keys.LeftShift) || input.IsKeyDown(Keys.RightShift);

        foreach (char c in input.TextInput)
        {
            if (c == '\b') Backspace();
            else if (!char.IsControl(c)) InsertChar(c);
        }
        if (input.IsKeyPressed(Keys.Enter)) InsertChar('\n');

        if (ctrl && input.IsKeyPressed(Keys.A))
        {
            _editSelAnchor = 0;
            _caretIndex = _editText.Length;
        }
        if (ctrl && input.IsKeyPressed(Keys.C)) CopySelection();
        if (ctrl && input.IsKeyPressed(Keys.X))
        {
            CopySelection();
            if (HasSelection) DeleteSelection();
        }
        if (ctrl && input.IsKeyPressed(Keys.V)) PasteText();

        if (input.IsKeyPressedOrRepeating(Keys.Left, nowMs)) MoveCaret(-1, shift);
        if (input.IsKeyPressedOrRepeating(Keys.Right, nowMs)) MoveCaret(1, shift);
        if (input.IsKeyPressedOrRepeating(Keys.Up, nowMs)) MoveCaretVertical(-1, shift);
        if (input.IsKeyPressedOrRepeating(Keys.Down, nowMs)) MoveCaretVertical(1, shift);
        if (input.IsKeyPressed(Keys.Home)) SetCaret(VisualLineHome(_caretIndex), shift);
        if (input.IsKeyPressed(Keys.End)) SetCaret(VisualLineEnd(_caretIndex), shift);
        if (input.IsKeyPressedOrRepeating(Keys.Delete, nowMs)) DeleteForward();
    }

    private void InsertChar(char c)
    {
        if (HasSelection) DeleteSelection();
        if (_editText.Length >= MaxLength) return;
        _editText = _editText.Insert(_caretIndex, c.ToString());
        _caretIndex++;
        _editSelAnchor = -1;
        _editContentVersion++;
    }

    private void Backspace()
    {
        if (HasSelection)
        {
            DeleteSelection();
            return;
        }
        if (_caretIndex > 0)
        {
            _editText = _editText.Remove(_caretIndex - 1, 1);
            _caretIndex--;
            _editContentVersion++;
        }
    }

    private void DeleteForward()
    {
        if (HasSelection)
        {
            DeleteSelection();
            return;
        }
        if (_caretIndex < _editText.Length)
        {
            _editText = _editText.Remove(_caretIndex, 1);
            _editContentVersion++;
        }
    }

    private void DeleteSelection()
    {
        int s = Math.Min(_editSelAnchor, _caretIndex), e = Math.Max(_editSelAnchor, _caretIndex);
        _editText = _editText.Remove(s, e - s);
        _caretIndex = s;
        _editSelAnchor = -1;
        _editContentVersion++;
    }

    private void MoveCaret(int delta, bool shift)
    {
        if (!shift && HasSelection)
        {
            _caretIndex = delta < 0 ? Math.Min(_caretIndex, _editSelAnchor) : Math.Max(_caretIndex, _editSelAnchor);
            _editSelAnchor = -1;
            return;
        }
        if (shift && _editSelAnchor < 0) _editSelAnchor = _caretIndex;
        _caretIndex = Math.Clamp(_caretIndex + delta, 0, _editText.Length);
        if (!shift) _editSelAnchor = -1;
    }

    private void SetCaret(int idx, bool shift)
    {
        if (shift && _editSelAnchor < 0) _editSelAnchor = _caretIndex;
        _caretIndex = Math.Clamp(idx, 0, _editText.Length);
        if (!shift) _editSelAnchor = -1;
    }

    private void MoveCaretVertical(int dir, bool shift)
    {
        if (_visualLines.Count == 0) return;
        var (line, col) = CaretToVisual(_caretIndex);
        int target = line + dir;
        if (target < 0 || target >= _visualLines.Count) return;
        SetCaret(_visualSrcStart[target] + Math.Min(col, _visualLines[target].text.Length), shift);
    }

    private int VisualLineHome(int caret)
    {
        var (l, _) = CaretToVisual(caret);
        return _visualSrcStart[l];
    }
    private int VisualLineEnd(int caret)
    {
        var (l, _) = CaretToVisual(caret);
        return _visualSrcStart[l] + _visualLines[l].text.Length;
    }

    private void CopySelection()
    {
        if (!HasSelection) return;
        int s = Math.Min(_editSelAnchor, _caretIndex), e = Math.Max(_editSelAnchor, _caretIndex);
        ClipboardService.SetText(_editText[s..e]);
    }

    private void PasteText()
    {
        string? clip = ClipboardService.GetText();
        if (string.IsNullOrEmpty(clip)) return;
        if (HasSelection) DeleteSelection();
        var parts = clip.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        for (int i = 0; i < parts.Length; i++) parts[i] = FilterForFont(parts[i]);
        string clean = string.Join("\n", parts);
        int room = MaxLength - _editText.Length;
        if (room <= 0) return;
        if (clean.Length > room) clean = clean[..room];
        _editText = _editText.Insert(_caretIndex, clean);
        _caretIndex += clean.Length;
        _editSelAnchor = -1;
        _editContentVersion++;
    }

    // Wraps _editText into visual lines, tracking each line's start index in the source so caret + click map
    // exactly. Soft wraps keep the boundary space on the ending line (so every source char is covered by one
    // visual line); an explicit '\n' forces a break.
    private void WrapEdit(SpriteFont font, int availW)
    {
        if (_editWrapVersion == _editContentVersion && _editWrapWidth == availW) return;
        _editWrapVersion = _editContentVersion;
        _editWrapWidth = availW;
        _visualLines.Clear();
        _visualSrcStart.Clear();

        int i = 0, n = _editText.Length;
        while (true)
        {
            int nl = _editText.IndexOf('\n', i);
            int paraEnd = nl < 0 ? n : nl;
            if (i == paraEnd)
            {
                _visualLines.Add(("", 0, false));
                _visualSrcStart.Add(i);
            }
            else
            {
                int pos = i;
                bool first = true;
                while (pos < paraEnd)
                {
                    string remaining = _editText[pos..paraEnd];
                    if (availW <= 0 || font.MeasureString(remaining).X <= availW)
                    {
                        _visualLines.Add((remaining, 0, !first));
                        _visualSrcStart.Add(pos);
                        break;
                    }
                    int lo = 1, hi = remaining.Length - 1, cut = 1;
                    while (lo <= hi)
                    {
                        int mid = (lo + hi) / 2;
                        if (font.MeasureString(remaining[..mid]).X <= availW)
                        {
                            cut = mid;
                            lo = mid + 1;
                        }
                        else
                        {
                            hi = mid - 1;
                        }
                    }
                    int sp = remaining.LastIndexOf(' ', cut - 1);
                    int take = sp > 0 ? sp + 1 : cut;   // keep the boundary space on this line
                    _visualLines.Add((remaining[..take], 0, !first));
                    _visualSrcStart.Add(pos);
                    pos += take;
                    first = false;
                }
            }
            if (nl < 0) break;
            i = nl + 1;
            if (i == n)
            {
                _visualLines.Add(("", 0, false));
                _visualSrcStart.Add(i);
                break;
            }  // trailing newline
        }
        if (_visualLines.Count == 0)
        {
            _visualLines.Add(("", 0, false));
            _visualSrcStart.Add(0);
        }
    }

    private (int Line, int Col) CaretToVisual(int caret)
    {
        for (int k = _visualLines.Count - 1; k >= 0; k--)
        {
            if (_visualSrcStart[k] <= caret)
                return (k, Math.Min(caret - _visualSrcStart[k], _visualLines[k].text.Length));
        }

        return (0, 0);
    }

    private int PixelToCaret(Point p, Rectangle logRect)
    {
        if (_visualLines.Count == 0 || _editFont is null) return _caretIndex;
        int visible = VisibleLines();
        int row = Math.Clamp((p.Y - logRect.Y) / LineH, 0, visible - 1);
        int k = Math.Clamp(_editScroll + row, 0, _visualLines.Count - 1);
        string line = _visualLines[k].text;
        float relX = p.X - (logRect.X + 2);
        int col = 0;
        for (int ci = 0; ci < line.Length; ci++)
        {
            float le = ci > 0 ? _editFont.MeasureString(line[..ci]).X : 0f;
            float re = _editFont.MeasureString(line[..(ci + 1)]).X;
            if (relX < (le + re) / 2f) break;
            col = ci + 1;
        }
        return Math.Min(_visualSrcStart[k] + col, _editText.Length);
    }

    private void EditableDraw(SpriteBatch sb, SpriteFont font, long nowMs)
    {
        var logRect = LogRect();
        _editFont = font;
        WrapEdit(font, logRect.Width - 4);
        int visible = VisibleLines();

        var (caretLine, caretCol) = CaretToVisual(_caretIndex);
        if (caretLine < _editScroll) _editScroll = caretLine;
        else if (caretLine >= _editScroll + visible) _editScroll = caretLine - visible + 1;
        _editScroll = Math.Clamp(_editScroll, 0, Math.Max(0, _visualLines.Count - visible));

        if (HasSelection)
        {
            int selS = Math.Min(_editSelAnchor, _caretIndex), selE = Math.Max(_editSelAnchor, _caretIndex);
            for (int r = 0; r < visible; r++)
            {
                int k = _editScroll + r;
                if (k >= _visualLines.Count) break;
                string lineText = _visualLines[k].text;
                int ls = _visualSrcStart[k], le = ls + lineText.Length;
                int os = Math.Max(selS, ls), oe = Math.Min(selE, le);
                if (os < oe)
                {
                    int cS = os - ls, cE = oe - ls;
                    float hx = logRect.X + 2 + (cS > 0 ? font.MeasureString(lineText[..cS]).X : 0f);
                    float hw = font.MeasureString(lineText[cS..cE]).X;
                    UiHelper.DrawFilledRect(sb, new Rectangle((int)hx, logRect.Y + r * LineH, Math.Max(1, (int)hw), LineH), UiHelper.TextAreaSelectionHighlight);
                }
            }
        }

        for (int r = 0; r < visible; r++)
        {
            int k = _editScroll + r;
            if (k >= _visualLines.Count) break;
            sb.DrawString(font, _visualLines[k].text, new Vector2(logRect.X + 2, logRect.Y + r * LineH), Color.White);
        }

        if (_editFocused && (nowMs / 500) % 2 == 0 && caretLine >= _editScroll && caretLine < _editScroll + visible)
        {
            string lineText = _visualLines[caretLine].text;
            int col = Math.Clamp(caretCol, 0, lineText.Length);
            float cx = logRect.X + 2 + (col > 0 ? font.MeasureString(lineText[..col]).X : 0f);
            UiHelper.DrawFilledRect(sb, new Rectangle((int)cx, logRect.Y + (caretLine - _editScroll) * LineH, 1, LineH), Color.White);
        }

        var track = SbTrackRect();
        UiHelper.DrawFilledRect(sb, track, UiHelper.TextAreaSbTrackBg);
        UiHelper.DrawBorder(sb, track, UiHelper.TextAreaSbTrackBorder);
        if (_visualLines.Count > visible)
        {
            int thumbH = Math.Max(16, track.Height * visible / _visualLines.Count);
            int maxOff = _visualLines.Count - visible;
            int thumbY = maxOff > 0 ? track.Y + (track.Height - thumbH) * _editScroll / maxOff : track.Y;
            var thumb = new Rectangle(track.X, thumbY, track.Width, thumbH);
            UiHelper.DrawFilledRect(sb, thumb, UiHelper.TextAreaSbThumbBg);
            UiHelper.DrawBorder(sb, thumb, UiHelper.TextAreaSbThumbBorder);
        }
    }
}

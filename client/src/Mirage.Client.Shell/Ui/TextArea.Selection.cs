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

/// <summary>Mouse selection over wrapped text: flat-index mapping, pixel-to-character resolution,
/// and extracting the selected run for the clipboard.</summary>
public sealed partial class TextArea
{
    // ── Selection helpers ─────────────────────────────────────────────────────

    private int FlatIndex(int lineIdx, int col)
    {
        if (_visualLines.Count == 0) return 0;
        lineIdx = Math.Clamp(lineIdx, 0, _visualLines.Count - 1);
        int flat = 0;
        for (int i = 0; i < lineIdx; i++)
            flat += _visualLines[i].text.Length + 1;
        return flat + Math.Clamp(col, 0, _visualLines[lineIdx].text.Length);
    }

    private int TotalFlatLength()
    {
        int total = 0;
        for (int i = 0; i < _visualLines.Count; i++)
        {
            if (i > 0) total++;
            total += _visualLines[i].text.Length;
        }
        return total;
    }

    private int ResolvePixel(Point pt, Rectangle logRect, SpriteFont font, int firstIdx, int visible)
    {
        int row = Math.Clamp((pt.Y - logRect.Y) / LineH, 0, visible - 1);
        int lineIdx = Math.Clamp(firstIdx + row, 0, Math.Max(0, _visualLines.Count - 1));
        if (_visualLines.Count == 0) return 0;
        var (lineText, _, _) = _visualLines[lineIdx];
        float textX = logRect.X + 2;
        float relX = pt.X - textX;
        int col = 0;
        for (int ci = 0; ci < lineText.Length; ci++)
        {
            float le = ci > 0 ? font.MeasureString(lineText[..ci]).X : 0f;
            float re = font.MeasureString(lineText[..(ci + 1)]).X;
            if (relX < (le + re) / 2f) break;
            col = ci + 1;
        }
        return FlatIndex(lineIdx, col);
    }

    private string ExtractSelection()
    {
        if (_anchorFlat < 0 || _caretFlat < 0 || _anchorFlat == _caretFlat) return "";
        int start = Math.Min(_anchorFlat, _caretFlat);
        int end = Math.Max(_anchorFlat, _caretFlat);
        var result = new System.Text.StringBuilder();
        int flat = 0;
        for (int i = 0; i < _visualLines.Count; i++)
        {
            if (i > 0)
            {
                if (flat >= start && flat < end)
                    result.Append(_visualLines[i].isContinuation ? ' ' : '\n');
                flat++;
            }
            var line = _visualLines[i].text;
            for (int c = 0; c < line.Length; c++, flat++)
                if (flat >= start && flat < end) result.Append(line[c]);
        }
        return result.ToString();
    }
}

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

/// <summary>Geometry and word wrap: the log and scrollbar rectangles, the visual-line rebuild, and
/// the span projection that carries links, names and colors across a wrap.</summary>
public sealed partial class TextArea
{
    // ── Layout helpers ─────────────────────────────────────────────────────────

    // Text area — leaves SbWidth on the right for the scrollbar.
    private Rectangle LogRect() =>
        new(_bounds.X, _bounds.Y, _bounds.Width - SbWidth, _bounds.Height);

    private Rectangle SbTrackRect() =>
        new(_bounds.Right - SbWidth, _bounds.Y, SbWidth, _bounds.Height);

    private Rectangle SbThumbRect()
    {
        var track = SbTrackRect();
        int visible = VisibleLines();
        int total = _visualLines.Count;
        if (total <= visible) return track;

        int thumbH = Math.Max(16, track.Height * visible / total);
        int maxOff = total - visible;
        // Offset 0 = newest = thumb at bottom; offset max = oldest = thumb at top.
        int thumbY = maxOff > 0
            ? track.Y + (track.Height - thumbH) * (maxOff - _scrollOffset) / maxOff
            : track.Y;
        return new Rectangle(track.X, thumbY, track.Width, thumbH);
    }

    private int VisibleLines() => Math.Max(1, LogRect().Height / LineH);

    // Walks the cached pixel rects produced by the previous Draw and returns the URL whose
    // rect contains the point, or null. One frame of lag after a layout change is harmless
    // — the rects re-stabilize immediately on the next Draw.
    private string? LinkAt(Point p)
    {
        for (int i = 0; i < _linkHitRects.Count; i++)
            if (_linkHitRects[i].rect.Contains(p)) return _linkHitRects[i].url;
        return null;
    }

    /// <summary>Returns the player name at <paramref name="p"/> if a name span is hit, else null.
    /// ChatPanel uses this on right-click to open the player context menu.</summary>
    public string? NameAt(Point p)
    {
        for (int i = 0; i < _nameHitRects.Count; i++)
            if (_nameHitRects[i].rect.Contains(p)) return _nameHitRects[i].name;
        return null;
    }

    private static string FilterForFont(string s) => TextValidation.Filter(s);

    // ── Word wrap ─────────────────────────────────────────────────────────────

    private void RebuildVisualLines(SpriteFont font, int availW)
    {
        if (_linesVersion == _cachedVersion && availW == _cachedWrapWidth) return;
        _visualLines.Clear();
        _visualLineLinks.Clear();
        _visualLineNames.Clear();
        _visualLineColors.Clear();
        for (int li = 0; li < _lines.Count; li++)
        {
            var (text, colorIndex) = _lines[li];
            var links = _sourceLineLinks[li];
            var names = _sourceLineNames[li];
            var colors = _sourceLineColors[li];
            string prefix = BuildPrefix(_lineTimes[li], _lineChannels[li], out int grayLen);
            if (prefix.Length > 0)
            {
                // Splice the "[time] [channel] " prefix onto the front of the source text and rebase
                // every span by its length so names/links/colors still line up. Wrapping the prefixed
                // text lands the prefix on the first visual line only; continuations carry none. Only
                // the leading timestamp run (grayLen) is grayed; the channel keeps the base color.
                int shift = prefix.Length;
                text = prefix + text;
                links = ShiftLinks(links, shift);
                names = ShiftNames(names, shift);
                colors = WithPrefixColor(colors, shift, grayLen);
            }
            WrapInto(text, colorIndex, links, names, colors,
                font, availW,
                _visualLines, _visualLineLinks, _visualLineNames, _visualLineColors);
        }
        _cachedVersion = _linesVersion;
        _cachedWrapWidth = availW;
    }

    // Assembles the bracketed prefix from whichever of the two metadata bits is enabled for this line:
    // "[time]", "[channel]", or "[time] [channel]" (a single space between the two brackets), plus one
    // trailing space as the gap before the message body. Empty string when neither applies, so the
    // caller splices nothing. A line with no channel (channelLabel == null) contributes no "[channel]"
    // even while ShowChannelLabels is on, and the spacing collapses to just "[time] ".
    // <paramref name="grayLen"/> returns the length of the leading timestamp run (0 when no timestamp)
    // so the caller grays only that part; the channel label stays in the message's base color.
    private string BuildPrefix(DateTime time, string? channelLabel, out int grayLen)
    {
        bool showTime = _showTimestamps;
        bool showChannel = _showChannelLabels && channelLabel is not null;
        grayLen = 0;
        if (!showTime && !showChannel) return "";
        string prefix = "";
        if (showTime)
        {
            prefix = $"[{FormatTime(time)}]";
            grayLen = prefix.Length;
        }
        if (showChannel)
        {
            if (prefix.Length > 0) prefix += " "; // single space between the timestamp and channel
            prefix += $"[{channelLabel}]";
        }
        return prefix + " ";
    }

    private string FormatTime(DateTime time) =>
        time.ToString(_use24HourClock ? TimeFormat24Hour : TimeFormat12Hour, CultureInfo.InvariantCulture);

    // Rebase helpers for the timestamp prefix. Each returns the source list untouched when empty so
    // the common no-span line allocates nothing; a non-empty list is copied with StartCol shifted.
    private static List<LinkSpan> ShiftLinks(List<LinkSpan> source, int shift)
    {
        if (source.Count == 0) return source;
        var result = new List<LinkSpan>(source.Count);
        foreach (var l in source) result.Add(new LinkSpan(l.StartCol + shift, l.Length, l.Url));
        return result;
    }

    private static List<NameSpan> ShiftNames(List<NameSpan> source, int shift)
    {
        if (source.Count == 0) return source;
        var result = new List<NameSpan>(source.Count);
        foreach (var n in source) result.Add(new NameSpan(n.StartCol + shift, n.Length, n.Name, n.Access, n.ShowAsPk));
        return result;
    }

    // Prepends the dim-gray span covering just the leading timestamp run (grayLen chars; 0 when there
    // is no timestamp), then the rebased source color spans. The channel label and the inter-bracket
    // spacing are left uncovered so they fall through to the line's base color and blend with the text.
    private static List<ColorSpan> WithPrefixColor(List<ColorSpan> source, int shift, int grayLen)
    {
        var result = new List<ColorSpan>(source.Count + (grayLen > 0 ? 1 : 0));
        if (grayLen > 0) result.Add(new ColorSpan(0, grayLen, TimestampColorIndex));
        foreach (var c in source) result.Add(new ColorSpan(c.StartCol + shift, c.Length, c.ColorIndex));
        return result;
    }

    // Scans the source line once for URLs. Spans are stored in source-line coordinates so the
    // wrap projection can split them across visual lines if needed without re-running regex.
    private static List<LinkSpan> DetectLinks(string text)
    {
        var spans = new List<LinkSpan>();
        if (text.Length == 0) return spans;
        foreach (Match m in UrlRegex.Matches(text))
        {
            string raw = m.Value;
            int trimmed = TrimUrlTail(raw);
            int len = raw.Length - trimmed;
            if (len <= 0) continue;
            string url = raw[..len];
            // www.example.com isn't a valid URI on its own — prepend http:// so the OS
            // shell can route it. https would refuse hosts without a TLS endpoint.
            string target = url.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                ? "http://" + url
                : url;
            spans.Add(new LinkSpan(m.Index, len, target));
        }
        return spans;
    }

    // Slices the source-line link list into the portion that overlaps the visual line's
    // source range [srcStart, srcEnd) and rebases each span's StartCol to be local to the
    // visual line. A URL that straddles a wrap boundary becomes two LinkSpans with the same
    // Url — both halves click through to the same target.
    private static List<LinkSpan> ProjectLinks(List<LinkSpan> source, int srcStart, int srcEnd)
    {
        var result = new List<LinkSpan>();
        for (int i = 0; i < source.Count; i++)
        {
            var link = source[i];
            int linkEnd = link.StartCol + link.Length;
            int s = Math.Max(link.StartCol, srcStart);
            int e = Math.Min(linkEnd, srcEnd);
            if (s < e) result.Add(new LinkSpan(s - srcStart, e - s, link.Url));
        }
        return result;
    }

    // Returns how many trailing chars to lop off the regex match: sentence punctuation, and
    // close-brackets that aren't balanced by an open-bracket inside the URL (so Wikipedia's
    // "Foo_(bar)" keeps its paren but "(www.example.com)" loses the trailing one).
    private static int TrimUrlTail(string s)
    {
        int trim = 0;
        while (trim < s.Length)
        {
            char c = s[s.Length - 1 - trim];
            if (".,!?:;\"'".IndexOf(c) >= 0)
            {
                trim++;
                continue;
            }
            int len = s.Length - trim;
            if (c == ')' && CountChar(s, 0, len, '(') < CountChar(s, 0, len, ')'))
            {
                trim++;
                continue;
            }
            if (c == ']' && CountChar(s, 0, len, '[') < CountChar(s, 0, len, ']'))
            {
                trim++;
                continue;
            }
            break;
        }
        return trim;
    }

    private static int CountChar(string s, int start, int end, char ch)
    {
        int n = 0;
        for (int i = start; i < end; i++) if (s[i] == ch) n++;
        return n;
    }

    private static void WrapInto(string text, int colorIndex, List<LinkSpan> sourceLinks,
                                  List<NameSpan> sourceNames, List<ColorSpan> sourceColors,
                                  SpriteFont font, float availW,
                                  List<(string, int, bool)> dest, List<List<LinkSpan>> destLinks,
                                  List<List<NameSpan>> destNames, List<List<ColorSpan>> destColors)
    {
        if (text.Length == 0)
        {
            dest.Add(("", colorIndex, false));
            destLinks.Add(new List<LinkSpan>());
            destNames.Add(new List<NameSpan>());
            destColors.Add(new List<ColorSpan>());
            return;
        }
        // srcStart tracks the offset in the ORIGINAL source string where this visual line
        // begins, so source-level URL/name/color spans can be projected onto each visual line.
        int srcStart = 0;
        bool isContinuation = false;
        while (srcStart < text.Length)
        {
            string remaining = text[srcStart..];
            if (font.MeasureString(remaining).X <= availW)
            {
                dest.Add((remaining, colorIndex, isContinuation));
                destLinks.Add(ProjectLinks(sourceLinks, srcStart, srcStart + remaining.Length));
                destNames.Add(ProjectNames(sourceNames, srcStart, srcStart + remaining.Length));
                destColors.Add(ProjectColors(sourceColors, srcStart, srcStart + remaining.Length));
                return;
            }
            // Binary search for the max prefix that fits.
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
            // Prefer a word boundary.
            int sp = remaining.LastIndexOf(' ', cut - 1);
            int lineEnd = sp > 0 ? sp : cut;
            int nextStart = sp > 0 ? sp + 1 : cut;
            dest.Add((remaining[..lineEnd].TrimEnd(), colorIndex, isContinuation));
            destLinks.Add(ProjectLinks(sourceLinks, srcStart, srcStart + lineEnd));
            destNames.Add(ProjectNames(sourceNames, srcStart, srcStart + lineEnd));
            destColors.Add(ProjectColors(sourceColors, srcStart, srcStart + lineEnd));
            srcStart += nextStart;
            isContinuation = true;
        }
    }

    /// <summary>Same projection rule as <see cref="ProjectLinks"/> — slices NameSpans into the
    /// visual line's source range and rebases StartCol. A speaker name straddling a wrap is
    /// rare (names are short), but if it happens both halves remain clickable for the same player.</summary>
    private static List<NameSpan> ProjectNames(List<NameSpan> source, int srcStart, int srcEnd)
    {
        var result = new List<NameSpan>();
        for (int i = 0; i < source.Count; i++)
        {
            var n = source[i];
            int end = n.StartCol + n.Length;
            int s = Math.Max(n.StartCol, srcStart);
            int e = Math.Min(end, srcEnd);
            if (s < e) result.Add(new NameSpan(s - srcStart, e - s, n.Name, n.Access, n.ShowAsPk));
        }
        return result;
    }

    /// <summary>Slices ColorSpans into the visual line's source range and rebases StartCol.
    /// Same projection rule as Links/Names.</summary>
    private static List<ColorSpan> ProjectColors(List<ColorSpan> source, int srcStart, int srcEnd)
    {
        var result = new List<ColorSpan>();
        for (int i = 0; i < source.Count; i++)
        {
            var c = source[i];
            int end = c.StartCol + c.Length;
            int s = Math.Max(c.StartCol, srcStart);
            int e = Math.Min(end, srcEnd);
            if (s < e) result.Add(new ColorSpan(s - srcStart, e - s, c.ColorIndex));
        }
        return result;
    }
}

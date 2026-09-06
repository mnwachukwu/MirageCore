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

/// <summary>
/// Scrollable, word-wrapped colored text area with a scrollbar and mouse text selection.
/// Owns no window chrome — the host (chat window, help panel, …) supplies the bounds and
/// draws any background/title around it. Shared so every "chat box" renders identically.
/// </summary>
public sealed partial class TextArea
{
    // The QBColor() 16-color palette as XNA Colors, indexed 0-15 — the render table for chat text.
    // Derived from GameColor.Rgb (the shared, single source of truth for these RGBs) so it can never
    // drift from the reserved-color set the guild picker enforces. The packed values there are the exact
    // QBColor() RGBs, so log message colors still match the original client's chat rendering.
    private static readonly Color[] GameColors = BuildPalette();

    private static Color[] BuildPalette()
    {
        var arr = new Color[GameColor.Rgb.Length];
        for (int i = 0; i < arr.Length; i++)
        {
            int rgb = GameColor.Rgb[i];
            arr[i] = new Color(GameColor.RedOf(rgb), GameColor.GreenOf(rgb), GameColor.BlueOf(rgb));
        }
        return arr;
    }

    public static Color GetColor(int index) => GameColors[Math.Clamp(index, 0, GameColors.Length - 1)];

    // Full area allotted to the log, including the scrollbar gutter on the right.
    private Rectangle _bounds;

    private readonly List<(string text, int colorIndex)> _lines = new();
    // Local wall-clock capture per source line, parallel to _lines. Frozen at AddLine so a line's
    // timestamp reflects when it arrived and stays correct even if the user only reveals timestamps
    // later. Formatted (not stored pre-formatted) so flipping the clock format reformats the buffer.
    private readonly List<DateTime> _lineTimes = new();
    // Opaque channel-label string per source line (e.g. "Say"), or null for channel-less lines
    // (client-local diagnostics, welcome batch). Parallel to _lines; the caller resolves the label
    // so this widget stays decoupled from the chat-channel enum. Shown only when ShowChannelLabels.
    private readonly List<string?> _lineChannels = new();
    private int _scrollOffset;
    // Set by ScrollToTop, resolved in Draw once the visual-line count is known. A flag rather
    // than a sentinel offset because the new-line absorb in Draw would overflow a large offset.
    private bool _pendingScrollToTop;

    // Pixel-wrapped view of _lines; rebuilt in Draw() when content or width changes.
    // isContinuation is true when the visual line was produced by word-wrapping the prior
    // line (so copy joins with a space) and false when it begins a fresh source line (so
    // copy joins with a newline).
    private readonly List<(string text, int colorIndex, bool isContinuation)> _visualLines = new();
    private int _linesVersion;
    private int _cachedVersion = -1;
    private int _cachedWrapWidth;
    private const int MaxLines = 500;
    private const int LineH = 16;
    private const int SbWidth = 8;
    // Optional "[time] [channel] " prefix. InvariantCulture keeps the AM/PM designator ASCII no matter
    // the OS locale; both time formats carry seconds since chat scrolls fast. Only the timestamp is
    // dim gray (palette 8) so it reads as a distinct side column; the channel label is left in the
    // line's base color so it blends with the message text.
    private const string TimeFormat24Hour = "HH:mm:ss";
    private const string TimeFormat12Hour = "h:mm:ss tt";
    private const int TimestampColorIndex = 8;
    // Squared pixel distance below which a press→release is treated as a link click rather
    // than a (degenerate) selection drag.
    private const int LinkClickSlopSq = 9;

    private bool _sbDragging;
    private int _sbDragStartY;
    private int _sbDragStartOffset;

    // Text selection
    private bool _focused;
    private bool _dragging;
    private Point _anchorPixel = new(-1, -1);
    private Point _caretPixel = new(-1, -1);
    private bool _selectAll;
    private int _anchorFlat = -1;
    private int _caretFlat = -1;

    // ── Editable mode ──────────────────────────────────────────────────────────
    // TextArea is the single multiline text control; editing is the DEFAULT. Read-only callers (chat log,
    // help, the mail reading pane) opt in with ReadOnly = true, restoring the append-only rich-log behavior
    // and leaving every editable path below dormant. The two modes are exclusive per instance and share only
    // low-level render/scroll helpers — the read-only Update/Draw are untouched, just guarded at the top.
    public bool ReadOnly { get; set; }
    public int MaxLength { get; init; } = int.MaxValue;
    private string _editText = "";
    private int _caretIndex;
    private int _editSelAnchor = -1;   // selection anchor in _editText (-1 = none)
    private bool _editFocused;
    private int _editScroll;            // first visible visual line (top-anchored)
    private SpriteFont? _editFont;      // cached from EditableDraw so EditableUpdate can measure clicks
    private readonly List<int> _visualSrcStart = new();   // per visual line: its start index in _editText
    private int _editWrapVersion = -1;
    private int _editWrapWidth = -1;
    private int _editContentVersion;    // bumped on every _editText mutation

    // Hyperlinks. URLs are detected once at the source-line level on AddLine (or on the
    // EnableHyperlinks toggle for already-stored lines) and held in _sourceLineLinks parallel
    // to _lines. RebuildVisualLines projects those source spans onto each visual line during
    // word-wrap, so a URL broken across two visual lines stays one logical link with one
    // shared target — clicking either half opens the same URL. Draw projects the per-visual
    // spans to pixel rects in _linkHitRects so Update can hit-test without the font.
    private readonly List<List<LinkSpan>> _sourceLineLinks = new();
    private readonly List<List<LinkSpan>> _visualLineLinks = new();
    private readonly List<(Rectangle rect, string url)> _linkHitRects = new();
    private string? _pressedLinkUrl;
    private Point _pressedLinkPos;
    private Point _lastMousePos = new(-1, -1);
    private bool _enableHyperlinks;
    public bool EnableHyperlinks
    {
        get => _enableHyperlinks;
        set
        {
            if (_enableHyperlinks == value) return;
            _enableHyperlinks = value;
            // Re-run URL detection for every already-buffered line — toggling on retroactively
            // makes prior chat clickable; toggling off drops the cached spans so the wrap
            // rebuild produces empty link lists.
            _sourceLineLinks.Clear();
            foreach (var (text, _) in _lines)
                _sourceLineLinks.Add(value ? DetectLinks(text) : new List<LinkSpan>());
            _linesVersion++;
        }
    }

    // Reveals the per-line timestamp prefix. Bumps the version so the next Draw re-wraps with the
    // stamp spliced in (or removed). Off by default.
    private bool _showTimestamps;
    public bool ShowTimestamps
    {
        get => _showTimestamps;
        set
        {
            if (_showTimestamps == value) return;
            _showTimestamps = value;
            _linesVersion++;
        }
    }

    // 24-hour vs 12-hour (AM/PM) clock for the timestamp. Only forces a re-wrap when timestamps are
    // actually visible — otherwise the change is latent until ShowTimestamps turns on.
    private bool _use24HourClock;
    public bool Use24HourClock
    {
        get => _use24HourClock;
        set
        {
            if (_use24HourClock == value) return;
            _use24HourClock = value;
            if (_showTimestamps) _linesVersion++;
        }
    }

    // Reveals each line's channel label (e.g. "[Say]") as a second bracketed prefix after the
    // timestamp. Independent of ShowTimestamps; bumps the version so the next Draw re-wraps.
    private bool _showChannelLabels;
    public bool ShowChannelLabels
    {
        get => _showChannelLabels;
        set
        {
            if (_showChannelLabels == value) return;
            _showChannelLabels = value;
            _linesVersion++;
        }
    }

    // Matches http(s):// and www. URLs up to the next whitespace or quote/angle bracket.
    // Trailing punctuation is stripped separately so periods and commas at the end of a
    // sentence don't get glued onto the URL.
    private static readonly Regex UrlRegex = new(
        @"(?:https?://|www\.)[^\s<>""]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly struct LinkSpan
    {
        public readonly int StartCol;
        public readonly int Length;
        public readonly string Url;
        public LinkSpan(int startCol, int length, string url)
        {
            StartCol = startCol;
            Length = length;
            Url = url;
        }
    }

    /// <summary>Player-name span inside a chat line. Carries the access tier and PK status
    /// frozen at send time so chat history keeps the speaker's color even after their PK
    /// timer expires. Right-click → context menu uses <see cref="NameAt(Point)"/> to resolve.</summary>
    public readonly struct NameSpan
    {
        public readonly int StartCol;
        public readonly int Length;
        public readonly string Name;
        public readonly AdminLevel Access;
        public readonly bool ShowAsPk;
        public NameSpan(int startCol, int length, string name, AdminLevel access, bool showAsPk)
        {
            StartCol = startCol;
            Length = length;
            Name = name;
            Access = access;
            ShowAsPk = showAsPk;
        }
    }

    private readonly List<List<NameSpan>> _sourceLineNames = new();
    private readonly List<List<NameSpan>> _visualLineNames = new();
    private readonly List<(Rectangle rect, string name)> _nameHitRects = new();

    /// <summary>Generic colored substring inside a line. Used by HelpPanel to paint the
    /// slash-command syntax in a different color than its description on the same line.
    /// Unlike <see cref="NameSpan"/> the color is explicit (palette index) rather than
    /// derived from access/PK; no hit-testing is exposed because nothing needs to click
    /// these segments.</summary>
    public readonly struct ColorSpan
    {
        public readonly int StartCol;
        public readonly int Length;
        public readonly int ColorIndex;
        public ColorSpan(int startCol, int length, int colorIndex)
        {
            StartCol = startCol;
            Length = length;
            ColorIndex = colorIndex;
        }
    }
    private readonly List<List<ColorSpan>> _sourceLineColors = new();
    private readonly List<List<ColorSpan>> _visualLineColors = new();

    public bool IsFocused => ReadOnly ? _focused : _editFocused;
    public bool IsEmpty => _lines.Count == 0;
    public Rectangle Bounds => _bounds;
    public void SetBounds(Rectangle bounds) => _bounds = bounds;
    public bool ContainsMouse(Point p) => _bounds.Contains(p);

    /// <summary>The editable content (meaningful when <see cref="ReadOnly"/> is false). Setting places the
    /// caret at the end and clears any selection.</summary>
    public string Text
    {
        get => _editText;
        set
        {
            _editText = value.Length > MaxLength ? value[..MaxLength] : value;
            _caretIndex = _editText.Length;
            _editSelAnchor = -1;
            _editContentVersion++;
        }
    }

    /// <summary>Empty the editable buffer.</summary>
    public void ClearText()
    {
        _editText = "";
        _caretIndex = 0;
        _editSelAnchor = -1;
        _editContentVersion++;
    }

    /// <summary>Give the editable area keyboard focus (no-op when read-only).</summary>
    public void Focus() { if (!ReadOnly) _editFocused = true; }

    /// <summary>Drops focus and clears any active selection (both read-only and editable state).</summary>
    public void Defocus()
    {
        _focused = false;
        _editFocused = false;
        _anchorFlat = -1;
        _caretFlat = -1;
        _editSelAnchor = -1;
    }

    public void AddLine(string text, int colorIndex = 0) =>
        AddLine(text, colorIndex, names: null, colors: null);

    /// <summary>Adds a line with optional player-name spans inside it. Names must reference
    /// substrings of <paramref name="text"/> (post-filter) — see ChatPanel for the only
    /// caller. Pass null for plain system lines.</summary>
    public void AddLine(string text, int colorIndex, IReadOnlyList<NameSpan>? names) =>
        AddLine(text, colorIndex, names, colors: null);

    /// <summary>Adds a line with optional color-overrides on substrings and an optional channel
    /// label. The color spans are used by HelpPanel to paint the slash-command syntax in a different
    /// color than its description on the same line; <paramref name="channelLabel"/> is the opaque
    /// per-line channel name (e.g. "Say") shown when ShowChannelLabels is on, or null for
    /// channel-less lines. All spans lists are optional and non-overlapping.</summary>
    public void AddLine(string text, int colorIndex, IReadOnlyList<NameSpan>? names, IReadOnlyList<ColorSpan>? colors, string? channelLabel = null)
    {
        string filtered = FilterForFont(text);
        _lines.Add((filtered, Math.Clamp(colorIndex, 0, GameColors.Length - 1)));
        _lineTimes.Add(DateTime.Now);
        _lineChannels.Add(channelLabel);
        _sourceLineLinks.Add(_enableHyperlinks ? DetectLinks(filtered) : new List<LinkSpan>());
        _sourceLineNames.Add(names is { Count: > 0 } ? new List<NameSpan>(names) : new List<NameSpan>());
        _sourceLineColors.Add(colors is { Count: > 0 } ? new List<ColorSpan>(colors) : new List<ColorSpan>());
        if (_lines.Count > MaxLines)
        {
            _lines.RemoveAt(0);
            _lineTimes.RemoveAt(0);
            _lineChannels.RemoveAt(0);
            _sourceLineLinks.RemoveAt(0);
            _sourceLineNames.RemoveAt(0);
            _sourceLineColors.RemoveAt(0);
        }
        _linesVersion++;
    }

    public void Clear()
    {
        _lines.Clear();
        _lineTimes.Clear();
        _lineChannels.Clear();
        _sourceLineLinks.Clear();
        _sourceLineNames.Clear();
        _sourceLineColors.Clear();
        _visualLines.Clear();
        _visualLineLinks.Clear();
        _visualLineNames.Clear();
        _visualLineColors.Clear();
        _linkHitRects.Clear();
        _nameHitRects.Clear();
        _pressedLinkUrl = null;
        _cachedVersion = -1;
        _scrollOffset = 0;
        _pendingScrollToTop = false;
    }

    /// <summary>Jump to the top of the content (oldest line first). HelpPanel calls this
    /// after Populate so a freshly-opened panel doesn't show the bottom of a long list.
    /// Deferred to the next Draw because the target offset depends on the visual-line count,
    /// which isn't known until the lines are wrapped against the current width.</summary>
    public void ScrollToTop() => _pendingScrollToTop = true;

    // keyboardActive: true when this area is the focused/active context that should own
    // keyboard actions (select-all, copy/cut). Mouse scrolling and selection always work.
    public void Update(InputState input, bool keyboardActive = true)
    {
        if (!ReadOnly)
        {
            EditableUpdate(input, keyboardActive);
            return;
        }
        var logRect = LogRect();
        _lastMousePos = input.MousePosition;

        // Hyperlink hover cursor — defer to UiHelper's per-frame cursor bus so multiple
        // TextArea instances (or other link widgets) don't race over what the OS cursor is.
        if (logRect.Contains(input.MousePosition))
        {
            if (_enableHyperlinks && LinkAt(input.MousePosition) != null)
                UiHelper.RequestHandCursor();
            else if (NameAt(input.MousePosition) != null)
                UiHelper.RequestHandCursor();
        }

        // Scroll wheel in log area — positive delta = scroll up = older messages = higher offset
        int wheel = input.ScrollWheelDelta();
        if (wheel != 0 && logRect.Contains(input.MousePosition))
        {
            int visible = VisibleLines();
            _scrollOffset = Math.Clamp(_scrollOffset + wheel / 120, 0, Math.Max(0, _visualLines.Count - visible));
        }

        // Scrollbar thumb drag
        var sbThumb = SbThumbRect();
        var sbTrack = SbTrackRect();
        if (!_sbDragging && input.IsPressIn(sbThumb))
        {
            _sbDragging = true;
            _sbDragStartY = input.MousePosition.Y;
            _sbDragStartOffset = _scrollOffset;
            input.CaptureMouse(this);
        }
        if (_sbDragging)
        {
            if (input.IsMouseDown())
            {
                int visible = VisibleLines();
                int maxOff = Math.Max(0, _visualLines.Count - visible);
                int trackH = sbTrack.Height - SbThumbRect().Height;
                if (trackH > 0 && maxOff > 0)
                {
                    int dy = input.MousePosition.Y - _sbDragStartY;
                    _scrollOffset = Math.Clamp(_sbDragStartOffset - dy * maxOff / trackH, 0, maxOff);
                }
            }
            else
            {
                _sbDragging = false;   // capture auto-releases the frame after button-up
            }
        }

        // Mouse press outside log → defocus and clear selection
        if (input.IsMouseJustPressed() && _focused && !logRect.Contains(input.MousePosition))
            Defocus();

        // Log-area selection drag — captures the pointer so a sweep that ends over another widget
        // can't click it, and the release is resolved here (the global click edge is capture-suppressed).
        if (input.IsPressIn(logRect) && !_sbDragging)
        {
            _focused = true;
            _dragging = true;
            _anchorPixel = input.MousePosition;
            _caretPixel = input.MousePosition;
            _anchorFlat = -1;
            _caretFlat = -1;
            _selectAll = false;
            input.CaptureMouse(this);
            // Remember a link target hit on press so we only follow it if the same link is still
            // under the mouse on release without a real drag — a quick click, not a selection sweep
            // that happens to start over a link.
            _pressedLinkUrl = _enableHyperlinks ? LinkAt(input.MousePosition) : null;
            _pressedLinkPos = input.MousePosition;
        }
        if (_dragging)
        {
            if (input.IsMouseDown())
            {
                _caretPixel = input.MousePosition;
                int vis = VisibleLines();
                if (input.MousePosition.Y < logRect.Y)
                    _scrollOffset = Math.Min(_scrollOffset + 1, Math.Max(0, _visualLines.Count - vis));
                else if (input.MousePosition.Y > logRect.Bottom)
                    _scrollOffset = Math.Max(_scrollOffset - 1, 0);
            }
            else
            {
                // Release ends the drag. Open a link only if this was a barely-moved click that began
                // and ended on the same link — capture suppresses the global click edge for this release.
                if (_pressedLinkUrl != null)
                {
                    int dx = input.MousePosition.X - _pressedLinkPos.X;
                    int dy = input.MousePosition.Y - _pressedLinkPos.Y;
                    if (LinkAt(input.MousePosition) == _pressedLinkUrl && dx * dx + dy * dy <= LinkClickSlopSq)
                        OpenUrl(_pressedLinkUrl);
                }
                _pressedLinkUrl = null;
                _dragging = false;   // capture auto-releases the frame after button-up
            }
        }

        bool ctrl = input.IsKeyDown(Keys.LeftControl) || input.IsKeyDown(Keys.RightControl);

        // Keyboard actions only when this area owns keyboard focus.
        if (keyboardActive && _focused && ctrl)
        {
            // Ctrl+A — select all text (flat indices resolved in Draw)
            if (input.IsKeyPressed(Keys.A))
            {
                _selectAll = true;
                _dragging = false;
            }

            // Ctrl+C / Ctrl+X — copy selection (read-only, so cut behaves as copy)
            if (input.IsKeyPressed(Keys.C) || input.IsKeyPressed(Keys.X))
            {
                string selected = ExtractSelection();
                if (!string.IsNullOrEmpty(selected))
                    ClipboardService.SetText(selected);
            }
        }
    }

    public void Draw(SpriteBatch sb, SpriteFont font, long nowMs)
    {
        if (!ReadOnly)
        {
            EditableDraw(sb, font, nowMs);
            return;
        }
        var logRect = LogRect();
        int prevVisualCount = _visualLines.Count;
        RebuildVisualLines(font, logRect.Width - 4);
        int visible = VisibleLines();
        if (_pendingScrollToTop)
        {
            // Resolve a deferred ScrollToTop now that the wrapped line count is known: the
            // largest valid offset pins firstIdx to 0 (the oldest line). Takes precedence over
            // the new-line absorb below, which would otherwise overflow the sentinel offset.
            _scrollOffset = Math.Max(0, _visualLines.Count - visible);
            _pendingScrollToTop = false;
        }
        // When scrolled up, shift offset to absorb new visual lines so the view stays put.
        else if (_scrollOffset > 0 && _visualLines.Count > prevVisualCount)
        {
            _scrollOffset += _visualLines.Count - prevVisualCount;
        }

        _scrollOffset = Math.Clamp(_scrollOffset, 0, Math.Max(0, _visualLines.Count - visible));
        int firstIdx = Math.Max(0, _visualLines.Count - visible - _scrollOffset);

        // Resolve pending pixel → flat selection coordinates (needs font metrics)
        if (_selectAll)
        {
            _anchorFlat = 0;
            _caretFlat = TotalFlatLength();
            _selectAll = false;
        }
        if (_anchorPixel.X >= 0)
        {
            _anchorFlat = ResolvePixel(_anchorPixel, logRect, font, firstIdx, visible);
            _anchorPixel = new(-1, -1);
        }
        if (_caretPixel.X >= 0)
        {
            _caretFlat = ResolvePixel(_caretPixel, logRect, font, firstIdx, visible);
            _caretPixel = new(-1, -1);
        }

        // Selection highlight — drawn before text so text renders on top
        if (_anchorFlat >= 0 && _caretFlat >= 0 && _anchorFlat != _caretFlat)
        {
            int selStart = Math.Min(_anchorFlat, _caretFlat);
            int selEnd = Math.Max(_anchorFlat, _caretFlat);
            for (int i = 0; i < visible; i++)
            {
                int idx = firstIdx + i;
                if (idx >= _visualLines.Count) break;
                var (lineText, _, _) = _visualLines[idx];
                int lineFlat = FlatIndex(idx, 0);
                int overlapStart = Math.Max(selStart, lineFlat);
                int overlapEnd = Math.Min(selEnd, lineFlat + lineText.Length);
                if (overlapStart < overlapEnd)
                {
                    int colS = overlapStart - lineFlat;
                    int colE = overlapEnd - lineFlat;
                    float hx = logRect.X + 2 + (colS > 0 ? font.MeasureString(lineText[..colS]).X : 0f);
                    float hw = font.MeasureString(lineText[colS..colE]).X;
                    UiHelper.DrawFilledRect(sb,
                        new Rectangle((int)hx, logRect.Y + i * LineH, Math.Max(1, (int)hw), LineH),
                        UiHelper.TextAreaSelectionHighlight);
                }
            }
        }

        _linkHitRects.Clear();
        _nameHitRects.Clear();
        Point mouse = _lastMousePos;
        for (int i = 0; i < visible; i++)
        {
            int idx = firstIdx + i;
            if (idx >= _visualLines.Count) break;
            var (text, colorIndex, _) = _visualLines[idx];
            var baseColor = GameColors[colorIndex];
            var lineY = logRect.Y + i * LineH;
            var links = _enableHyperlinks && idx < _visualLineLinks.Count
                ? _visualLineLinks[idx]
                : null;
            var names = idx < _visualLineNames.Count ? _visualLineNames[idx] : null;
            var colors = idx < _visualLineColors.Count ? _visualLineColors[idx] : null;

            // Common fast path: no spans at all.
            if ((links == null || links.Count == 0)
                && (names == null || names.Count == 0)
                && (colors == null || colors.Count == 0))
            {
                sb.DrawString(font, text, new Vector2(logRect.X + 2, lineY), baseColor);
                continue;
            }

            // Merge link + name + color spans into one list sorted by Start. Assumes no overlap —
            // server-generated chat puts names at the front and any URLs in the body; HelpPanel's
            // color spans cover the command prefix only.
            var spans = new List<DrawSpan>();
            if (links != null) foreach (var l in links) spans.Add(DrawSpan.Link(l.StartCol, l.Length, l.Url));
            if (names != null) foreach (var n in names) spans.Add(DrawSpan.Name(n.StartCol, n.Length, n.Name, n.Access, n.ShowAsPk));
            if (colors != null) foreach (var c in colors) spans.Add(DrawSpan.Color(c.StartCol, c.Length, c.ColorIndex));
            spans.Sort((a, b) => a.Start.CompareTo(b.Start));

            float x = logRect.X + 2;
            int pos = 0;
            foreach (var span in spans)
            {
                if (span.Start > pos)
                {
                    string pre = text[pos..span.Start];
                    sb.DrawString(font, pre, new Vector2(x, lineY), baseColor);
                    x += font.MeasureString(pre).X;
                }
                if (span.Start < pos) { continue; } // overlap fallback — skip
                string segText = text[span.Start..span.End];
                float w = font.MeasureString(segText).X;
                var rect = new Rectangle((int)x, lineY, Math.Max(1, (int)w), LineH);
                bool hover = rect.Contains(mouse);

                Color segColor;
                switch (span.Kind)
                {
                    case DrawSpan.SpanKind.Link:
                        segColor = hover ? UiHelper.HyperlinkHoverColor : UiHelper.HyperlinkColor;
                        UiHelper.DrawFilledRect(sb,
                            new Rectangle(rect.X, rect.Y + LineH - 2, rect.Width, 1),
                            segColor);
                        _linkHitRects.Add((rect, span.Payload));
                        break;
                    case DrawSpan.SpanKind.Name:
                        segColor = GetColor(PlayerNameColor.For(span.ShowAsPk, span.Access));
                        if (hover)
                        {
                            UiHelper.DrawFilledRect(sb,
                                new Rectangle(rect.X, rect.Y + LineH - 2, rect.Width, 1),
                                segColor);
                        }

                        _nameHitRects.Add((rect, span.Payload));
                        break;
                    default: // SpanKind.Color — plain colored substring, no hover or hit-rect
                        segColor = GetColor(span.ColorIndex);
                        break;
                }
                sb.DrawString(font, segText, new Vector2(x, lineY), segColor);
                x += w;
                pos = span.End;
            }
            if (pos < text.Length)
                sb.DrawString(font, text[pos..], new Vector2(x, lineY), baseColor);
        }

        // Caret — blinking, only while focused
        if (_focused && _caretFlat >= 0 && (nowMs / 500) % 2 == 0)
        {
            for (int i = 0; i < visible; i++)
            {
                int idx = firstIdx + i;
                if (idx >= _visualLines.Count) break;
                var (lineText, _, _) = _visualLines[idx];
                int lineFlat = FlatIndex(idx, 0);
                if (_caretFlat >= lineFlat && _caretFlat <= lineFlat + lineText.Length)
                {
                    int col = _caretFlat - lineFlat;
                    float cx = logRect.X + 2 + (col > 0 ? font.MeasureString(lineText[..col]).X : 0f);
                    UiHelper.DrawFilledRect(sb,
                        new Rectangle((int)cx, logRect.Y + i * LineH, 1, LineH),
                        Color.White);
                    break;
                }
            }
        }

        // Scrollbar (drawn on top of text). The track is always visible so the area
        // reads as scrollable; the thumb appears only when the content overflows.
        var track = SbTrackRect();
        UiHelper.DrawFilledRect(sb, track, UiHelper.TextAreaSbTrackBg);
        UiHelper.DrawBorder(sb, track, UiHelper.TextAreaSbTrackBorder);
        if (_visualLines.Count > visible)
        {
            var thumb = SbThumbRect();
            UiHelper.DrawFilledRect(sb, thumb, UiHelper.TextAreaSbThumbBg);
            UiHelper.DrawBorder(sb, thumb, UiHelper.TextAreaSbThumbBorder);
        }
    }

    private static void OpenUrl(string url) => UiHelper.OpenUrl(url);
}

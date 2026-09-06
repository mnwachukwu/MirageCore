using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Shell.Input;

namespace Mirage.Client.Shell.Ui;

public sealed class ListBox
{
    public List<string> Items { get; } = new();
    public int SelectedIndex { get; set; } = -1;
    /// <summary>1-based-ish index of the row currently under the cursor, or -1 if none.
    /// Updated each frame in <see cref="Update"/>; consumers (panels that want to draw a
    /// hover tooltip) read it after Update.</summary>
    public int HoveredIndex { get; private set; } = -1;
    /// <summary>Optional custom row painter. When set, replaces the default single-string row draw so a
    /// consumer (e.g. <see cref="Table{T}"/>) can paint multi-column cells aligned to this list's own row
    /// layout. Args: (spriteBatch, font, rowIndex, rowRect). The list still owns the row background /
    /// selection highlight, hover, right-click, keyboard nav, and the scrollbar — one row engine, custom
    /// row contents. Row backgrounds still key off <see cref="Items"/>.Count, so a consumer must keep
    /// Items sized to the row count (the string contents go unused).</summary>
    public Action<SpriteBatch, SpriteFont, int, Rectangle>? RowRenderer { get; set; }
    /// <summary>When true (default), a hovered row whose text is truncated shows a full-text tooltip. Set
    /// false on lists that install their OWN richer hover tooltip for a row (e.g. the inventory/bank/shop/
    /// spell item + spell tooltips, which already reveal the full name) so the two don't fight over the
    /// shared Tooltip singleton.</summary>
    public bool ShowTruncationTooltip { get; set; } = true;
    private Rectangle _lastContentRect;

    private int _scrollOffset;
    /// <summary>Index of the first visible row. Read-only, and exposed only so tests can see that the list is
    /// not parked past the end of its own contents — the symptom of that is a blank list, which needs a
    /// SpriteBatch to observe any other way.</summary>
    internal int ScrollOffset => _scrollOffset;
    private bool _sbDragging;
    private int _sbDragStartY;
    private int _sbDragStartOffset;
    private Point _lastMouse;   // captured in Update so Draw can offer a truncation tooltip per row
    private readonly string _tooltipScope = UiHelper.NextTooltipScope("listbox");

    private const int RowHeight = 20;
    /// <summary>Row pixel height — exposed so a consumer (e.g. <see cref="Table{T}"/>) can size a header
    /// strip to match the body's rows without duplicating the magic number.</summary>
    public const int RowPixels = RowHeight;
    private const int SbWidth = 8;
    /// <summary>Width the vertical scrollbar reserves on the right — exposed so a consumer (e.g.
    /// <see cref="Table{T}"/>) can compute the column area that excludes it.</summary>
    public const int ScrollbarWidth = SbWidth;
    private static readonly Color ListBg = new(20, 20, 40);
    private static readonly Color SelectedRowBg = new(60, 60, 120);

    /// <summary>Return to the top with nothing selected. For a list whose CONTENTS are being replaced with an
    /// unrelated set — a different shop's stock, not the same stock re-filtered — where carrying the old
    /// position over would drop the reader into the middle of something they have not seen the start of, and
    /// leave a selection index armed against a row that now means something else.</summary>
    public void Reset()
    {
        _scrollOffset = 0;
        SelectedIndex = -1;
    }

    public void Update(InputState input, Rectangle bounds, bool keyboardActive = true)
    {
        _lastMouse = input.MousePosition;
        int visibleRows = bounds.Height / RowHeight;
        int maxOffset = Math.Max(0, Items.Count - visibleRows);

        // Whoever owns this list refills Items directly, and nothing tells the control that happened. When the
        // new contents are shorter than the old offset skipped — a smaller shop, a narrowed filter — every
        // remaining row sits above the viewport and the list would draw EMPTY. Clamped here rather than only
        // in the scroll branches below, which would leave it that way until the user happened to scroll.
        _scrollOffset = Math.Clamp(_scrollOffset, 0, maxOffset);

        // Scroll wheel
        int wheel = input.ScrollWheelDelta();
        if (wheel != 0 && bounds.Contains(input.MousePosition))
            _scrollOffset = Math.Clamp(_scrollOffset - wheel / 120, 0, maxOffset);

        // Scrollbar thumb drag
        var sbThumb = SbThumbRect(bounds, visibleRows);
        var sbTrack = SbTrackRect(bounds);

        // Scrollbar thumb drag captures the pointer: while captured no other widget sees the mouse, and
        // the release that ends the drag is swallowed for everyone — no phantom click on a row/link/button.
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
                if (maxOffset > 0)
                {
                    int trackH = sbTrack.Height - sbThumb.Height;
                    if (trackH > 0)
                    {
                        int dy = input.MousePosition.Y - _sbDragStartY;
                        _scrollOffset = Math.Clamp(_sbDragStartOffset + dy * maxOffset / trackH, 0, maxOffset);
                    }
                }
            }
            else
            {
                _sbDragging = false;   // capture auto-releases the frame after button-up
            }
        }

        // Click on track (not thumb) → page jump
        if (input.IsClickIn(sbTrack) && !sbThumb.Contains(input.MousePosition))
        {
            _scrollOffset = input.MousePosition.Y < sbThumb.Y
                ? Math.Clamp(_scrollOffset - visibleRows, 0, maxOffset)
                : Math.Clamp(_scrollOffset + visibleRows, 0, maxOffset);
            input.ConsumeMouseClick();
        }

        // Click in content area → select row. Reject clicks/hovers that fall in the bottom strip
        // of pixels that *aren't* a drawn row — bounds.Height isn't always an integer multiple of
        // RowHeight, and bounds may also extend past the last item.
        var contentRect = ContentRect(bounds);
        _lastContentRect = contentRect;
        if (input.IsClickIn(contentRect))
        {
            int rowOnScreen = (input.MousePosition.Y - contentRect.Y) / RowHeight;
            int row = rowOnScreen + _scrollOffset;
            if (rowOnScreen >= 0 && rowOnScreen < visibleRows && row >= 0 && row < Items.Count)
                SelectedIndex = row;
        }

        // Hover tracking — only fires for fully-drawn rows so a tooltip never shows for content
        // the user can't see (off-screen below the last visible row, or in the unused tail strip
        // when bounds.Height % RowHeight != 0).
        HoveredIndex = -1;
        if (contentRect.Contains(input.MousePosition))
        {
            int rowOnScreen = (input.MousePosition.Y - contentRect.Y) / RowHeight;
            int row = rowOnScreen + _scrollOffset;
            if (rowOnScreen >= 0 && rowOnScreen < visibleRows && row >= 0 && row < Items.Count)
                HoveredIndex = row;
        }

        // Keyboard navigation — only when this list's panel has keyboard focus.
        if (keyboardActive)
        {
            if (input.IsKeyPressed(Keys.Up) && SelectedIndex > 0)
            {
                SelectedIndex--;
                if (SelectedIndex < _scrollOffset)
                    _scrollOffset = SelectedIndex;
            }
            if (input.IsKeyPressed(Keys.Down) && SelectedIndex < Items.Count - 1)
            {
                SelectedIndex++;
                if (SelectedIndex >= _scrollOffset + visibleRows)
                    _scrollOffset = SelectedIndex - visibleRows + 1;
            }
        }
    }

    public void Draw(SpriteBatch sb, SpriteFont font, Rectangle bounds)
    {
        int visibleRows = bounds.Height / RowHeight;
        var contentRect = ContentRect(bounds);
        float maxTextW = contentRect.Width - 8; // 4px padding each side

        UiHelper.DrawFilledRect(sb, bounds, ListBg);
        UiHelper.DrawBorder(sb, bounds, Color.Gray);

        for (int i = 0; i < visibleRows; i++)
        {
            int idx = i + _scrollOffset;
            if (idx >= Items.Count) break;

            var rowRect = new Rectangle(contentRect.X, contentRect.Y + i * RowHeight, contentRect.Width, RowHeight);
            if (idx == SelectedIndex)
                UiHelper.DrawFilledRect(sb, rowRect, SelectedRowBg);

            if (RowRenderer is not null)
            {
                RowRenderer(sb, font, idx, rowRect);
                continue;
            }

            string display = UiHelper.FitText(font, Items[idx], maxTextW);
            sb.DrawString(font, display, new Vector2(contentRect.X + 4, contentRect.Y + i * RowHeight + 2), Color.White);
            if (ShowTruncationTooltip)
            {
                UiHelper.LabelTooltip(font, Items[idx],
                    new Rectangle(contentRect.X + 4, contentRect.Y + i * RowHeight, (int)maxTextW, RowHeight),
                    _lastMouse, _tooltipScope, idx);
            }
        }

        DrawScrollbar(sb, bounds, visibleRows);
    }

    private static Rectangle ContentRect(Rectangle bounds)
        => new(bounds.X, bounds.Y, bounds.Width - SbWidth, bounds.Height);

    /// <summary>If a right-click landed over a content row this frame, returns that 1-based row
    /// index (so callers can index 1..MaxInv/MaxBankSlots directly) and consumes the click so it
    /// doesn't bleed through to the world. Returns 0 otherwise. Selection is intentionally NOT
    /// mutated — right-click on a slot opens its context menu without disturbing which slot is
    /// currently selected for the panel's buttons. Matches the right-click-on-player convention.</summary>
    public int ConsumeRightClickedRow(InputState input)
    {
        if (!input.IsRightMouseClicked()) return 0;
        if (HoveredIndex < 0) return 0;
        input.ConsumeRightMouseClick();
        return HoveredIndex + 1;
    }

    /// <summary>Screen rect of the hovered row, or <see cref="Rectangle.Empty"/> if no row is
    /// hovered. Used by panels to anchor a tooltip beside the row the user is pointing at.</summary>
    public Rectangle HoveredRowRect()
    {
        if (HoveredIndex < 0) return Rectangle.Empty;
        int rowOnScreen = HoveredIndex - _scrollOffset;
        return new Rectangle(_lastContentRect.X, _lastContentRect.Y + rowOnScreen * RowHeight,
            _lastContentRect.Width, RowHeight);
    }

    private static Rectangle SbTrackRect(Rectangle bounds)
        => new(bounds.Right - SbWidth, bounds.Y, SbWidth, bounds.Height);

    private Rectangle SbThumbRect(Rectangle bounds, int visibleRows)
    {
        var track = SbTrackRect(bounds);
        int total = Items.Count;
        if (total <= visibleRows) return track;

        int thumbH = Math.Max(16, track.Height * visibleRows / total);
        int maxOff = total - visibleRows;
        int thumbY = maxOff > 0
            ? track.Y + (track.Height - thumbH) * _scrollOffset / maxOff
            : track.Y;
        return new Rectangle(track.X, thumbY, track.Width, thumbH);
    }

    private void DrawScrollbar(SpriteBatch sb, Rectangle bounds, int visibleRows)
    {
        var track = SbTrackRect(bounds);
        UiHelper.DrawFilledRect(sb, track, UiHelper.ListScrollTrackBg);
        var thumb = SbThumbRect(bounds, visibleRows);
        UiHelper.DrawFilledRect(sb, thumb, UiHelper.ListScrollThumbBg);
        UiHelper.DrawBorder(sb, thumb, UiHelper.ListScrollThumbBorder);
    }
}

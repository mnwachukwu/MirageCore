using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Shell.Input;

namespace Mirage.Client.Shell.Ui;

public sealed class DropDown
{
    public List<string> Items { get; } = new();
    public int SelectedIndex { get; set; } = -1;

    /// <summary>When true the popup opens ABOVE the header instead of below — for a control docked
    /// near the bottom of the screen (the chat channel selector) where a downward list would clip.</summary>
    public bool OpenUp { get; set; }

    private bool _open;
    private int _scrollOffset;
    private bool _sbDragging;
    private int _sbDragStartY;
    private int _sbDragStartOffset;

    private const int RowH = 18;
    private const int MaxVisible = 8;
    private const int SbWidth = 8;
    private readonly string _tooltipScope = UiHelper.NextTooltipScope("dropdown");
    private static readonly Color ListBg = new(25, 20, 55);
    private static readonly Color SelectedRowBg = new(60, 80, 160);
    private static readonly Color HoveredRowBg = new(45, 50, 100);

    public string? SelectedItem =>
        SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;

    public void Update(InputState input, Rectangle bounds)
    {
        var mouse = input.MousePosition;

        // Modal-ish while open: suppress hover on whatever sits beneath the popup.
        if (_open && ListRect(bounds).Contains(mouse))
            input.ConsumeMouseHover();

        // Header toggles open/closed.
        if (input.IsClickIn(bounds))
        {
            _open = !_open;
            _scrollOffset = SelectedIndex >= 0
                ? Math.Clamp(SelectedIndex - MaxVisible / 2, 0, Math.Max(0, Items.Count - MaxVisible))
                : 0;
            input.ConsumeMouseClick();
            return;
        }

        if (!_open) return;

        var list = ListRect(bounds);
        var contentRect = new Rectangle(list.X, list.Y, list.Width - SbWidth, list.Height);

        // Click on a row selects it and closes.
        if (input.IsClickIn(contentRect))
        {
            int idx = (mouse.Y - contentRect.Y) / RowH + _scrollOffset;
            if (idx >= 0 && idx < Items.Count)
                SelectedIndex = idx;
            _open = false;
            input.ConsumeMouseClick();
            return;
        }

        // Scroll wheel over the popup.
        int wheel = input.ScrollWheelDelta();
        if (wheel != 0 && list.Contains(mouse))
        {
            int maxOff = Math.Max(0, Items.Count - MaxVisible);
            _scrollOffset = Math.Clamp(_scrollOffset - wheel / 120, 0, maxOff);
        }

        // Scrollbar thumb drag — captures the pointer so its release can't select a row or leak out.
        var sbTrack = new Rectangle(list.Right - SbWidth, list.Y, SbWidth, list.Height);
        var sbThumb = SbThumbRect(list);
        if (!_sbDragging && input.IsPressIn(sbThumb))
        {
            _sbDragging = true;
            _sbDragStartY = mouse.Y;
            _sbDragStartOffset = _scrollOffset;
            input.CaptureMouse(this);
        }
        if (_sbDragging)
        {
            if (input.IsMouseDown())
            {
                int maxOff = Math.Max(0, Items.Count - MaxVisible);
                int trackH = sbTrack.Height - sbThumb.Height;
                if (trackH > 0 && maxOff > 0)
                {
                    int dy = mouse.Y - _sbDragStartY;
                    _scrollOffset = Math.Clamp(_sbDragStartOffset + dy * maxOff / trackH, 0, maxOff);
                }
            }
            else
            {
                _sbDragging = false;   // capture auto-releases the frame after button-up
            }
            return;
        }

        // A press anywhere on the open popup must not reach whatever sits beneath it. A bottom-docked
        // list (the chat channel selector) opens UPWARD over the chat log's text area; without this the
        // press starts the log's selection drag, which CAPTURES the pointer — and a captured pointer
        // suppresses the release's click edge, so the row-select above (resolved on release) would
        // silently never fire and the list would look unclickable. Header and scrollbar presses already
        // returned above; this covers presses on the rows. Harmless when nothing sits beneath.
        if (list.Contains(mouse))
            input.ConsumeMouseDown();

        // A completed click that landed neither on the header nor a row dismisses the popup.
        if (input.IsMouseClicked())
        {
            _open = false;
            input.ConsumeMouseClick();
        }
    }

    // Draw just the closed header — call in normal draw order.
    public void DrawHeader(SpriteBatch sb, SpriteFont font, Rectangle bounds, InputState input)
    {
        var mouse = input.MousePosition;
        bool hovered = bounds.Contains(mouse);
        UiHelper.DrawFilledRect(sb, bounds, hovered ? UiHelper.ButtonHoverBg : UiHelper.ButtonNormalBg);
        UiHelper.DrawBorder(sb, bounds, UiHelper.UiControlBorder);

        string label = UiHelper.FitText(font, SelectedItem ?? "(select)", bounds.Width - 8 - 13); // 13px for arrow
        sb.DrawString(font, label, new Vector2(bounds.X + 4, bounds.Y + 4), Color.White);
        UiHelper.LabelTooltip(font, SelectedItem ?? "", new Rectangle(bounds.X + 4, bounds.Y, bounds.Width - 8 - 13, bounds.Height),
            mouse, _tooltipScope, -1);

        DrawTriangle(sb, bounds.Right - 13, bounds.Y + bounds.Height / 2 - 2, _open);
    }

    // Draw the open popup list — call last so it renders on top of other controls.
    public void DrawPopup(SpriteBatch sb, SpriteFont font, Rectangle bounds, InputState input)
    {
        if (!_open || Items.Count == 0) return;

        var mouse = input.MousePosition;
        var list = ListRect(bounds);
        int visible = Math.Min(Items.Count, MaxVisible);

        UiHelper.DrawFilledRect(sb, list, ListBg);
        UiHelper.DrawBorder(sb, list, UiHelper.UiControlBorder);

        var contentRect = new Rectangle(list.X, list.Y, list.Width - SbWidth, list.Height);

        for (int i = 0; i < visible; i++)
        {
            int idx = i + _scrollOffset;
            if (idx >= Items.Count) break;
            var row = new Rectangle(contentRect.X, contentRect.Y + i * RowH, contentRect.Width, RowH);
            if (idx == SelectedIndex)
                UiHelper.DrawFilledRect(sb, row, SelectedRowBg);
            else if (row.Contains(mouse))
                UiHelper.DrawFilledRect(sb, row, HoveredRowBg);
            string display = UiHelper.FitText(font, Items[idx], contentRect.Width - 8);
            sb.DrawString(font, display, new Vector2(row.X + 4, row.Y + 1), Color.White);
            UiHelper.LabelTooltip(font, Items[idx], new Rectangle(row.X + 4, row.Y, contentRect.Width - 8, RowH),
                mouse, _tooltipScope, idx);
        }

        // Scrollbar
        var sbTrack = new Rectangle(list.Right - SbWidth, list.Y, SbWidth, list.Height);
        UiHelper.DrawFilledRect(sb, sbTrack, UiHelper.ListScrollTrackBg);
        var sbThumb = SbThumbRect(list);
        UiHelper.DrawFilledRect(sb, sbThumb, UiHelper.ListScrollThumbBg);
        UiHelper.DrawBorder(sb, sbThumb, UiHelper.ListScrollThumbBorder);
    }

    private Rectangle SbThumbRect(Rectangle list)
    {
        int total = Items.Count;
        int visible = Math.Min(total, MaxVisible);
        if (total <= MaxVisible)
            return new Rectangle(list.Right - SbWidth, list.Y, SbWidth, list.Height);

        int thumbH = Math.Max(16, list.Height * visible / total);
        int maxOff = total - MaxVisible;
        int thumbY = maxOff > 0
            ? list.Y + (list.Height - thumbH) * _scrollOffset / maxOff
            : list.Y;
        return new Rectangle(list.Right - SbWidth, thumbY, SbWidth, thumbH);
    }

    private static void DrawTriangle(SpriteBatch sb, int x, int y, bool pointUp)
    {
        for (int r = 0; r < 5; r++)
        {
            int w = pointUp ? r * 2 + 1 : (4 - r) * 2 + 1;
            int ox = pointUp ? 4 - r : r;
            UiHelper.DrawFilledRect(sb, new Rectangle(x + ox, y + r, w, 1), Color.LightGray);
        }
    }

    private Rectangle ListRect(Rectangle bounds)
    {
        int visible = Math.Min(Items.Count, MaxVisible);
        int h = visible * RowH + 1;
        int y = OpenUp ? bounds.Y - h : bounds.Bottom;
        return new(bounds.X, y, bounds.Width, h);
    }
}

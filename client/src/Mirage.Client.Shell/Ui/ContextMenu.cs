using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Shell.Input;

namespace Mirage.Client.Shell.Ui;

/// <summary>
/// Floating popup menu used by right-click on player names (chat) and player sprites (world).
/// Captures every mouse-button event while open so clicks never bleed through to the world
/// or other panels. Esc closes; outside-click closes and is consumed.
/// </summary>
public sealed class ContextMenu
{
    public readonly struct Item
    {
        public readonly string Label;
        public readonly Action? OnClick;
        public readonly IReadOnlyList<Item>? SubItems; // non-null = parent of a hover-reveal submenu
        public readonly bool Enabled;
        public readonly Func<bool>? EnabledFn;   // optional LIVE per-frame enabled state (e.g. an in-range check)
        public bool IsEnabled => EnabledFn?.Invoke() ?? Enabled;

        public Item(string label, Action? onClick)
        {
            Label = label;
            OnClick = onClick;
            SubItems = null;
            Enabled = true;
            EnabledFn = null;
        }
        public Item(string label, Action? onClick, bool enabled)
        {
            Label = label;
            OnClick = onClick;
            SubItems = null;
            Enabled = enabled;
            EnabledFn = null;
        }
        public Item(string label, Action? onClick, Func<bool> enabledFn)
        {
            Label = label;
            OnClick = onClick;
            SubItems = null;
            Enabled = true;
            EnabledFn = enabledFn;
        }
        public Item(string label, IReadOnlyList<Item> subItems)
        {
            Label = label;
            OnClick = null;
            SubItems = subItems;
            Enabled = true;
            EnabledFn = null;
        }
    }

    public bool IsOpen { get; private set; }
    public string TargetName { get; private set; } = "";

    private readonly List<Item> _items = new();
    private Rectangle _bounds;
    private int _hoverIndex = -1;            // currently hovered item row in _items
    private int _selectedParentIndex = -1;   // index of the parent row whose submenu is currently visible
    private Rectangle _subBounds;
    private int _subHoverIndex = -1;

    // ── Layout constants ─────────────────────────────────────────────────────
    // Per-item row height. Sized to accommodate the default game font line height
    // plus a single-pixel top/bottom breathing margin inside the row.
    private const int RowH = 18;
    // Horizontal text padding inside each row.
    private const int PadX = 8;
    // Minimum panel width, applied when every item's measured width falls below it.
    private const int MinWidth = 120;
    // Vertical padding above the first row and below the last row.
    private const int PadTop = 3;
    private const int PadBottom = 3;
    // Panel-border inset for rows: rows are drawn one pixel inside each vertical edge so
    // the hover-highlight fill never overpaints the border.
    private const int RowInsetX = 1;
    private const int RowInsetTotalX = RowInsetX * 2;
    // Submenu arrow indicator: width allowance added to ComputeWidth so the arrow has room,
    // and the offset from the row's right edge where the glyph is drawn.
    private const string SubmenuMarker = ">";
    private const int SubmenuMarkerWidth = 14;
    private const int SubmenuMarkerRightOffset = 12;
    // Y-offset applied to text inside the row so the glyph baseline sits visually centered.
    private const int RowTextYOffset = 1;

    public void Open(Point at, string targetName, IReadOnlyList<Item> items, Rectangle screen, SpriteFont font)
    {
        _items.Clear();
        _items.AddRange(items);
        TargetName = targetName;
        IsOpen = true;
        _hoverIndex = -1;
        _selectedParentIndex = -1;
        _subHoverIndex = -1;

        int width = ComputeWidth(font, _items);
        int height = _items.Count * RowH + PadTop + PadBottom;
        int x = at.X;
        int y = at.Y;
        if (x + width > screen.Right) x = screen.Right - width;
        if (y + height > screen.Bottom) y = screen.Bottom - height;
        if (x < screen.Left) x = screen.Left;
        if (y < screen.Top) y = screen.Top;
        _bounds = new Rectangle(x, y, width, height);
    }

    public void Close()
    {
        IsOpen = false;
        _items.Clear();
        TargetName = "";
        _hoverIndex = -1;
        _selectedParentIndex = -1;
        _subHoverIndex = -1;
    }

    public bool ContainsMouse(Point p) =>
        IsOpen && (_bounds.Contains(p) || (_selectedParentIndex >= 0 && _subBounds.Contains(p)));

    public void Update(InputState input, Rectangle screen, SpriteFont font)
    {
        if (!IsOpen) return;

        // Esc closes.
        if (input.IsKeyPressed(Keys.Escape))
        {
            Close();
            return;
        }

        Point m = input.MousePosition;

        // Hit-test main rows.
        _hoverIndex = -1;
        if (_bounds.Contains(m))
        {
            int row = (m.Y - _bounds.Y - PadTop) / RowH;
            if (row >= 0 && row < _items.Count) _hoverIndex = row;
        }

        // Submenu visibility: parent stays open while cursor is on the parent row OR over the sub panel.
        if (_selectedParentIndex >= 0)
        {
            bool stillOverParent = _hoverIndex == _selectedParentIndex;
            bool stillOverSub = _subBounds.Contains(m);
            if (!stillOverParent && !stillOverSub) _selectedParentIndex = -1;
        }

        // Open submenu when hovering a parent item.
        if (_hoverIndex >= 0 && _items[_hoverIndex].SubItems is { } subs && _selectedParentIndex != _hoverIndex)
        {
            _selectedParentIndex = _hoverIndex;
            int subW = ComputeWidth(font, subs);
            int subH = subs.Count * RowH + PadTop + PadBottom;
            int sx = _bounds.Right;
            int sy = _bounds.Y + _hoverIndex * RowH;
            if (sx + subW > screen.Right) sx = _bounds.X - subW;
            if (sy + subH > screen.Bottom) sy = screen.Bottom - subH;
            _subBounds = new Rectangle(sx, sy, subW, subH);
        }

        // Hit-test submenu rows.
        _subHoverIndex = -1;
        if (_selectedParentIndex >= 0 && _subBounds.Contains(m))
        {
            int row = (m.Y - _subBounds.Y - PadTop) / RowH;
            if (row >= 0 && row < _items[_selectedParentIndex].SubItems!.Count) _subHoverIndex = row;
        }

        // Click bleed-through prevention: while the menu is open, claim every mouse-button event.
        bool insideAny = _bounds.Contains(m) || (_selectedParentIndex >= 0 && _subBounds.Contains(m));
        if (input.IsMouseClicked() || input.IsRightMouseClicked() || input.IsMouseJustPressed() || input.IsRightMouseJustPressed())
        {
            if (input.IsMouseClicked())
            {
                if (_subHoverIndex >= 0)
                {
                    var sub = _items[_selectedParentIndex].SubItems![_subHoverIndex];
                    if (sub.IsEnabled)
                    {
                        var cb = sub.OnClick;
                        Close();
                        cb?.Invoke();
                    }
                }
                else if (_hoverIndex >= 0 && _items[_hoverIndex].SubItems is null)
                {
                    var leaf = _items[_hoverIndex];
                    if (leaf.IsEnabled)
                    {
                        var cb = leaf.OnClick;
                        Close();
                        cb?.Invoke();
                    }
                }
                else if (!insideAny)
                {
                    Close();
                }
            }
            else if (!insideAny)
            {
                Close();
            }
            input.ConsumeMouseClick();
            input.ConsumeMouseDown();
            input.ConsumeRightMouseClick();
            input.ConsumeRightMouseDown();
        }

        // Also block hover on anything beneath.
        if (insideAny) input.ConsumeMouseHover();
    }

    public void Draw(SpriteBatch sb, SpriteFont font)
    {
        if (!IsOpen) return;

        DrawPanel(sb, font, _bounds, _items, _hoverIndex);

        if (_selectedParentIndex >= 0)
        {
            var subs = _items[_selectedParentIndex].SubItems!;
            DrawPanel(sb, font, _subBounds, subs, _subHoverIndex);
        }
    }

    private static void DrawPanel(SpriteBatch sb, SpriteFont font, Rectangle bounds, IReadOnlyList<Item> items, int hoverIndex)
    {
        UiHelper.DrawFilledRect(sb, bounds, UiHelper.PopupBg);
        UiHelper.DrawBorder(sb, bounds, UiHelper.UiControlBorder);

        for (int i = 0; i < items.Count; i++)
        {
            var rowRect = new Rectangle(
                bounds.X + RowInsetX,
                bounds.Y + PadTop + i * RowH,
                bounds.Width - RowInsetTotalX,
                RowH);
            if (i == hoverIndex && items[i].IsEnabled)
                UiHelper.DrawFilledRect(sb, rowRect, UiHelper.ButtonHoverBg);
            Color labelColor = items[i].IsEnabled ? Color.White : Color.DarkGray;
            sb.DrawString(font, items[i].Label, new Vector2(rowRect.X + PadX, rowRect.Y + RowTextYOffset), labelColor);
            if (items[i].SubItems is not null)
            {
                sb.DrawString(font, SubmenuMarker,
                    new Vector2(rowRect.Right - SubmenuMarkerRightOffset, rowRect.Y + RowTextYOffset),
                    items[i].Enabled ? Color.LightGray : Color.DarkGray);
            }
        }
    }

    private static int ComputeWidth(SpriteFont font, IReadOnlyList<Item> items)
    {
        int max = MinWidth;
        for (int i = 0; i < items.Count; i++)
        {
            int markerAllowance = items[i].SubItems is not null ? SubmenuMarkerWidth : 0;
            int w = (int)font.MeasureString(items[i].Label).X + PadX * 2 + markerAllowance;
            if (w > max) max = w;
        }
        return max;
    }
}

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Shell.Input;
using System.Linq;

namespace Mirage.Client.Shell.Ui;

/// <summary>One declared column of a <see cref="Table{T}"/>: how to pull a cell's sort key and display text
/// out of a row of type <typeparamref name="T"/>, plus its header and initial width. The sort key doubles
/// as the display text when no separate <see cref="Text"/> selector is given (so an int/string field is a
/// one-liner and sorts correctly for free).</summary>
public sealed class TableColumn<T>
{
    public Func<string> Header { get; }
    public int Width { get; }
    public int MinWidth { get; }
    public Func<T, string> Text { get; }
    public Func<T, IComparable?> SortKey { get; }

    public TableColumn(Func<string> header, Func<T, IComparable?> sortKey, Func<T, string>? text, int width, int minWidth)
    {
        Header = header;
        SortKey = sortKey;
        Text = text ?? (t => sortKey(t)?.ToString() ?? "");
        MinWidth = Math.Max(1, minWidth);
        Width = Math.Max(MinWidth, width);
    }
}

/// <summary>The row-type-independent slice of a <see cref="Table{T}"/> that a host needs to save and restore a
/// table's column layout. Order, widths and sort are all plain ints, so this stays non-generic — letting a host
/// keep many differently-typed tables in one collection and persist them uniformly.</summary>
public interface IColumnLayoutTable
{
    /// <summary>True for the frame after the user resized, reordered or sorted a column.</summary>
    bool LayoutChanged { get; }
    /// <summary>Whether the user can drag columns into a new order. Hosts persist the order only when true.</summary>
    bool AllowReorder { get; }
    /// <summary>Current display order (display position → logical column index).</summary>
    IReadOnlyList<int> ColumnOrder { get; }
    /// <summary>Current per-logical-column widths.</summary>
    IReadOnlyList<int> ColumnWidths { get; }
    /// <summary>Current sort column (logical index; -1 = none) and direction.</summary>
    int SortColumn { get; }
    bool SortAscending { get; }
    /// <summary>Restore a saved layout (shape-checked; a stale or foreign config is safely ignored).</summary>
    void ApplyColumnLayout(IReadOnlyList<int> order, IReadOnlyList<int> widths, int sortColumn, bool sortAscending);
    /// <summary>Discard the user's column customization and go back to what the host declared — the
    /// Options panel's Reset Panels button.</summary>
    void ResetColumnLayout();
}

/// <summary>A data-bound, multi-column table built on <see cref="ListBox"/>. Declare columns from a row's
/// properties with the fluent <see cref="Column(string, Func{T, IComparable}, Func{T, string}, int, int)"/>,
/// optionally give a <see cref="WithRowKey"/> for selection that survives a list swap, then feed it any
/// collection via <see cref="Items"/> — it renders, and sorting / resizing / reordering come for free.
///
/// The list owns the body (row scroll, selection highlight, hover, right-click, keyboard nav, scrollbar);
/// this view adds the header strip — click a header to sort (ASCII ^ / v), drag its right-edge divider to
/// resize, or (on tables that opt in via <see cref="Table{T}.AllowReorder"/>) drag a header cell sideways to
/// reorder. Column widths / display order / sort state live in the
/// pure, unit-tested <see cref="TableModel"/> (built once from the declared columns); the row VALUES come
/// straight from the column selectors applied to <see cref="Items"/>, so no per-row bookkeeping is needed.
/// Selection is exposed in <typeparamref name="T"/> terms (<see cref="SelectedItem"/>).</summary>
public sealed class Table<T> : IColumnLayoutTable
{
    private readonly List<TableColumn<T>> _cols = new();
    private TableModel? _model;
    private IReadOnlyList<T> _items = Array.Empty<T>();
    private Func<T, object>? _rowKey;
    private Func<T, Color>? _rowColor;
    private readonly ListBox _body = new();
    private int[] _rowOrder = Array.Empty<int>();   // display position → source index into Items
    private int _selectedSource = -1;               // selected row as a source index into Items (-1 none)
    private bool _dirty = true;
    // The host's declared default sort, captured from its first SortBy call (every panel makes exactly
    // one, at construction). ResetColumnLayout restores it, so resetting returns the table to the sort
    // the panel shipped with rather than to unsorted. -1 = the host never declared one.
    private int _defaultSortColumn = -1;
    private bool _defaultSortAscending = true;

    // Header drag: at most one of resize / reorder is live at a time.
    private enum Drag { None, Resize, Reorder }
    private Drag _drag = Drag.None;
    private int _dragLogical = -1;      // Resize: the column being widened (logical index)
    private int _dragFromDisplay = -1;  // Reorder: the column's display position at press
    private int _dragStartMouseX;
    private int _dragStartWidth;
    private int _dragCurrentX;          // Reorder: live cursor x for the drag ghost
    private bool _dragMoved;            // Reorder passed the threshold — else the release is a plain sort click

    // Horizontal scroll (only when the columns overflow the width): a pixel offset applied to the header +
    // cells, with a bottom scrollbar; the whole table is scissor-clipped to its bounds so nothing bleeds.
    private int _hScroll;
    private bool _hDragging;
    private int _hDragStartX;
    private int _hDragStartScroll;
    private const int HScrollHeight = 8;

    // The frame's derived rects + overflow state, so Update and Draw agree on the layout.
    private readonly record struct Metrics(Rectangle Header, Rectangle Body, Rectangle HBar, bool HActive, int MaxHScroll, int ColWindowW);

    // Truncation tooltips: the last-seen mouse (from Update) + font (cached from Draw) let a hovered cell
    // whose text is clipped reveal the full text via the shared Tooltip. Unique scope per table instance.
    private Point _lastMouse;
    private SpriteFont? _lastFont;
    private readonly string _tooltipScope = UiHelper.NextTooltipScope("table");

    private static readonly int HeaderHeight = ListBox.RowPixels;
    private const int DividerGrab = 4;      // px on either side of a divider that grabs a resize
    private const int HScrollWheelStep = 40;   // px scrolled horizontally per Shift+wheel notch
    private const int ReorderThreshold = 5; // px of travel before a header press becomes a reorder (vs. a sort click)
    private const int CellPadX = 4;
    private const int RowTextPadY = 2;

    private static readonly Color HeaderBg = new(30, 30, 60);
    private static readonly Color HeaderBorder = Color.Gray;
    private static readonly Color HeaderText = Color.White;
    private static readonly Color SortArrowColor = new(120, 140, 255);
    private static readonly Color ReorderGhostBg = new(60, 60, 120, 200);
    private const int ReorderGhostHalfW = 30;

    public Table() => _body.RowRenderer = DrawRow;

    // ── Fluent declaration ─────────────────────────────────────────────────────
    /// <summary>Declare a column. <paramref name="value"/> is the cell's sort key (an int/string/long/etc.
    /// sorts naturally); <paramref name="text"/> is the optional display formatter, defaulting to the sort
    /// key's ToString(). Columns render in declaration order (drag reorders at runtime). Fluent.</summary>
    public Table<T> Column(Func<string> header, Func<T, IComparable?> value, Func<T, string>? text = null,
        int width = TableColumn.DefaultWidth, int minWidth = TableColumn.DefaultMinWidth)
    {
        _cols.Add(new TableColumn<T>(header, value, text, width, minWidth));
        _model = null;   // rebuilt from the columns on next use
        return this;
    }

    /// <summary>Column with a static (non-localized) header.</summary>
    public Table<T> Column(string header, Func<T, IComparable?> value, Func<T, string>? text = null,
        int width = TableColumn.DefaultWidth, int minWidth = TableColumn.DefaultMinWidth)
        => Column(() => header, value, text, width, minWidth);

    /// <summary>Identify a row by a stable key so the selection follows the same logical row across an
    /// <see cref="Items"/> swap (e.g. a server push that rebuilds the list). Without it, selection tracks by
    /// index and clears if that index falls out of range. Fluent.</summary>
    public Table<T> WithRowKey(Func<T, object> key)
    {
        _rowKey = key;
        return this;
    }

    /// <summary>Tint a row's text by a per-row rule (e.g. gray out an "in transit" mail row). Null (default)
    /// paints every row white. Fluent.</summary>
    public Table<T> WithRowColor(Func<T, Color> color)
    {
        _rowColor = color;
        return this;
    }

    // ── Data + selection ───────────────────────────────────────────────────────
    /// <summary>The rows to render. Assigning a new collection re-sorts on the next Update/Draw; the
    /// selection is preserved by row key (if set) or by index.</summary>
    public IReadOnlyList<T> Items
    {
        get => _items;
        set
        {
            var next = value ?? Array.Empty<T>();
            // With a row key, follow the selected row across the swap (cleared if it's gone). WITHOUT a key we
            // can't tell whether the same logical row is still there, so clear the selection rather than risk
            // highlighting a different row that happens to land on the old index.
            _selectedSource = _rowKey is not null && _selectedSource >= 0 && _selectedSource < _items.Count
                ? IndexOfKey(next, _rowKey(_items[_selectedSource]))
                : -1;
            _items = next;
            _dirty = true;
        }
    }

    /// <summary>The pure column/sort/order model (built once from the declared columns). Exposed for
    /// programmatic sorting or inspection; the header UI drives it too.</summary>
    public TableModel Model => _model ??= BuildModel();

    /// <summary>True for the frame after the user resized or reordered a column — a host reads it to persist
    /// the new column layout (mirrors a panel's LayoutChanged).</summary>
    public bool LayoutChanged { get; private set; }

    /// <summary>When false, dragging a header sideways does nothing (it stays a sort click) so columns can be
    /// sorted + resized but never reordered. Default false (fixed order) — hosts opt in with AllowReorder = true.</summary>
    public bool AllowReorder { get; set; } = false;

    /// <inheritdoc/>
    public IReadOnlyList<int> ColumnOrder => Model.Order;
    /// <inheritdoc/>
    public IReadOnlyList<int> ColumnWidths => Model.Columns.Select(c => c.Width).ToList();
    /// <inheritdoc/>
    public int SortColumn => Model.SortColumn;
    /// <inheritdoc/>
    public bool SortAscending => Model.SortAscending;

    /// <summary>Restore a persisted column layout — display order, per-logical widths, and the sort
    /// (column + direction). Shape-checked, so a stale/foreign config is safely ignored. A negative
    /// <paramref name="sortColumn"/> means "no saved sort" and leaves the current (default) sort intact.
    /// See <see cref="TableModel.ApplyLayout"/> / <see cref="TableModel.SetSort"/>.</summary>
    public void ApplyColumnLayout(IReadOnlyList<int> order, IReadOnlyList<int> widths, int sortColumn, bool sortAscending)
    {
        Model.ApplyLayout(order, widths);
        if (sortColumn >= 0) Model.SetSort(sortColumn, sortAscending);
        _dirty = true;
    }

    /// <summary>Throw away the player's column customization — display order, widths and sort — and go back
    /// to what the panel declared, re-applying its <see cref="SortBy"/> default so the reset table is sorted
    /// the way a fresh character's would be rather than left unsorted. The inverse of
    /// <see cref="ApplyColumnLayout"/>; the host persists the result the same way it persists a drag.</summary>
    public void ResetColumnLayout()
    {
        Model.ResetLayout();
        if (_defaultSortColumn >= 0) Model.SetSort(_defaultSortColumn, _defaultSortAscending);
        _dirty = true;
    }

    /// <summary>Selected row as an <see cref="Items"/> index, or -1. Setting clamps to range.</summary>
    public int SelectedIndex
    {
        get => _selectedSource;
        set => _selectedSource = value >= 0 && value < _items.Count ? value : -1;
    }

    /// <summary>The selected row, or default(T) if none.</summary>
    public T? SelectedItem => _selectedSource >= 0 && _selectedSource < _items.Count ? _items[_selectedSource] : default;

    /// <summary>The row under the cursor, or default(T) if none.</summary>
    public T? HoveredItem
    {
        get
        {
            int d = _body.HoveredIndex;
            return d >= 0 && d < _rowOrder.Length ? _items[_rowOrder[d]] : default;
        }
    }

    public void ClearSelection() => _selectedSource = -1;

    /// <summary>Sort by a column programmatically (same toggle rule as clicking its header: a new column
    /// selects it ascending, the current one flips direction). Handy for a default sort — and the seam
    /// unit tests use to drive sorting without the header UI.</summary>
    public void ToggleSort(int logicalColumn)
    {
        Model.ToggleSort(logicalColumn);
        _dirty = true;
    }

    /// <summary>Set the initial/default sort column + direction so the header shows its sort arrow without a
    /// click and the rows start in that order.</summary>
    public void SortBy(int logicalColumn, bool ascending = true)
    {
        // The first call is the declaration; later ones (if any) are runtime re-sorts, which must not
        // become what Reset Panels goes back to.
        if (_defaultSortColumn < 0)
        {
            _defaultSortColumn = logicalColumn;
            _defaultSortAscending = ascending;
        }
        Model.SetSort(logicalColumn, ascending);
        _dirty = true;
    }

    /// <summary><see cref="Items"/> permuted into the current display (sort) order. Pure — no rendering — so
    /// tests can assert the data-bound sort end to end.</summary>
    public IReadOnlyList<T> RowsInDisplayOrder()
    {
        if (_dirty) Resort();
        var result = new List<T>(_rowOrder.Length);
        foreach (int src in _rowOrder) result.Add(_items[src]);
        return result;
    }

    // ── Frame ──────────────────────────────────────────────────────────────────
    public void Update(InputState input, Rectangle bounds, bool keyboardActive = true)
    {
        SyncHeaders();
        LayoutChanged = false;            // set by UpdateHeader on a resize/reorder this frame
        _lastMouse = input.MousePosition;
        var m = Measure(bounds);
        _hScroll = Math.Clamp(_hScroll, 0, m.MaxHScroll);

        UpdateHeader(input, m.Header);    // may toggle sort / reorder / resize; marks _dirty on a sort change
        if (m.HActive) UpdateHScroll(input, m);
        // Shift + wheel scrolls HORIZONTALLY when the h-scrollbar is active and the pointer is over the table
        // (up = left, matching the ListBox vertical sign); consume it so the body doesn't also scroll vertically.
        // A plain wheel falls through to _body.Update below for the usual vertical scroll.
        if (m.HActive && input.IsShiftDown() && bounds.Contains(input.MousePosition))
        {
            int hWheel = input.ScrollWheelDelta();
            if (hWheel != 0)
            {
                _hScroll = Math.Clamp(_hScroll - hWheel / 120 * HScrollWheelStep, 0, m.MaxHScroll);
                input.ConsumeScrollWheel();
            }
        }
        if (_dirty) Resort();             // rebuild the row permutation + body items before the body reads them
        _body.Update(input, m.Body, keyboardActive);

        // Fold the body's (display-order) selection back into source terms so it survives the next re-sort.
        int sel = _body.SelectedIndex;
        _selectedSource = sel >= 0 && sel < _rowOrder.Length ? _rowOrder[sel] : -1;

        CheckTruncationTooltip(m);        // a hovered cell whose text is clipped shows the full text
    }

    public void Draw(SpriteBatch sb, SpriteFont font, Rectangle bounds)
    {
        SyncHeaders();
        _lastFont = font;                 // cached so the next Update can measure truncation for tooltips
        var m = Measure(bounds);
        _hScroll = Math.Clamp(_hScroll, 0, m.MaxHScroll);
        if (_dirty) Resort();             // SetRows/Items changed since the last Update (e.g. a Draw-time rebuild)

        // When columns overflow, scissor-clip the whole table to its bounds so scrolled cells can't bleed
        // past either edge; the vertical scrollbar draws on top of the rightmost sliver. No overflow = no clip.
        if (m.HActive) UiHelper.BeginClip(sb, bounds);
        _body.Draw(sb, font, m.Body);     // rows (via DrawRow); the header sits above, non-overlapping
        DrawHeader(sb, font, m.Header);
        if (m.HActive)
        {
            DrawHScroll(sb, m);
            UiHelper.EndClip(sb);
        }
    }

    private Metrics Measure(Rectangle bounds)
    {
        int colWindowW = Math.Max(0, bounds.Width - ListBox.ScrollbarWidth);   // columns live left of the v-scrollbar
        int total = Model.TotalWidth;
        bool hActive = total > colWindowW;
        int maxH = Math.Max(0, total - colWindowW);
        var header = new Rectangle(bounds.X, bounds.Y, bounds.Width, HeaderHeight);
        int bodyH = Math.Max(0, bounds.Height - HeaderHeight - (hActive ? HScrollHeight : 0));
        var body = new Rectangle(bounds.X, bounds.Y + HeaderHeight, bounds.Width, bodyH);
        var hbar = new Rectangle(bounds.X, body.Bottom, colWindowW, HScrollHeight);
        return new Metrics(header, body, hbar, hActive, maxH, colWindowW);
    }

    // ── Internals ──────────────────────────────────────────────────────────────
    private TableModel BuildModel()
    {
        if (_cols.Count == 0)
            throw new InvalidOperationException("Declare at least one Column() before using the table.");
        return new TableModel(_cols.Select(c => new TableColumn(c.Header(), c.Width, c.MinWidth)));
    }

    // Refresh the model's (string) headers from the columns' header selectors — so a language change is
    // picked up without any per-column plumbing at the call site.
    private void SyncHeaders()
    {
        var m = Model;
        for (int i = 0; i < _cols.Count; i++) m.Columns[i].Header = _cols[i].Header();
    }

    private int IndexOfKey(IReadOnlyList<T> items, object key)
    {
        for (int i = 0; i < items.Count; i++)
            if (Equals(_rowKey!(items[i]), key)) return i;
        return -1;
    }

    private void Resort()
    {
        _dirty = false;
        var m = Model;
        int n = _items.Count;
        _body.Items.Clear();

        var keys = new IComparable?[n];
        int sortCol = m.SortColumn;
        if (sortCol >= 0)
        {
            var sel = _cols[sortCol].SortKey;
            for (int i = 0; i < n; i++) keys[i] = sel(_items[i]);
        }
        _rowOrder = m.SortRowOrder(keys);   // identity when unsorted

        for (int i = 0; i < n; i++) _body.Items.Add("");   // ListBox row engine keys off Items.Count
        _body.SelectedIndex = _selectedSource >= 0 ? Array.IndexOf(_rowOrder, _selectedSource) : -1;
    }

    // A hovered header/cell whose text doesn't fit its column registers a full-text tooltip (drawn by the
    // shared Tooltip after all panels). Uses this Update's mouse + the font cached from the last Draw.
    private void CheckTruncationTooltip(Metrics m)
    {
        if (_lastFont is null) return;

        if (m.Header.Contains(_lastMouse))
        {
            foreach (var b in Model.Layout())
            {
                var cell = new Rectangle(m.Header.X + b.X - _hScroll, m.Header.Y, b.Width, m.Header.Height);
                if (!cell.Contains(_lastMouse)) continue;
                RegisterIfTruncated(Model.ColumnAt(b.Logical).Header, cell, ("h", b.Logical));
                return;
            }
            return;
        }

        int d = _body.HoveredIndex;
        if (d < 0 || d >= _rowOrder.Length) return;
        var rowRect = _body.HoveredRowRect();
        if (rowRect == Rectangle.Empty) return;
        var item = _items[_rowOrder[d]];
        foreach (var b in Model.Layout())
        {
            var cell = new Rectangle(rowRect.X + b.X - _hScroll, rowRect.Y, b.Width, rowRect.Height);
            if (!cell.Contains(_lastMouse)) continue;
            RegisterIfTruncated(_cols[b.Logical].Text(item), cell, (_rowOrder[d], b.Logical));
            return;
        }
    }

    private void RegisterIfTruncated(string text, Rectangle cell, object key)
        => UiHelper.LabelTooltip(_lastFont!, text,
            new Rectangle(cell.X + CellPadX, cell.Y, Math.Max(0, cell.Width - CellPadX * 2), cell.Height),
            _lastMouse, _tooltipScope, key);

    // ── Header input ─────────────────────────────────────────────────────────────
    private void UpdateHeader(InputState input, Rectangle header)
    {
        var boxes = Model.Layout();
        int localX = input.MousePosition.X - header.X + _hScroll;   // screen → content x (columns start at content 0)

        if (_drag == Drag.None && input.IsPressIn(header))
        {
            int dividerLogical = HitDivider(boxes, localX);
            if (dividerLogical >= 0)
            {
                _drag = Drag.Resize;
                _dragLogical = dividerLogical;
                _dragStartMouseX = input.MousePosition.X;
                _dragStartWidth = Model.ColumnAt(dividerLogical).Width;
                input.CaptureMouse(this);
            }
            else
            {
                int fromDisplay = HitColumn(boxes, localX);
                if (fromDisplay >= 0)
                {
                    _drag = Drag.Reorder;
                    _dragFromDisplay = fromDisplay;
                    _dragStartMouseX = input.MousePosition.X;
                    _dragCurrentX = input.MousePosition.X;
                    _dragMoved = false;
                    input.CaptureMouse(this);
                }
            }
        }

        switch (_drag)
        {
            case Drag.Resize:
                if (input.IsMouseDown())
                {
                    Model.ResizeColumn(_dragLogical, _dragStartWidth + (input.MousePosition.X - _dragStartMouseX));
                }
                else
                {
                    _drag = Drag.None;
                    _dragLogical = -1;
                    LayoutChanged = true;
                }  // persist the new width
                UiHelper.RequestResizeWeCursor();
                break;

            case Drag.Reorder:
                if (input.IsMouseDown())
                {
                    _dragCurrentX = input.MousePosition.X;
                    if (AllowReorder && Math.Abs(_dragCurrentX - _dragStartMouseX) > ReorderThreshold) _dragMoved = true;
                }
                else
                {
                    int fromLogical = boxes[_dragFromDisplay].Logical;
                    if (_dragMoved)
                    {
                        int relX = _dragCurrentX - header.X + _hScroll;
                        int toDisplay = HitColumn(boxes, relX);
                        if (toDisplay < 0) toDisplay = relX <= 0 ? 0 : Model.ColumnCount - 1;
                        Model.MoveColumn(_dragFromDisplay, toDisplay);
                        LayoutChanged = true;   // persist the new column order
                    }
                    else
                    {
                        Model.ToggleSort(fromLogical);   // a press with no travel is a sort click
                        _dirty = true;
                        LayoutChanged = true;   // persist the chosen sort
                    }
                    _drag = Drag.None;
                    _dragFromDisplay = -1;
                    _dragMoved = false;
                }
                break;

            default:
                // Geometric hover test (not IsHoverIn, which a panel-level hover-consume could suppress) so
                // the left/right resize cursor reliably shows whenever the pointer is over a column divider.
                if (IsOverColumnDivider(input.MousePosition, header))
                    UiHelper.RequestResizeWeCursor();
                break;
        }
    }

    /// <summary>Whether <paramref name="mouse"/> is over a column-resize divider inside the header strip
    /// <paramref name="header"/> — the pure geometry (current column layout + horizontal scroll) behind the
    /// left/right resize cursor. Exposed so the cursor rule is unit-testable without the hardware input
    /// path; the header-hover code forwards <c>input.MousePosition</c> here.</summary>
    public bool IsOverColumnDivider(Point mouse, Rectangle header)
        => header.Contains(mouse) && HitDivider(Model.Layout(), mouse.X - header.X + _hScroll) >= 0;

    // Logical index of the column whose right-edge divider is within DividerGrab of localX, else -1.
    private static int HitDivider(IReadOnlyList<TableColumnBox> boxes, int localX)
    {
        foreach (var b in boxes)
            if (Math.Abs(localX - (b.X + b.Width)) <= DividerGrab) return b.Logical;
        return -1;
    }

    // Display position of the column containing localX, else -1.
    private static int HitColumn(IReadOnlyList<TableColumnBox> boxes, int localX)
    {
        for (int i = 0; i < boxes.Count; i++)
            if (localX >= boxes[i].X && localX < boxes[i].X + boxes[i].Width) return i;
        return -1;
    }

    // ── Rendering ────────────────────────────────────────────────────────────────
    // RowRenderer callback: paint one body row's cells. rowIndex is the ListBox display row, which maps
    // through _rowOrder to the source row, whose cell text comes from each column's selector.
    private void DrawRow(SpriteBatch sb, SpriteFont font, int displayRow, Rectangle rowRect)
    {
        if (displayRow < 0 || displayRow >= _rowOrder.Length) return;
        var item = _items[_rowOrder[displayRow]];
        Color color = _rowColor?.Invoke(item) ?? Color.White;
        foreach (var b in Model.Layout())
        {
            UiHelper.DrawLabel(sb, font, _cols[b.Logical].Text(item),
                new Vector2(rowRect.X + b.X - _hScroll + CellPadX, rowRect.Y + RowTextPadY), color, b.Width - CellPadX * 2);
        }
    }

    // Horizontal scrollbar thumb (only meaningful when overflowing).
    private Rectangle HThumb(Metrics m)
    {
        int total = Model.TotalWidth;
        int trackW = m.HBar.Width;
        int thumbW = total > 0 ? Math.Max(16, trackW * m.ColWindowW / total) : trackW;
        int thumbX = m.MaxHScroll > 0 ? m.HBar.X + (trackW - thumbW) * _hScroll / m.MaxHScroll : m.HBar.X;
        return new Rectangle(thumbX, m.HBar.Y, thumbW, m.HBar.Height);
    }

    private void UpdateHScroll(InputState input, Metrics m)
    {
        var thumb = HThumb(m);
        // Thumb drag captures the pointer (same pattern as the ListBox v-scrollbar) so no other widget sees it.
        if (!_hDragging && input.IsPressIn(thumb))
        {
            _hDragging = true;
            _hDragStartX = input.MousePosition.X;
            _hDragStartScroll = _hScroll;
            input.CaptureMouse(this);
        }
        if (_hDragging)
        {
            if (input.IsMouseDown())
            {
                int range = m.HBar.Width - thumb.Width;
                if (range > 0)
                    _hScroll = Math.Clamp(_hDragStartScroll + (input.MousePosition.X - _hDragStartX) * m.MaxHScroll / range, 0, m.MaxHScroll);
            }
            else
            {
                _hDragging = false;   // capture auto-releases the frame after button-up
            }
        }
        // Click the track (not the thumb) → page by a column-window width.
        if (input.IsClickIn(m.HBar) && !thumb.Contains(input.MousePosition))
        {
            _hScroll = input.MousePosition.X < thumb.X
                ? Math.Clamp(_hScroll - m.ColWindowW, 0, m.MaxHScroll)
                : Math.Clamp(_hScroll + m.ColWindowW, 0, m.MaxHScroll);
            input.ConsumeMouseClick();
        }
    }

    private void DrawHScroll(SpriteBatch sb, Metrics m)
    {
        UiHelper.DrawFilledRect(sb, m.HBar, UiHelper.ListScrollTrackBg);
        var thumb = HThumb(m);
        UiHelper.DrawFilledRect(sb, thumb, UiHelper.ListScrollThumbBg);
        UiHelper.DrawBorder(sb, thumb, UiHelper.ListScrollThumbBorder);
    }

    private void DrawHeader(SpriteBatch sb, SpriteFont font, Rectangle header)
    {
        UiHelper.DrawFilledRect(sb, header, HeaderBg);
        foreach (var b in Model.Layout())
        {
            int cellX = header.X + b.X - _hScroll;
            var col = Model.ColumnAt(b.Logical);
            string arrow = Model.SortColumn == b.Logical ? (Model.SortAscending ? "^" : "v") : "";
            float arrowW = arrow.Length > 0 ? font.MeasureString(arrow).X + CellPadX : 0;
            UiHelper.DrawLabel(sb, font, col.Header, new Vector2(cellX + CellPadX, header.Y + RowTextPadY),
                HeaderText, b.Width - CellPadX * 2 - arrowW);
            if (arrow.Length > 0)
                sb.DrawString(font, arrow, new Vector2(cellX + b.Width - arrowW, header.Y + RowTextPadY), SortArrowColor);
            // Divider on the column's right edge.
            UiHelper.DrawFilledRect(sb, new Rectangle(cellX + b.Width - 1, header.Y, 1, header.Height), HeaderBorder);
        }
        UiHelper.DrawBorder(sb, header, HeaderBorder);

        // Reorder ghost: the grabbed header trailing the cursor.
        if (_drag == Drag.Reorder && _dragMoved && _dragFromDisplay >= 0 && _dragFromDisplay < Model.ColumnCount)
        {
            var col = Model.ColumnAt(Model.Layout()[_dragFromDisplay].Logical);
            var ghost = new Rectangle(_dragCurrentX - ReorderGhostHalfW, header.Y, ReorderGhostHalfW * 2, header.Height);
            UiHelper.DrawFilledRect(sb, ghost, ReorderGhostBg);
            UiHelper.DrawBorder(sb, ghost, HeaderBorder);
            UiHelper.DrawLabel(sb, font, col.Header, new Vector2(ghost.X + CellPadX, ghost.Y + RowTextPadY),
                HeaderText, ghost.Width - CellPadX * 2);
        }
    }
}

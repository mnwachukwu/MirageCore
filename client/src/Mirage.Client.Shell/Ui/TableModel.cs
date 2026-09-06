namespace Mirage.Client.Shell.Ui;

/// <summary>One column's definition: a header + minimum width, and a live, drag-resizable
/// <see cref="Width"/> (always clamped at/above <see cref="MinWidth"/>). A column keeps a stable
/// LOGICAL index (its position in <see cref="TableModel.Columns"/>) that never changes when the user
/// reorders the display; only <see cref="TableModel.Order"/> permutes. <see cref="Header"/> is settable
/// so a consumer can re-localize it on a language change.</summary>
public sealed class TableColumn
{
    public const int DefaultMinWidth = 24;
    public const int DefaultWidth = 80;
    public string Header { get; set; }
    public int MinWidth { get; }
    public int Width { get; set; }
    /// <summary>The width the host declared this column with. <see cref="Width"/> is mutated in place by
    /// resize drags and by a restored layout, so the original has to be kept separately for
    /// <see cref="TableModel.ResetLayout"/> to have something to go back to.</summary>
    public int DeclaredWidth { get; }

    public TableColumn(string header, int width, int minWidth = DefaultMinWidth)
    {
        Header = header ?? "";
        MinWidth = Math.Max(1, minWidth);
        Width = DeclaredWidth = Math.Max(MinWidth, width);
    }
}

/// <summary>A column's placement in DISPLAY order: its logical index plus the x-offset (from the table's
/// left edge) and pixel width. Produced by <see cref="TableModel.Layout"/> so the header strip and the
/// row cells draw against one shared geometry (they can never drift out of alignment).</summary>
public readonly record struct TableColumnBox(int Logical, int X, int Width);

/// <summary>The pure, render-free state of a <see cref="Table{T}"/>: the column set, the current display
/// order, the resizable widths, and the active sort (column + direction). All mutations are here and
/// fully unit-tested; the <see cref="Table{T}"/> view is a thin input/draw shell over it. Row data lives
/// with the consumer — the model only turns a column's per-row keys into a sorted row permutation via
/// <see cref="SortRowOrder"/>, so it stays ignorant of the row type.</summary>
public sealed class TableModel
{
    private readonly List<TableColumn> _columns;
    private int[] _order;   // display position → logical column index

    /// <summary>Logical index of the column the rows are sorted by, or -1 when unsorted.</summary>
    public int SortColumn { get; private set; } = -1;
    /// <summary>True for ascending, false for descending. Only meaningful when <see cref="SortColumn"/> >= 0.</summary>
    public bool SortAscending { get; private set; } = true;

    public TableModel(IEnumerable<TableColumn> columns)
    {
        _columns = columns.ToList();
        if (_columns.Count == 0)
            throw new ArgumentException("A table needs at least one column.", nameof(columns));
        _order = new int[_columns.Count];
        for (int i = 0; i < _order.Length; i++) _order[i] = i;
    }

    public int ColumnCount => _columns.Count;
    /// <summary>Columns in stable LOGICAL order (never permuted by reordering).</summary>
    public IReadOnlyList<TableColumn> Columns => _columns;
    /// <summary>The display order: <c>Order[displayPos]</c> is the logical column shown at that position.</summary>
    public IReadOnlyList<int> Order => _order;
    public TableColumn ColumnAt(int logical) => _columns[logical];
    public int TotalWidth
    {
        get
        {
            int w = 0;
            foreach (var c in _columns) w += c.Width;
            return w;
        }
    }

    /// <summary>Columns in DISPLAY order with cumulative x-offsets — the single geometry the header and
    /// the rows both lay out against.</summary>
    public IReadOnlyList<TableColumnBox> Layout()
    {
        var boxes = new List<TableColumnBox>(_order.Length);
        int x = 0;
        foreach (int logical in _order)
        {
            var c = _columns[logical];
            boxes.Add(new TableColumnBox(logical, x, c.Width));
            x += c.Width;
        }
        return boxes;
    }

    /// <summary>Reorder the display: pull the column at display position <paramref name="fromDisplay"/> and
    /// reinsert it so it occupies display position <paramref name="toDisplay"/>. <paramref name="toDisplay"/>
    /// is clamped into range; an out-of-range source or a no-op move is ignored. Logical indices (and thus
    /// the sort target and each column's cell mapping) are untouched.</summary>
    public void MoveColumn(int fromDisplay, int toDisplay)
    {
        int n = _order.Length;
        if (fromDisplay < 0 || fromDisplay >= n) return;
        toDisplay = Math.Clamp(toDisplay, 0, n - 1);
        if (fromDisplay == toDisplay) return;
        int logical = _order[fromDisplay];
        var list = new List<int>(_order);
        list.RemoveAt(fromDisplay);
        list.Insert(toDisplay, logical);   // after the removal, inserting at toDisplay lands it there in both directions
        _order = list.ToArray();
    }

    /// <summary>Set a column's width by LOGICAL index, clamped up to its <see cref="TableColumn.MinWidth"/>.
    /// Out-of-range indices are ignored.</summary>
    public void ResizeColumn(int logical, int width)
    {
        if (logical < 0 || logical >= _columns.Count) return;
        var c = _columns[logical];
        c.Width = Math.Max(c.MinWidth, width);
    }

    /// <summary>Restore a persisted layout: <paramref name="widths"/> is a per-logical-column width and
    /// <paramref name="order"/> is a display→logical permutation. Each is applied only if its shape matches
    /// this model (so a config saved against a different column set is ignored, never corrupts the table);
    /// widths are clamped up to each column's <see cref="TableColumn.MinWidth"/>. Sort state is not persisted.</summary>
    public void ApplyLayout(IReadOnlyList<int> order, IReadOnlyList<int> widths)
    {
        if (widths.Count == _columns.Count)
            for (int i = 0; i < _columns.Count; i++) _columns[i].Width = Math.Max(_columns[i].MinWidth, widths[i]);
        if (order.Count == _columns.Count && IsPermutation(order))
            _order = order.ToArray();
    }

    /// <summary>Throw away every layout customization — display order, widths and sort — leaving the model
    /// exactly as the host declared it. The inverse of <see cref="ApplyLayout"/>, and what the Options
    /// panel's Reset Panels button drives. The sort is cleared rather than restored because the model has
    /// no idea which sort was a host default and which was a header click; <see cref="Table{T}"/> owns that
    /// distinction and re-applies its default afterwards.</summary>
    public void ResetLayout()
    {
        for (int i = 0; i < _columns.Count; i++)
        {
            _columns[i].Width = _columns[i].DeclaredWidth;
            _order[i] = i;
        }
        SortColumn = -1;
        SortAscending = true;
    }

    private bool IsPermutation(IReadOnlyList<int> order)
    {
        var seen = new bool[_columns.Count];
        foreach (int v in order)
        {
            if (v < 0 || v >= _columns.Count || seen[v]) return false;
            seen[v] = true;
        }
        return true;
    }

    /// <summary>Click a header: sorting on a NEW column selects it ascending; clicking the column that is
    /// already the sort target flips the direction. Out-of-range indices are ignored.</summary>
    public void ToggleSort(int logical)
    {
        if (logical < 0 || logical >= _columns.Count) return;
        if (SortColumn == logical)
        {
            SortAscending = !SortAscending;
        }
        else
        {
            SortColumn = logical;
            SortAscending = true;
        }
    }

    /// <summary>Set the sort column + direction directly — for an initial/default sort (so the header shows
    /// its sort arrow from the start), unlike <see cref="ToggleSort"/> which flips. Out-of-range ignored.</summary>
    public void SetSort(int logical, bool ascending)
    {
        if (logical < 0 || logical >= _columns.Count) return;
        SortColumn = logical;
        SortAscending = ascending;
    }

    /// <summary>Turn the active-sort-column's per-row keys into a display permutation: returns row indices
    /// [0..keys.Count) ordered by <paramref name="keys"/> in the current direction. The sort is STABLE —
    /// rows with equal keys keep their original relative order in BOTH directions — and nulls sort first
    /// ascending. When unsorted (<see cref="SortColumn"/> < 0) returns the identity order unchanged.
    /// The caller supplies keys for whichever column <see cref="SortColumn"/> names.</summary>
    public int[] SortRowOrder(IReadOnlyList<IComparable?> keys)
    {
        int n = keys.Count;
        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;
        if (SortColumn < 0) return order;
        Array.Sort(order, (a, b) =>
        {
            int cmp = Comparer<object?>.Default.Compare(keys[a], keys[b]);
            if (cmp != 0) return SortAscending ? cmp : -cmp;
            return a.CompareTo(b);   // stable tiebreak: equal keys keep ascending original order regardless of direction
        });
        return order;
    }
}

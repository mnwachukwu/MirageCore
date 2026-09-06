using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Mirage.Editor.Localization;
using Mirage.Editor.ViewModels;
using Mirage.Shared;
using System.Globalization;

namespace Mirage.Editor.Controls;

/// <summary>
/// Scrollable grid showing all tiles from a tileset bitmap.
/// Click a single tile or drag to select a rectangle; result is exposed as SelectedStamp.
/// Tiles are arranged in columns of 7 (matching the client's sprite atlas math).
/// </summary>
public sealed class TilePaletteControl : Control
{
    public static readonly StyledProperty<Bitmap?> TileBitmapProperty =
        AvaloniaProperty.Register<TilePaletteControl, Bitmap?>(nameof(TileBitmap));

    public static readonly StyledProperty<TileStamp?> SelectedStampProperty =
        AvaloniaProperty.Register<TilePaletteControl, TileStamp?>(nameof(SelectedStamp));

    public Bitmap? TileBitmap
    {
        get => GetValue(TileBitmapProperty);
        set => SetValue(TileBitmapProperty, value);
    }

    public TileStamp? SelectedStamp
    {
        get => GetValue(SelectedStampProperty);
        set => SetValue(SelectedStampProperty, value);
    }

    private const int TileW = 32;
    private const int TileH = 32;

    private static readonly Pen SelectedPen = new(Brushes.Cyan, 2);

    // RTB cache — rebuilt only when TileBitmap changes; selection drawn on top each frame.
    private RenderTargetBitmap? _cache;
    private bool _cacheDirty = true;
    private int _rtbRetryCount;

    // Drag-selection state.
    private int _selStartCol;
    private int _selStartRow;
    private bool _isDragging;

    // Palette-space rect of the current selection (for rendering).
    private int _palSelCol = -1;
    private int _palSelRow = -1;
    private int _palSelCols;
    private int _palSelRows;

    static TilePaletteControl()
    {
        AffectsMeasure<TilePaletteControl>(TileBitmapProperty);
        AffectsRender<TilePaletteControl>(TileBitmapProperty, SelectedStampProperty);
        TileBitmapProperty.Changed.AddClassHandler<TilePaletteControl>(
            (c, _) => { c._cacheDirty = true; c._rtbRetryCount = 0; c._palSelCol = -1; });
    }

    private static int ColsFromBitmap(Bitmap bmp) =>
        Math.Max(1, (int)(bmp.Size.Width / TileW));

    private void RebuildCache(Bitmap bmp, int cols, int rows, int totalTiles)
    {
        _cacheDirty = false;
        // Three consecutive failures stop the rebuild running on every frame; a new sheet clears the budget
        // (TileBitmapProperty above), so a transient failure costs a redraw rather than the palette.
        if (_rtbRetryCount >= 3) return;

        var size = new PixelSize(cols * TileW, rows * TileH);
        try
        {
            if (_cache is null || _cache.PixelSize != size)
            {
                _cache?.Dispose();
                _cache = new RenderTargetBitmap(size, new Vector(96, 96));
            }
            using var rctx = _cache.CreateDrawingContext();
            rctx.FillRectangle(Brushes.Black, new Rect(0, 0, size.Width, size.Height));
            DrawTilesDirect(rctx, bmp, cols, rows, totalTiles);
            _rtbRetryCount = 0;
        }
        catch
        {
            _cache?.Dispose();
            _cache = null;
            _rtbRetryCount++;
            _cacheDirty = true;
        }
    }

    private static void DrawTilesDirect(DrawingContext ctx, Bitmap bmp, int cols, int rows, int totalTiles)
    {
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int tileIndex = r * cols + c;
                if (tileIndex >= totalTiles) break;
                var tileRect = new Rect(c * TileW, r * TileH, TileW, TileH);
                ctx.DrawImage(bmp, tileRect, tileRect);
            }
        }
    }

    // The empty-palette caption is resolved inside Render, and Avalonia is retained-mode: Render
    // runs only when the visual is invalidated. So unlike the client's per-frame Draw loop, an
    // inline fetch here does NOT self-heal — without forcing a repaint the caption keeps the old
    // language until some unrelated interaction happens to invalidate the control.
    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        EditorStrings.LanguageChanged += InvalidateVisual;
    }

    protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        EditorStrings.LanguageChanged -= InvalidateVisual;
        base.OnDetachedFromLogicalTree(e);
    }

    public override void Render(DrawingContext ctx)
    {
        var bmp = TileBitmap;
        if (bmp is null)
        {
            ctx.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
            ctx.DrawText(new FormattedText(
                EditorStrings.Get(EditorStrings.TilePalette_NoTileset),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                13,
                Brushes.Gray), new Point(4, 4));
            return;
        }

        int cols = ColsFromBitmap(bmp);
        int totalTiles = cols * (int)(bmp.Size.Height / TileH);
        int rows = (int)Math.Ceiling((double)totalTiles / cols);

        if (_cacheDirty || _cache is null)
            RebuildCache(bmp, cols, rows, totalTiles);

        if (_cache is not null)
        {
            var cacheRect = new Rect(0, 0, _cache.PixelSize.Width, _cache.PixelSize.Height);
            ctx.DrawImage(_cache, cacheRect, cacheRect);
        }
        else
        {
            ctx.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
            DrawTilesDirect(ctx, bmp, cols, rows, totalTiles);
        }

        // Selection highlight — drawn on top of cache, no cache rebuild needed.
        if (_palSelCol >= 0 && _palSelCols > 0 && _palSelRows > 0)
        {
            ctx.DrawRectangle(null, SelectedPen,
                new Rect(_palSelCol * TileW, _palSelRow * TileH, _palSelCols * TileW, _palSelRows * TileH));
        }
    }

    // Computes and sets SelectedStamp from the current start + end palette cell.
    // Also updates _palSel* fields for rendering.
    private void UpdateStamp(Bitmap bmp, int endCol, int endRow)
    {
        int cols = ColsFromBitmap(bmp);
        int totalTiles = cols * (int)(bmp.Size.Height / TileH);

        int minCol = Math.Min(_selStartCol, endCol);
        int maxCol = Math.Min(Math.Max(_selStartCol, endCol), cols - 1);
        int minRow = Math.Min(_selStartRow, endRow);
        int maxRow = Math.Max(_selStartRow, endRow);

        int stampCols = maxCol - minCol + 1;
        int stampRows = maxRow - minRow + 1;

        var indices = new int[stampCols, stampRows];
        for (int dr = 0; dr < stampRows; dr++)
        {
            for (int dc = 0; dc < stampCols; dc++)
            {
                int tileIdx = (minRow + dr) * cols + (minCol + dc) + 1; // 1-based
                indices[dc, dr] = tileIdx <= totalTiles ? tileIdx : 0;
            }
        }

        _palSelCol = minCol;
        _palSelRow = minRow;
        _palSelCols = stampCols;
        _palSelRows = stampRows;

        SelectedStamp = new TileStamp(stampCols, stampRows, indices);
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed is false) return;
        var bmp = TileBitmap;
        if (bmp is null) return;

        int cols = ColsFromBitmap(bmp);
        var pos = e.GetPosition(this);
        int col = Math.Clamp((int)(pos.X / TileW), 0, cols - 1);
        int row = Math.Max(0, (int)(pos.Y / TileH));

        _selStartCol = col;
        _selStartRow = row;
        _isDragging = true;
        e.Pointer.Capture(this);

        UpdateStamp(bmp, col, row);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isDragging) return;
        var bmp = TileBitmap;
        if (bmp is null) return;

        int cols = ColsFromBitmap(bmp);
        var pos = e.GetPosition(this);
        int col = Math.Clamp((int)(pos.X / TileW), 0, cols - 1);
        int row = Math.Max(0, (int)(pos.Y / TileH));

        UpdateStamp(bmp, col, row);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_isDragging) return;
        _isDragging = false;
        e.Pointer.Capture(null);

        var bmp = TileBitmap;
        if (bmp is null) return;

        int cols = ColsFromBitmap(bmp);
        var pos = e.GetPosition(this);
        int col = Math.Clamp((int)(pos.X / TileW), 0, cols - 1);
        int row = Math.Max(0, (int)(pos.Y / TileH));

        UpdateStamp(bmp, col, row);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var bmp = TileBitmap;
        if (bmp is null) return new Size(TileW, TileH);
        int cols = ColsFromBitmap(bmp);
        int totalTiles = cols * (int)(bmp.Size.Height / TileH);
        int rows = Math.Max(1, (int)Math.Ceiling((double)totalTiles / cols));
        return new Size(cols * TileW, rows * TileH);
    }
}

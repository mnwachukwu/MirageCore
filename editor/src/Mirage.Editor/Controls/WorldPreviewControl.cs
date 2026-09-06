using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Mirage.Editor.Localization;
using Mirage.Editor.Services;
using Mirage.Shared.Records;
using System.Globalization;

namespace Mirage.Editor.Controls;

/// <summary>
/// A read-only canvas of the maps around the open one: one map per grid cell, drawn from the same tile art
/// the map editor uses.
///
/// <para>Maps near the viewport are each held as their own bitmap, rendered at the smallest power-of-two
/// scale at or above the current zoom. Never below it: what is on screen is always drawn from at least as
/// many pixels as it occupies, so it reads as real map art rather than a thumbnail, while a reach of ten
/// maps in every direction stays affordable. Holding all 441 at native size would be around 330 MB of
/// render targets, and 441 GPU surfaces is the part a driver may refuse outright.</para>
///
/// <para>One bitmap per map rather than one for the whole region, for two reasons: a single surface would
/// pass the 8192 px texture ceiling well inside the radius, and a one-tile edit would rebuild every map on
/// the grid instead of the one that changed.</para>
///
/// <para>Bitmaps are built on the dispatcher a few at a time, never inside <see cref="Render"/>: a render
/// target is UI-thread-affine, and allocating one mid-draw stalls the frame that is trying to present.</para>
/// </summary>
public sealed class WorldPreviewControl : Control
{
    private const int TilePx = 32;
    private const int BuildsPerPass = 3;
    private const double MinZoom = 0.05;

    // Native resolution is the ceiling: past it the map is upscaled and goes soft, which is the one thing
    // rendering at the zoom bucket exists to avoid. Not a memory limit — culling to the viewport is what
    // bounds that, and it bounds it at every zoom.
    private const double MaxZoom = 1.0;

    private static readonly IBrush Backdrop = new SolidColorBrush(Color.FromRgb(18, 18, 20));
    private static readonly IBrush EmptyCellBrush = new SolidColorBrush(Color.FromRgb(30, 30, 34));
    private static readonly IBrush GroupTintBrush = new SolidColorBrush(Color.FromArgb(44, 255, 190, 90));
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.FromArgb(200, 235, 235, 240));
    private static readonly IBrush LabelBackBrush = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
    private static readonly IBrush WarpBadgeBrush = new SolidColorBrush(Color.FromArgb(205, 120, 60, 165));
    private static readonly IBrush WarpLabelBrush = new SolidColorBrush(Color.FromArgb(240, 245, 235, 255));

    // Four outlines that have to stay apart at a glance, so each owns its own hue: gray is structure,
    // amber is group membership, white is the cursor, and cyan is the one map being edited.
    private static readonly Pen GridPen = new(new SolidColorBrush(Color.FromArgb(90, 120, 120, 130)), 1);
    private static readonly Pen GroupPen = new(new SolidColorBrush(Color.FromArgb(190, 255, 190, 90)), 2);
    private static readonly Pen HoverPen = new(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 2);
    private static readonly Pen OriginPen = new(new SolidColorBrush(Color.FromArgb(255, 0, 229, 255)), 3);

    private readonly Dictionary<int, RenderTargetBitmap> _cache = [];
    private readonly Dictionary<int, int> _warpCounts = [];
    private readonly HashSet<int> _pending = [];
    private readonly Dictionary<int, Rect> _warpBadges = [];
    private double _cacheScale = 1.0;
    private bool _buildScheduled;
    private bool _panMode;
    private Point _panLastPos;
    private int _hoverMap;

    /// <summary>Maps to draw and the cells they sit in.</summary>
    public static readonly StyledProperty<MapLinkLayoutResult> LayoutProperty =
        AvaloniaProperty.Register<WorldPreviewControl, MapLinkLayoutResult>(
            nameof(Layout), MapLinkLayoutResult.Empty);

    /// <summary>Tileset sheets, indexed by sheet number.</summary>
    public static readonly StyledProperty<IReadOnlyList<Bitmap?>> TilesetsProperty =
        AvaloniaProperty.Register<WorldPreviewControl, IReadOnlyList<Bitmap?>>(nameof(Tilesets), []);

    /// <summary>Canvas scale; 1.0 draws one screen pixel per map pixel.</summary>
    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<WorldPreviewControl, double>(nameof(Zoom), 0.25);

    /// <summary>The open map, outlined so it can be found on a crowded grid.</summary>
    public static readonly StyledProperty<int> OriginMapProperty =
        AvaloniaProperty.Register<WorldPreviewControl, int>(nameof(OriginMap));

    /// <summary>Map group of the open map; maps sharing it are tinted. 0 tints nothing.</summary>
    public static readonly StyledProperty<int> OriginGroupProperty =
        AvaloniaProperty.Register<WorldPreviewControl, int>(nameof(OriginGroup));

    /// <summary>The part of the canvas actually on screen, in canvas coordinates. Set by the host from its
    /// scroll viewer; an empty rect means "assume everything is visible".</summary>
    public static readonly StyledProperty<Rect> ViewportProperty =
        AvaloniaProperty.Register<WorldPreviewControl, Rect>(nameof(Viewport));

    public MapLinkLayoutResult Layout
    {
        get => GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    public IReadOnlyList<Bitmap?> Tilesets
    {
        get => GetValue(TilesetsProperty);
        set => SetValue(TilesetsProperty, value);
    }

    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public int OriginMap
    {
        get => GetValue(OriginMapProperty);
        set => SetValue(OriginMapProperty, value);
    }

    public int OriginGroup
    {
        get => GetValue(OriginGroupProperty);
        set => SetValue(OriginGroupProperty, value);
    }

    public Rect Viewport
    {
        get => GetValue(ViewportProperty);
        set => SetValue(ViewportProperty, value);
    }

    /// <summary>A map cell was clicked.</summary>
    public event Action<int>? MapClicked;

    /// <summary>A map's warp badge was clicked, asking for its warp destinations.</summary>
    public event Action<int>? WarpsClicked;

    /// <summary>A new zoom was asked for, with the canvas point under the cursor to keep still.</summary>
    public event Action<double, Point>? ZoomRequested;

    /// <summary>Ctrl+drag moved by this much.</summary>
    public event Action<Vector>? PanRequested;

    /// <summary>The wheel asked to scroll by this much.</summary>
    public event Action<Vector>? ScrollRequested;

    static WorldPreviewControl()
    {
        AffectsRender<WorldPreviewControl>(LayoutProperty, ZoomProperty, OriginMapProperty, OriginGroupProperty);
        AffectsMeasure<WorldPreviewControl>(LayoutProperty, ZoomProperty);
        LayoutProperty.Changed.AddClassHandler<WorldPreviewControl>((c, _) => c.OnLayoutReplaced());
        TilesetsProperty.Changed.AddClassHandler<WorldPreviewControl>((c, _) => c.DropAll());
        // Both change which maps deserve a surface and at what scale.
        ZoomProperty.Changed.AddClassHandler<WorldPreviewControl>((c, _) => c.ScheduleBuild());
        ViewportProperty.Changed.AddClassHandler<WorldPreviewControl>((c, _) => c.ScheduleBuild());
    }

    public WorldPreviewControl()
    {
        ClipToBounds = true;
        Focusable = true;
        EditorStrings.LanguageChanged += OnLanguageChanged;
        DetachedFromVisualTree += (_, _) => EditorStrings.LanguageChanged -= OnLanguageChanged;
    }

    // The warp badge spells out its label once the cell is wide enough, so a language switch has to repaint.
    private void OnLanguageChanged() => InvalidateVisual();

    // One cell in pixels, read off the open map's own tile counts. Every map in a connected region is the
    // same size (a link between two differently-sized maps is refused at authoring time), so one map's size
    // sizes the whole grid. Nothing placed means nothing to size: an empty grid has no cell, and there is
    // no default to stand in for one.
    private (int W, int H) CellPixels()
    {
        var layout = Layout;
        if (layout.Placements.Count == 0) return (0, 0);
        var origin = layout.Placements.FirstOrDefault(p => p.MapNum == OriginMap);
        var rec = origin.Record ?? layout.Placements[0].Record;
        return (rec.Width * TilePx, rec.Height * TilePx);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var layout = Layout;
        var (cw, ch) = CellPixels();
        if (cw == 0 || ch == 0) return default;
        return new Size(layout.CellsWide * cw * Zoom, layout.CellsHigh * ch * Zoom);
    }

    /// <summary>Throws away one map's bitmap so it is redrawn from the current record.</summary>
    public void Invalidate(int mapNum)
    {
        if (_cache.Remove(mapNum, out var bmp)) bmp.Dispose();
        _warpCounts.Remove(mapNum);
        ScheduleBuild();
        InvalidateVisual();
    }

    private void OnLayoutReplaced()
    {
        // Maps that walked out of range give their surfaces back; the rest keep theirs, so a re-walk after
        // a link edit does not repaint the region.
        var keep = Layout.Placements.Select(p => p.MapNum).ToHashSet();
        foreach (int id in _cache.Keys.Where(id => !keep.Contains(id)).ToList())
        {
            _cache[id].Dispose();
            _cache.Remove(id);
        }
        foreach (int id in _warpCounts.Keys.Where(id => !keep.Contains(id)).ToList())
            _warpCounts.Remove(id);
        _pending.Clear();
        ScheduleBuild();
    }

    private void DropAll()
    {
        foreach (var bmp in _cache.Values) bmp.Dispose();
        _cache.Clear();
        _warpCounts.Clear();
        _pending.Clear();
        ScheduleBuild();
        InvalidateVisual();
    }

    // Warp destinations are counted once per map and kept with its bitmap: the scan walks every tile on both
    // planes, which is not something to redo on each frame.
    private int WarpCountOf(MapPlacement p)
    {
        if (_warpCounts.TryGetValue(p.MapNum, out int n)) return n;
        n = WarpLinks.WarpOnlyDestinations(p.MapNum, p.Record).Count;
        _warpCounts[p.MapNum] = n;
        return n;
    }

    private void ScheduleBuild()
    {
        if (_buildScheduled) return;
        _buildScheduled = true;
        Dispatcher.UIThread.Post(BuildPass, DispatcherPriority.Background);
    }

    /// <summary>
    /// The scale maps are rendered at: the smallest power of two at or above the current zoom.
    ///
    /// <para>Never below the zoom, so what is on screen is always drawn from at least as many pixels as it
    /// occupies and looks identical to rendering at full size. Stepping in powers of two rather than tracking
    /// the zoom exactly means a pinch through a range of scales re-renders a handful of times instead of
    /// every frame.</para>
    ///
    /// <para>This is what makes a radius of ten affordable. Holding all 441 maps at native size would be
    /// about 330 MB of render targets — and 441 GPU surfaces, which is the part a driver is liable to simply
    /// refuse. At the opening zoom the same grid is nearer 5 MB.</para>
    /// </summary>
    private double ScaleBucket() => ScaleBucketFor(Zoom);

    /// <inheritdoc cref="ScaleBucket"/>
    internal static double ScaleBucketFor(double zoom)
    {
        double scale = 1.0 / 16.0;
        while (scale < zoom && scale < 1.0) scale *= 2;
        return Math.Min(scale, 1.0);
    }

    // Maps near enough to the viewport to be worth holding a surface for. At the opening zoom that is the
    // whole grid; zoomed in it is a handful, which is what keeps the cache bounded at every scale.
    private bool NearViewport(MapPlacement p, MapLinkLayoutResult layout, double cellW, double cellH, double margin)
    {
        var view = Viewport;
        if (view.Width <= 0 || view.Height <= 0) return true;
        return CellRect(p, layout, cellW, cellH).Inflate(margin).Intersects(view);
    }

    private void BuildPass()
    {
        _buildScheduled = false;
        var layout = Layout;
        var tilesets = Tilesets;
        double bucket = ScaleBucket();
        var (cw, ch) = CellPixels();
        if (cw == 0 || ch == 0) return;
        double cellW = cw * Zoom, cellH = ch * Zoom;

        // A zoom step past a power of two invalidates every surface at once: they were all built at the old
        // scale, and a bitmap coarser than the screen is the one thing this must never draw.
        if (Math.Abs(bucket - _cacheScale) > 0.0001)
        {
            foreach (var stale in _cache.Values) stale.Dispose();
            _cache.Clear();
            _cacheScale = bucket;
        }

        var wanted = layout.Placements
            .Where(p => NearViewport(p, layout, cellW, cellH, Math.Max(cellW, cellH)))
            .ToList();

        foreach (int id in _cache.Keys.Where(id => !wanted.Any(p => p.MapNum == id)).ToList())
        {
            _cache[id].Dispose();
            _cache.Remove(id);
        }

        int built = 0;
        foreach (var p in wanted)
        {
            if (built >= BuildsPerPass) break;
            if (_cache.ContainsKey(p.MapNum) || !_pending.Add(p.MapNum)) continue;
            try
            {
                _cache[p.MapNum] = RenderMapAtScale(p.Record, tilesets, bucket);
            }
            catch (Exception ex)
            {
                EditorLog.Warn("World preview could not render map {Map}: {Error}", p.MapNum, ex.Message);
            }
            finally
            {
                _pending.Remove(p.MapNum);
                built++;
            }
        }

        if (built > 0) InvalidateVisual();
        if (wanted.Any(p => !_cache.ContainsKey(p.MapNum))) ScheduleBuild();
    }

    // One map drawn through the map editor's own tile path, into a surface scaled by `scale`.
    private static RenderTargetBitmap RenderMapAtScale(
        MapRecord map, IReadOnlyList<Bitmap?> tilesets, double scale)
    {
        int nativeW = TileGridControl.MapPixelW(map), nativeH = TileGridControl.MapPixelH(map);
        int w = Math.Max(1, (int)Math.Round(nativeW * scale));
        int h = Math.Max(1, (int)Math.Round(nativeH * scale));

        var rtb = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
        using var ctx = rtb.CreateDrawingContext();
        ctx.FillRectangle(Brushes.Black, new Rect(0, 0, w, h));
        using (ctx.PushTransform(Matrix.CreateScale(scale, scale)))
            TileGridControl.DrawPlacementsBand(ctx, [(map, 0, 0)], tilesets, 0, nativeH);
        return rtb;
    }

    public override void Render(DrawingContext ctx)
    {
        var layout = Layout;
        double zoom = Zoom;

        ctx.FillRectangle(Backdrop, new Rect(Bounds.Size));

        var (cw, ch) = CellPixels();
        if (cw == 0 || ch == 0) return;
        double cellW = cw * zoom, cellH = ch * zoom;

        foreach (var p in layout.Placements)
        {
            var dst = CellRect(p, layout, cellW, cellH);

            if (_cache.TryGetValue(p.MapNum, out var bmp))
            {
                // The bitmap is at the cache scale, not at zoom, so its pixels are converted back to map
                // pixels before being laid out. A map larger than the cell is clipped rather than squashed,
                // so a world that broke the same-size rule outside the editor reads as wrong instead of
                // merely looking odd.
                double srcW = Math.Min(bmp.Size.Width, cw * _cacheScale);
                double srcH = Math.Min(bmp.Size.Height, ch * _cacheScale);
                double shown = zoom / _cacheScale;
                ctx.DrawImage(bmp, new Rect(0, 0, srcW, srcH),
                    new Rect(dst.X, dst.Y, srcW * shown, srcH * shown));
            }
            else
            {
                ctx.FillRectangle(EmptyCellBrush, dst);
            }

            if (OriginGroup > 0 && p.Record.MapGroup == OriginGroup)
            {
                ctx.FillRectangle(GroupTintBrush, dst);
                ctx.DrawRectangle(GroupPen, dst);
            }
            ctx.DrawRectangle(GridPen, dst);
        }

        _warpBadges.Clear();
        foreach (var p in layout.Placements)
        {
            var dst = CellRect(p, layout, cellW, cellH);
            if (p.MapNum == _hoverMap && p.MapNum != OriginMap) ctx.DrawRectangle(HoverPen, dst.Deflate(1));
            // Inset by half the stroke so the current map's outline sits wholly inside its own cell, and
            // is not half-clipped where the cell meets the edge of the region.
            if (p.MapNum == OriginMap) ctx.DrawRectangle(OriginPen, dst.Deflate(1.5));
            DrawLabel(ctx, p, dst);
            var badge = DrawWarpBadge(ctx, p, dst);
            if (badge != default) _warpBadges[p.MapNum] = badge;
        }
    }

    private static Rect CellRect(MapPlacement p, MapLinkLayoutResult layout, double cellW, double cellH) =>
        new((p.CellX - layout.MinX) * cellW, (p.CellY - layout.MinY) * cellH, cellW, cellH);

    // The map number, and its name when the cell is wide enough to read one. The number carries a "#" so it
    // cannot be read as the warp count in the opposite corner.
    private void DrawLabel(DrawingContext ctx, MapPlacement p, Rect dst)
    {
        if (dst.Width < 44) return;
        string text = dst.Width >= 150 && !string.IsNullOrWhiteSpace(p.Record.Name)
            ? $"#{p.MapNum}  {p.Record.Name}"
            : $"#{p.MapNum}";

        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            Typeface.Default, 11, LabelBrush) { MaxTextWidth = Math.Max(1, dst.Width - 8) };
        var at = new Point(dst.X + 4, dst.Y + 3);
        ctx.FillRectangle(LabelBackBrush, new Rect(at.X - 2, at.Y - 1, ft.Width + 4, ft.Height + 2));
        ctx.DrawText(ft, at);
    }

    // How many other maps this one reaches by warp, bottom-right. Warps are the connections the grid cannot
    // show, so the count is the only sign they exist; a map with none stays clean.
    private Rect DrawWarpBadge(DrawingContext ctx, MapPlacement p, Rect dst)
    {
        if (dst.Width < 44) return default;
        int count = WarpCountOf(p);
        if (count == 0) return default;

        string text = dst.Width >= 150
            ? $"{EditorStrings.Get(EditorStrings.WorldPreview_WarpsLabel)} {count}"
            : count.ToString(CultureInfo.CurrentCulture);

        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            Typeface.Default, 11, WarpLabelBrush);
        var box = new Rect(dst.Right - ft.Width - 8, dst.Bottom - ft.Height - 6, ft.Width + 6, ft.Height + 3);
        ctx.FillRectangle(WarpBadgeBrush, box, 3);
        ctx.DrawText(ft, new Point(box.X + 3, box.Y + 1));
        return box;
    }

    private int MapAt(Point pos)
    {
        var layout = Layout;
        if (layout.Placements.Count == 0) return 0;
        var (cw, ch) = CellPixels();
        double cellW = cw * Zoom, cellH = ch * Zoom;
        if (cellW <= 0 || cellH <= 0) return 0;

        int cx = (int)Math.Floor(pos.X / cellW) + layout.MinX;
        int cy = (int)Math.Floor(pos.Y / cellH) + layout.MinY;
        foreach (var p in layout.Placements)
            if (p.CellX == cx && p.CellY == cy) return p.MapNum;
        return 0;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var props = e.GetCurrentPoint(this).Properties;
        if (!props.IsLeftButtonPressed) return;

        var mods = e.KeyModifiers;
        bool ctrl = mods.HasFlag(KeyModifiers.Control);
        bool alt = mods.HasFlag(KeyModifiers.Alt);
        bool shift = mods.HasFlag(KeyModifiers.Shift);

        // The map-canvas traverse chord is tested first: it contains Ctrl, and the pan below would
        // otherwise swallow it.
        if (ctrl && alt && shift)
        {
            Navigate(e);
            return;
        }

        if (ctrl)
        {
            _panMode = true;
            _panLastPos = e.GetPosition(VisualRoot as Visual);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        Navigate(e);
    }

    // The warp badge is its own target, and is tested before the cell it sits on: it is the only affordance
    // for connections the grid cannot draw, so clicking it opens them rather than opening the map under it.
    private void Navigate(PointerPressedEventArgs e)
    {
        var pos = e.GetPosition(this);
        foreach (var (mapNum, box) in _warpBadges)
        {
            if (!box.Inflate(3).Contains(pos)) continue;
            WarpsClicked?.Invoke(mapNum);
            e.Handled = true;
            return;
        }

        int map = MapAt(pos);
        if (map <= 0) return;
        MapClicked?.Invoke(map);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_panMode)
        {
            // Root-relative, so scrolling the content mid-drag does not corrupt the next delta.
            var pos = e.GetPosition(VisualRoot as Visual);
            PanRequested?.Invoke(new Vector(pos.X - _panLastPos.X, pos.Y - _panLastPos.Y));
            _panLastPos = pos;
            return;
        }

        int over = MapAt(e.GetPosition(this));
        if (over != _hoverMap)
        {
            _hoverMap = over;
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_panMode) return;
        _panMode = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hoverMap == 0) return;
        _hoverMap = 0;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var mods = e.KeyModifiers;

        if (mods.HasFlag(KeyModifiers.Control))
        {
            double target = Math.Clamp(Zoom * Math.Pow(1.15, e.Delta.Y), MinZoom, MaxZoom);
            ZoomRequested?.Invoke(target, e.GetPosition(this));
            e.Handled = true;
            return;
        }

        // Shift goes sideways, everything else goes down the page. Both are answered here rather than left
        // to the scroll viewer, so the pair behaves the same whichever platform the wheel came from.
        var step = mods.HasFlag(KeyModifiers.Shift)
            ? new Vector(e.Delta.Y + e.Delta.X, 0)
            : new Vector(0, e.Delta.Y);
        ScrollRequested?.Invoke(step * 60);
        e.Handled = true;
    }
}

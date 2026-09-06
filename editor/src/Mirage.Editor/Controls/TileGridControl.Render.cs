using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using Mirage.Editor.Models;
using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;
using Mirage.Shared;
using Mirage.Shared.Records;
using SkiaSharp;
using System.Globalization;
using Side = Mirage.Editor.Controls.AttributeBorderGeometry.Side;

namespace Mirage.Editor.Controls;

/// <summary>The render pass: the cached tile bitmap, the per-cell draw, the attribute/light/NPC
/// overlays, and the night approximation laid over them.</summary>
public sealed partial class TileGridControl : Control
{
    // ── Render ────────────────────────────────────────────────────────────────
    public override void Render(DrawingContext ctx)
    {
        double zoom = Zoom;
        double displayW = TotalCols * TileW * zoom;
        double displayH = TotalRows * TileH * zoom;

        // Snapshot all mode/layer/attribute state once so the hover-tint branches
        // never see an inconsistent mix of property reads mid-frame.
        var editorMode = EditorMode;
        var selectedAttribute = SelectedAttribute;
        var attrLayer = AttributeLayer;
        var tilesets = Tilesets;

        ctx.FillRectangle(Brushes.Black, new Rect(Bounds.Size));

        var bmp = TileBitmap; // the currently-selected sheet, used for stamp-ghost previews
        if (Map is not null && tilesets.Count > 0)
        {
            if (_tileCacheDirty)
                RebuildTileCache(tilesets);

            if (_tileCache is not null)
            {
                var src = new Rect(0, 0, TotalCols * TileW, TotalRows * TileH);
                var dst = new Rect(0, 0, displayW, displayH);
                ctx.DrawImage(_tileCache, src, dst);
            }
        }

        // Night preview: darken the edited map and glow its placed lights (use it to view AlwaysDark maps too).
        if (_nightPreviewMode && Map is not null)
            DrawNightOverlay(ctx, zoom);

        // Center-map border (drawn over the zoomed image)
        if (Map is not null)
        {
            double cx = OffsetCol * TileW * zoom;
            double cy = OffsetRow * TileH * zoom;
            double cw = GridCols * TileW * zoom;
            double ch = GridRows * TileH * zoom;
            ctx.DrawRectangle(null, CenterBorderPen, new Rect(cx, cy, cw, ch));
        }

        // Paste preview is active when in Place action and the clipboard's kind
        // matches the current editor mode.  When active, we suppress the regular
        // hover preview (stamp ghost / attribute brush footprint) and draw the
        // clipboard contents at the hover cell instead.
        bool pasteActive = Action == EditorAction.Place
            && ((ClipboardKind == ClipboardKind.Tile && editorMode == EditorMode.Tile)
             || (ClipboardKind == ClipboardKind.Attribute && editorMode == EditorMode.Attribute)
             || (ClipboardKind == ClipboardKind.Light && editorMode == EditorMode.Light));

        // Hover highlight — only in Place action, with no paste preview and not while placing an NPC (MODE 2
        // owns the hover cell then, drawing its own footprint brush below).
        if (Map is not null && _hoverX >= 0 && _hoverY >= 0
            && Action == EditorAction.Place && !pasteActive && !NpcPlacementActive)
        {
            if (_rightDown)
            {
                // Right-click erases exactly one tile — show a focused single-cell indicator
                // rather than the full stamp/brush footprint, regardless of mode.
                var cellRect = new Rect(
                    (_hoverX + OffsetCol) * TileW * zoom,
                    (_hoverY + OffsetRow) * TileH * zoom,
                    TileW * zoom, TileH * zoom);
                ctx.FillRectangle(HoverBlockedBrush, cellRect);
                ctx.DrawRectangle(null, HoverBlockPen, cellRect);
            }
            else if (editorMode == EditorMode.Tile)
            {
                // Ghost-stamp preview: draw each tile in the selection at 50 % opacity,
                // then overlay a per-cell tint that shows whether placement is allowed.
                var stamp = SelectedStamp;
                int stampCols = stamp?.Cols ?? 1;
                int stampRows = stamp?.Rows ?? 1;

                for (int dr = 0; dr < stampRows; dr++)
                {
                    for (int dc = 0; dc < stampCols; dc++)
                    {
                        int mx = _hoverX + dc, my = _hoverY + dr;
                        if (mx < 0 || mx >= GridCols || my < 0 || my >= GridRows) continue;

                        var cellRect = new Rect(
                            (mx + OffsetCol) * TileW * zoom,
                            (my + OffsetRow) * TileH * zoom,
                            TileW * zoom, TileH * zoom);

                        // Stamp tiles come from the selected sheet (bmp).
                        int tileIdx = stamp?.Indices[dc, dr] ?? 0;
                        if (tileIdx > 0 && bmp is not null)
                        {
                            using var _ = ctx.PushOpacity(0.5);
                            DrawTileFromSheet(ctx, bmp, tileIdx, cellRect);
                        }

                        var tile = Map!.Tile[mx, my];
                        bool allowed = LayerCell.IsEmpty(SelectedLayerCellOf(tile));
                        ctx.FillRectangle(allowed ? HoverBrush : HoverBlockedBrush, cellRect);
                        ctx.DrawRectangle(null, allowed ? HoverPen : HoverBlockPen, cellRect);
                    }
                }
            }
            else if (editorMode == EditorMode.Attribute)
            {
                // Brush footprint: per-cell tint showing where the attribute will land.
                int bw = Math.Max(1, AttributeBrushSizeX);
                int bh = Math.Max(1, AttributeBrushSizeY);
                for (int dy = 0; dy < bh; dy++)
                {
                    for (int dx = 0; dx < bw; dx++)
                    {
                        int mx = _hoverX + dx, my = _hoverY + dy;
                        if (mx < 0 || mx >= GridCols || my < 0 || my >= GridRows) continue;

                        var cellRect = new Rect(
                            (mx + OffsetCol) * TileW * zoom,
                            (my + OffsetRow) * TileH * zoom,
                            TileW * zoom, TileH * zoom);

                        var tile = Map!.Tile[mx, my];
                        bool allowed = AttrPreviewAllowed(tile, selectedAttribute, attrLayer);
                        ctx.FillRectangle(allowed ? HoverBrush : HoverBlockedBrush, cellRect);
                        ctx.DrawRectangle(null, allowed ? HoverPen : HoverBlockPen, cellRect);
                    }
                }
            }
            else // editorMode == EditorMode.Light
            {
                // A placed light occupies exactly one tile — highlight just the hovered cell.
                if (_hoverX >= 0 && _hoverX < GridCols && _hoverY >= 0 && _hoverY < GridRows)
                {
                    var cellRect = new Rect(
                        (_hoverX + OffsetCol) * TileW * zoom,
                        (_hoverY + OffsetRow) * TileH * zoom,
                        TileW * zoom, TileH * zoom);
                    ctx.FillRectangle(HoverBrush, cellRect);
                    ctx.DrawRectangle(null, HoverPen, cellRect);
                }
            }
        }

        // Delete action: preview the erase-brush footprint (the area that WILL be cleared) at the hover cell — an
        // erase tint over each brush cell, mirroring the Place attribute-brush footprint preview. Works in every
        // mode (tile-art / attribute / light are all cleared under the brush).
        if (Map is not null && _hoverX >= 0 && _hoverY >= 0 && Action == EditorAction.Delete && !NpcPlacementActive)
        {
            int bw = Math.Max(1, AttributeBrushSizeX);
            int bh = Math.Max(1, AttributeBrushSizeY);
            for (int dy = 0; dy < bh; dy++)
            {
                for (int dx = 0; dx < bw; dx++)
                {
                    int mx = _hoverX + dx, my = _hoverY + dy;
                    if (mx < 0 || mx >= GridCols || my < 0 || my >= GridRows) continue;
                    var cellRect = new Rect(
                        (mx + OffsetCol) * TileW * zoom,
                        (my + OffsetRow) * TileH * zoom,
                        TileW * zoom, TileH * zoom);
                    ctx.FillRectangle(HoverBlockedBrush, cellRect);
                    ctx.DrawRectangle(null, HoverBlockPen, cellRect);
                }
            }
        }

        // Select action: a single-cell hover highlight (where a marquee would begin) while NOT mid-drag — the
        // Place-style hover for Select; once a drag is active the selection-rect overlay below takes over.
        if (Map is not null && _hoverX >= 0 && _hoverY >= 0 && Action == EditorAction.Select && !_leftDown)
        {
            var cellRect = new Rect(
                (_hoverX + OffsetCol) * TileW * zoom,
                (_hoverY + OffsetRow) * TileH * zoom,
                TileW * zoom, TileH * zoom);
            ctx.FillRectangle(HoverBrush, cellRect);
            ctx.DrawRectangle(null, HoverPen, cellRect);
        }

        // NPC placement brush (MODE 2): while placing a row's NPC, draw its SxS footprint at
        // the hover cell — green when it forms a legal pin, red otherwise — over the cached tiles like the other
        // hover previews. Clamped to the visible grid so an edge-spilling footprint shows only its on-map part.
        if (NpcPlacementActive && Map is not null && _hoverX >= 0 && _hoverY >= 0)
        {
            bool ok = NpcPlacementValidAt?.Invoke(_hoverX, _hoverY) ?? false;
            var fill = ok ? NpcPlaceOkBrush : NpcPlaceBadBrush;
            var pen = ok ? NpcPlaceOkPen : NpcPlaceBadPen;
            int size = Math.Max(1, NpcPlacementSize);
            for (int dy = 0; dy < size; dy++)
            {
                for (int dx = 0; dx < size; dx++)
                {
                    int mx = _hoverX + dx, my = _hoverY + dy;
                    if (mx < 0 || mx >= GridCols || my < 0 || my >= GridRows) continue;
                    var cellRect = new Rect(
                        (mx + OffsetCol) * TileW * zoom,
                        (my + OffsetRow) * TileH * zoom,
                        TileW * zoom, TileH * zoom);
                    ctx.FillRectangle(fill, cellRect);
                    ctx.DrawRectangle(null, pen, cellRect);
                }
            }
        }

        // Logical-layer readout on the hover highlighter: the layer letter (G = Ground, F = Fringe; C = Canopy
        // once it exists) — plus the 1-based stack index in Tile mode, e.g. "G4" / "F1" — just left of the
        // anchor cell's top-left, so you can see which layer you're authoring at a glance. Attribute/Light modes
        // show only the plane letter (no per-stack index). Shown in Place, Delete, AND Select actions; in Select
        // it pins to the top-left of the selection rectangle (the "selector") once a marquee exists.
        if (Map is not null && _hoverX >= 0 && _hoverY >= 0 && !NpcPlacementActive
            && Action is EditorAction.Place or EditorAction.Delete or EditorAction.Select)
        {
            string label = editorMode == EditorMode.Tile
                ? $"{SelectedLayerType.ToString()[..1]}{SelectedLayerIndex}"
                : AttributeLayer.ToString()[..1];
            var ft = new FormattedText(label, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, Typeface.Default, LayerBadgeFontSize, Brushes.White);
            int anchorX = _hoverX, anchorY = _hoverY;
            if (Action == EditorAction.Select && SelectionRect is { } selr) { anchorX = selr.X1; anchorY = selr.Y1; }
            double cellLeft = (anchorX + OffsetCol) * TileW * zoom;
            double cellTop = (anchorY + OffsetRow) * TileH * zoom;
            double tx = cellLeft - ft.Width - LayerBadgeGap;
            double ty = cellTop;
            ctx.FillRectangle(LayerNumBgBrush, new Rect(tx - LayerBadgePadX, ty, ft.Width + LayerBadgePadX * 2, ft.Height));
            ctx.DrawText(ft, new Point(tx, ty));
        }

        // Selection-rect overlay (Select action) — bright white tint inside,
        // hard white border around the whole rect.
        if (Action == EditorAction.Select && Map is not null && SelectionRect is { } sr)
        {
            double sx = (sr.X1 + OffsetCol) * TileW * zoom;
            double sy = (sr.Y1 + OffsetRow) * TileH * zoom;
            double sw = (sr.X2 - sr.X1 + 1) * TileW * zoom;
            double sh = (sr.Y2 - sr.Y1 + 1) * TileH * zoom;
            var rectShape = new Rect(sx, sy, sw, sh);
            ctx.FillRectangle(SelectionFillBrush, rectShape);
            ctx.DrawRectangle(null, SelectionPen, rectShape);
        }

        // Paste preview (Place action + clipboard kind matches current mode).
        if (pasteActive && Map is not null && _hoverX >= 0 && _hoverY >= 0)
        {
            if (ClipboardKind == ClipboardKind.Tile && ClipboardTiles is { } tiles)
            {
                int w = tiles.GetLength(0), h = tiles.GetLength(1);
                for (int dy = 0; dy < h; dy++)
                {
                    for (int dx = 0; dx < w; dx++)
                    {
                        int packed = tiles[dx, dy];   // clipboard cells are packed LayerCell values
                        if (LayerCell.IsEmpty(packed)) continue;
                        int mx = _hoverX + dx, my = _hoverY + dy;
                        if (mx < 0 || mx >= GridCols || my < 0 || my >= GridRows) continue;

                        var cellRect = new Rect(
                            (mx + OffsetCol) * TileW * zoom,
                            (my + OffsetRow) * TileH * zoom,
                            TileW * zoom, TileH * zoom);

                        {
                            using var _ = ctx.PushOpacity(0.5);
                            DrawPackedLayer(ctx, tilesets, packed, cellRect);
                        }

                        var tile = Map!.Tile[mx, my];
                        bool allowed = LayerCell.IsEmpty(SelectedLayerCellOf(tile));
                        ctx.FillRectangle(allowed ? HoverBrush : HoverBlockedBrush, cellRect);
                        ctx.DrawRectangle(null, allowed ? HoverPen : HoverBlockPen, cellRect);
                    }
                }
            }
            else if (ClipboardKind == ClipboardKind.Attribute && ClipboardAttrs is { } attrs)
            {
                int w = attrs.GetLength(0), h = attrs.GetLength(1);
                for (int dy = 0; dy < h; dy++)
                {
                    for (int dx = 0; dx < w; dx++)
                    {
                        var src = attrs[dx, dy];
                        if (src.Type == TileType.Walkable) continue;
                        int mx = _hoverX + dx, my = _hoverY + dy;
                        if (mx < 0 || mx >= GridCols || my < 0 || my >= GridRows) continue;

                        var cellRect = new Rect(
                            (mx + OffsetCol) * TileW * zoom,
                            (my + OffsetRow) * TileH * zoom,
                            TileW * zoom, TileH * zoom);

                        var attrBrush = src.Type switch
                        {
                            TileType.Blocked => BlockedBrush,
                            TileType.Warp => WarpBrush,
                            TileType.Item => ItemBrush,
                            TileType.Key => KeyBrush,
                            TileType.KeyOpen => KeyOpenBrush,
                            TileType.NpcAvoid => NpcAvoidBrush,
                            _ => (IBrush?)null,
                        };
                        if (attrBrush is not null)
                        {
                            using var _ = ctx.PushOpacity(0.6);
                            ctx.FillRectangle(attrBrush, cellRect);
                        }

                        var tile = Map!.Tile[mx, my];
                        bool allowed = tile.Type == TileType.Walkable || tile.Type == src.Type;
                        ctx.FillRectangle(allowed ? HoverBrush : HoverBlockedBrush, cellRect);
                        ctx.DrawRectangle(null, allowed ? HoverPen : HoverBlockPen, cellRect);
                    }
                }
            }
            else if (ClipboardKind == ClipboardKind.Light && ClipboardLights is { } lights)
            {
                int w = lights.GetLength(0), h = lights.GetLength(1);
                for (int dy = 0; dy < h; dy++)
                {
                    for (int dx = 0; dx < w; dx++)
                    {
                        if (lights[dx, dy] is null) continue;   // a placed light always replaces — always allowed
                        int mx = _hoverX + dx, my = _hoverY + dy;
                        if (mx < 0 || mx >= GridCols || my < 0 || my >= GridRows) continue;
                        var cellRect = new Rect(
                            (mx + OffsetCol) * TileW * zoom,
                            (my + OffsetRow) * TileH * zoom,
                            TileW * zoom, TileH * zoom);
                        ctx.FillRectangle(HoverBrush, cellRect);
                        ctx.DrawRectangle(null, HoverPen, cellRect);
                    }
                }
            }
        }
    }

    private void RebuildTileCache(IReadOnlyList<Bitmap?> tilesets)
    {
        _tileCacheDirty = false;
        if (_rtbRetryCount >= 3) return;

        var targetSize = new PixelSize(TotalCols * TileW, TotalRows * TileH);

        try
        {
            if (_tileCache is null ||
                _tileCache.PixelSize.Width != targetSize.Width ||
                _tileCache.PixelSize.Height != targetSize.Height)
            {
                _tileCache?.Dispose();
                _tileCache = new RenderTargetBitmap(targetSize, new Vector(96, 96));
            }

            using (var rctx = _tileCache.CreateDrawingContext())
                RenderAllCells(rctx, tilesets);

            _rtbRetryCount = 0;
        }
        catch (Exception ex)
        {
            _tileCache?.Dispose();
            _tileCache = null;
            _rtbRetryCount++;
            _tileCacheDirty = true;
            TileCacheRenderFailed?.Invoke(ex);
        }
    }

    /// <summary>Resolves an NPC number to its footprint size (EffectiveSize, >= 1), set by the map-editor view
    /// from EditorDataService — drives the size-aware spawn-pin footprint overlay; null = 1.</summary>
    public Func<int, int>? NpcSizeLookup { get; set; }

    /// <summary>MODE 2: returns whether the row being placed forms a legal pin at (x,y) —
    /// colors the live placement brush green/red. Set by the map-editor view from the VM; null = never valid.</summary>
    public Func<int, int, bool>? NpcPlacementValidAt { get; set; }

    // Renders all 9 cells of the 3×3 grid into the RTB drawing context.
    private void RenderAllCells(DrawingContext ctx, IReadOnlyList<Bitmap?> tilesets)
    {
        Func<int, int> npcSize = NpcSizeLookup ?? (_ => 1);
        int centerAnimFrame = _animPreviewMode ? _animFrame : -1;   // -1 = static (draw all anim layers)
        bool doorPreview = _doorPreviewMode;
        bool showAttributes = EditorMode == EditorMode.Attribute;  // attribute tints/borders are mode-only
        bool showLights = EditorMode == EditorMode.Light;    // placed-light glyphs are mode-only
        WorldLayer attrLayer = AttributeLayer;   // which logical plane's attributes the overlay shows/edits

        // The visual stack the user is focused on — drawn full-strength while the other two dim on the center
        // cell.  Tile mode: the selected stack; Attribute/Light mode: the active logical plane's surface stack
        // (Ground → Ground[], Fringe → Fringe[]), so the Canopy and the other plane recede while you author.
        LayerType activeStack = EditorMode == EditorMode.Tile
            ? SelectedLayerType
            : (attrLayer == WorldLayer.Fringe ? LayerType.Fringe : LayerType.Ground);

        // Grid of (rowCell, colCell) → MapRecord?; center at (1,1).
        MapRecord?[,] cells = new MapRecord?[3, 3];
        cells[0, 0] = NeighborUpLeft;
        cells[0, 1] = NeighborUp;
        cells[0, 2] = NeighborUpRight;
        cells[1, 0] = NeighborLeft;
        cells[1, 1] = Map;
        cells[1, 2] = NeighborRight;
        cells[2, 0] = NeighborDownLeft;
        cells[2, 1] = NeighborDown;
        cells[2, 2] = NeighborDownRight;

        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                bool isCenter = row == 1 && col == 1;
                var pixOff = new Point(col * GridCols * TileW, row * GridRows * TileH);
                RenderCell(ctx, tilesets, cells[row, col], pixOff,
                    applyOverlay: !isCenter, animFrame: isCenter ? centerAnimFrame : -1,
                    doorPreview: doorPreview, showAttributes: showAttributes, showLights: showLights,
                    attrLayer: attrLayer, npcSize: npcSize, activeStack: activeStack, visible: LayerVisibility);

                // Arrivals are computed for the open map only, so they are drawn only on the center cell.
                // A neighbor's would need the same world-wide scan per cell, which the 3x3 does not pay for.
                if (isCenter && showAttributes && cells[row, col] is { } centerMap)
                    RenderInboundWarps(ctx, centerMap, pixOff, InboundWarps);
            }
        }
    }

    private static void RenderCell(DrawingContext ctx, IReadOnlyList<Bitmap?> tilesets,
        MapRecord? map, Point pixOff, bool applyOverlay, int animFrame, bool doorPreview, bool showAttributes, bool showLights,
        WorldLayer attrLayer, Func<int, int> npcSize, LayerType activeStack, LayerVisibility visible)
    {
        var cellRect = new Rect(pixOff.X, pixOff.Y, Cols(map) * TileW, Rows(map) * TileH);

        if (map is null)
        {
            ctx.FillRectangle(Brushes.Black, cellRect);
            return;
        }

        // Only the CENTER cell dims its non-active stacks; the neighbors already recede under NeighborOverlayBrush.
        LayerType? focusStack = applyOverlay ? null : activeStack;
        for (int x = 0; x < Cols(map); x++)
        {
            for (int y = 0; y < Rows(map); y++)
            {
                RenderTileAt(ctx, map, tilesets, x, y,
                    new Point(pixOff.X + x * TileW, pixOff.Y + y * TileH), animFrame, doorPreview, showAttributes, attrLayer, focusStack, visible);
            }
        }

        if (showAttributes)
        {
            RenderAttributeBorders(ctx, map, pixOff, attrLayer);
            RenderNpcSpawns(ctx, map, pixOff, npcSize, attrLayer);
        }

        if (showLights)
            RenderPlacedLights(ctx, map, pixOff, attrLayer);

        if (applyOverlay)
            ctx.FillRectangle(NeighborOverlayBrush, cellRect);
    }

    private static Pen? AttributeBorderPen(TileType type) => type switch
    {
        TileType.Blocked => BlockedBorderPen,
        TileType.Warp => WarpBorderPen,
        TileType.Item => ItemBorderPen,
        TileType.Key => KeyBorderPen,
        TileType.KeyOpen => KeyOpenBorderPen,
        TileType.NpcAvoid => NpcAvoidBorderPen,
        TileType.LayerRamp => LayerRampBorderPen,
        _ => null,
    };

    private static TileType NeighborAttr(MapRecord map, int x, int y, WorldLayer layer) =>
        x >= 0 && x < Cols(map) && y >= 0 && y < Rows(map) ? DisplayAttr(map.Tile[x, y], layer).Type : TileType.Walkable;

    // Fills a band inside each edge of a tile where the adjacent tile has a different attribute type (on the
    // active layer — a ramp counts on both), producing an outline around contiguous blocks rather than per-tile
    // boxes.  Each band sits inside the cell that owns it, so where two attributes meet, both keep their own
    // side of the shared grid line — see AttributeBorderGeometry.
    private static void RenderAttributeBorders(DrawingContext ctx, MapRecord map, Point pixOff, WorldLayer attrLayer)
    {
        for (int x = 0; x < Cols(map); x++)
        {
            for (int y = 0; y < Rows(map); y++)
            {
                var attr = DisplayAttr(map.Tile[x, y], attrLayer).Type;
                var pen = AttributeBorderPen(attr);
                if (pen?.Brush is not { } brush) continue;
                double t = pen.Thickness;

                double px = pixOff.X + x * TileW;
                double py = pixOff.Y + y * TileH;

                if (NeighborAttr(map, x, y - 1, attrLayer) != attr) FillBand(ctx, brush, px, py, Side.Top, t);
                if (NeighborAttr(map, x, y + 1, attrLayer) != attr) FillBand(ctx, brush, px, py, Side.Bottom, t);
                if (NeighborAttr(map, x - 1, y, attrLayer) != attr) FillBand(ctx, brush, px, py, Side.Left, t);
                if (NeighborAttr(map, x + 1, y, attrLayer) != attr) FillBand(ctx, brush, px, py, Side.Right, t);
            }
        }
    }

    private static void FillBand(DrawingContext ctx, IBrush brush, double px, double py, Side side, double thickness) =>
        ctx.FillRectangle(brush, AttributeBorderGeometry.Band(px, py, TileW, TileH, side, thickness));

    // Draws each placed light as a colored bulb dot plus a faint reach ring (radius in tiles → px).
    // Visible only in Light Sources mode; mirrors RenderAttributeBorders' per-cell overlay pattern.  Filtered to
    // the ACTIVE logical layer so ground and fringe lights (which may share a tile) don't overlap-clutter — you
    // see (and edit) only the plane you're authoring, matching the Ground/Fringe selector.
    private static void RenderPlacedLights(DrawingContext ctx, MapRecord map, Point pixOff, WorldLayer activeLayer)
    {
        foreach (var pl in map.Lights)
        {
            if (pl.Layer != activeLayer) continue;
            if (pl.X < 0 || pl.X >= Cols(map) || pl.Y < 0 || pl.Y >= Rows(map)) continue;
            uint rgb = pl.Light.Rgb;
            var color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            var center = new Point(pixOff.X + (pl.X + 0.5) * TileW, pixOff.Y + (pl.Y + 0.5) * TileH);

            // Reach ring in the light's color — pronounced so the radius reads at a glance.
            double reach = pl.Light.Radius * TileW;
            ctx.DrawEllipse(null, new Pen(new SolidColorBrush(color, 0.7), 2), center, reach, reach);

            // Simple bulb dot: the light's color with a flat black outline.
            double r = TileW * 0.32;
            ctx.DrawEllipse(new SolidColorBrush(color), LightGlyphOutlinePen, center, r, r);
        }
    }

    // Draws each fixed NPC-spawn pin as a teal badge with its runtime post number, so the author sees where
    // pinned NPCs spawn. Visible in Attribute mode; mirrors RenderPlacedLights' per-cell overlay. Filtered to the
    // ACTIVE logical layer so a Ground pin and a Fringe pin (which may share a tile) don't overlap-clutter — you
    // see (and edit) only the plane you're authoring, matching the Ground/Fringe selector.
    private static void RenderNpcSpawns(DrawingContext ctx, MapRecord map, Point pixOff, Func<int, int> npcSize, WorldLayer activeLayer)
    {
        for (int i = 0; i < map.Npcs.Count; i++)
        {
            var e = map.Npcs[i];
            if (!e.HasPin || e.PinLayer != activeLayer) continue;
            int px = e.PinX!.Value, py = e.PinY!.Value;
            if (px < 0 || px >= Cols(map) || py < 0 || py >= Rows(map)) continue;
            // Full SxS footprint: the whole reserved body, clamped to the cell, then the
            // post-number badge on the anchor tile.
            int size = Math.Max(1, npcSize(e.Npc));
            int fw = Math.Min(size, Cols(map) - px), fh = Math.Min(size, Rows(map) - py);
            ctx.DrawRectangle(NpcSpawnFootprintBrush, NpcSpawnFootprintPen,
                new Rect(pixOff.X + px * TileW, pixOff.Y + py * TileH, fw * TileW, fh * TileH));
            double cx = pixOff.X + (px + 0.5) * TileW;
            double cy = pixOff.Y + (py + 0.5) * TileH;
            double r = TileW * 0.34;
            ctx.DrawEllipse(NpcSpawnMarkerBrush, NpcSpawnMarkerPen, new Point(cx, cy), r, r);
            var ft = new FormattedText((i + 1).ToString(), CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, Typeface.Default, TileH * 0.42, Brushes.White);
            ctx.DrawText(ft, new Point(cx - ft.Width / 2, cy - ft.Height / 2));
        }
    }

    // Draws each tile another map warps INTO as a violet badge carrying the source map's number, so the
    // receiving map shows what opens onto it — a warp is authored wholly on the departing side, and without
    // this the destination tile looks like any other. Several maps arriving on one tile compound into one
    // badge showing "+N": a tile is one doorway however many doors open onto it, and the hover read-out is
    // where the rest of them are named. Already filtered to the active plane by the caller.
    private static void RenderInboundWarps(DrawingContext ctx, MapRecord map, Point pixOff,
        IReadOnlyList<InboundWarp> inbound)
    {
        foreach (var w in inbound)
        {
            if (w.X < 0 || w.X >= Cols(map) || w.Y < 0 || w.Y >= Rows(map)) continue;
            var tileRect = new Rect(pixOff.X + w.X * TileW, pixOff.Y + w.Y * TileH, TileW, TileH);
            ctx.FillRectangle(InboundWarpTileBrush, tileRect);

            double cx = pixOff.X + (w.X + 0.5) * TileW;
            double cy = pixOff.Y + (w.Y + 0.5) * TileH;
            double r = TileW * 0.34;
            ctx.DrawEllipse(InboundWarpMarkerBrush, InboundWarpMarkerPen, new Point(cx, cy), r, r);

            string label = w.SourceMaps.Count == 1
                ? w.SourceMaps[0].ToString(CultureInfo.CurrentCulture)
                : $"+{w.SourceMaps.Count}";
            var ft = new FormattedText(label, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, Typeface.Default, TileH * 0.40, Brushes.White);
            ctx.DrawText(ft, new Point(cx - ft.Width / 2, cy - ft.Height / 2));
        }
    }

    // Faithful night pass: builds a light map (navy ambient + additive two-layer warm halos) and composites it
    // over the already-drawn tiles with a real MULTIPLY blend via Skia — the same pipeline the client uses,
    // sharing LightModel so the editor preview and the game match. No-ops if the Skia backend is unavailable.
    private void DrawNightOverlay(DrawingContext ctx, double zoom)
    {
        var map = Map;
        if (map is null) return;
        double x0 = OffsetCol * TileW * zoom, y0 = OffsetRow * TileH * zoom;
        var rect = new Rect(x0, y0, Cols(map) * TileW * zoom, Rows(map) * TileH * zoom);

        var lights = new List<NightLight>(map.Lights.Count);
        // Only the ACTIVE plane's lights, matching the markers and the Ground/Fringe selector, in every mode.
        // A convenience view of the plane being authored — the client owns accuracy, and occludes per layer.
        foreach (var pl in map.Lights)
        {
            if (pl.Layer != AttributeLayer) continue;
            if (pl.X < 0 || pl.X >= Cols(map) || pl.Y < 0 || pl.Y >= Rows(map)) continue;
            lights.Add(new NightLight(
                (float)(x0 + (pl.X + 0.5) * TileW * zoom),
                (float)(y0 + (pl.Y + 0.5) * TileH * zoom),
                (float)(pl.Light.Radius * TileW * zoom),
                pl.Light.Rgb, Math.Clamp(pl.Light.Intensity, 0f, 1f), pl.Light.Flicker, pl.Id.GetHashCode()));
        }
        float t = (Environment.TickCount64 - _nightEpochMs) / 1000f;
        ctx.Custom(new NightLightOp(rect, lights, t));
    }

    private readonly record struct NightLight(
        float Cx, float Cy, float RadiusPx, uint Rgb, float Intensity, FlickerStyle Flicker, int Seed);

    // Custom Skia draw op: reproduces GameplayScreen's DrawLightMap/DrawLightHalo — a navy light map with
    // additive two-layer warm halos, MULTIPLIED over the already-drawn tiles inside the center-map rect.
    private sealed class NightLightOp : ICustomDrawOperation
    {
        private readonly Rect _rect;
        private readonly List<NightLight> _lights;
        private readonly float _t;
        public NightLightOp(Rect rect, List<NightLight> lights, float t)
        {
            _rect = rect;
            _lights = lights;
            _t = t;
        }

        public Rect Bounds => _rect;
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (leaseFeature is null) return;   // non-Skia backend: skip gracefully
            using var lease = leaseFeature.Lease();
            var canvas = lease.SkCanvas;
            var skRect = new SKRect((float)_rect.X, (float)_rect.Y, (float)_rect.Right, (float)_rect.Bottom);

            canvas.Save();
            canvas.ClipRect(skRect);
            // Build the light map in an isolated layer, then composite it onto the tiles with MULTIPLY.
            using (var multiply = new SKPaint { BlendMode = SKBlendMode.Multiply })
            {
                canvas.SaveLayer(multiply);
                using (var navy = new SKPaint
                {
                    Color = new SKColor(LightModel.NightAmbientR, LightModel.NightAmbientG, LightModel.NightAmbientB)
                })
                {
                    canvas.DrawRect(skRect, navy);   // full-night ambient
                }

                foreach (var l in _lights)
                {
                    if (l.RadiusPx < 0.5f) continue;
                    float lit = l.Intensity;   // EffectiveDarkness = 1 at full night
                    DrawHalo(canvas, l.Cx, l.Cy, l.RadiusPx, outer: true, Tint(l.Rgb, LightModel.OuterDimFactor * lit));
                    float f = LightModel.FlickerFor(l.Flicker, _t, l.Seed);
                    float innerR = l.RadiusPx * LightModel.InnerRadiusFactor * MathF.Max(f, LightModel.MinInnerSizeFactor);
                    DrawHalo(canvas, l.Cx, l.Cy, innerR, outer: false, Tint(l.Rgb, lit * f));
                }
                canvas.Restore();   // MULTIPLY the light map onto the tiles
            }
            canvas.Restore();
        }

        private static SKColor Tint(uint rgb, float k) => new(
            (byte)Math.Clamp(((rgb >> 16) & 0xFF) * k, 0f, 255f),
            (byte)Math.Clamp(((rgb >> 8) & 0xFF) * k, 0f, 255f),
            (byte)Math.Clamp((rgb & 0xFF) * k, 0f, 255f));

        // Additive halo = a radial gradient with the falloff baked into the color (tint x falloff), drawn Plus
        // so the light map accumulates like the client's LightAccumBlend.
        private static void DrawHalo(SKCanvas c, float cx, float cy, float r, bool outer, SKColor tint)
        {
            if (r < 0.5f) return;
            const int n = 16;
            var colors = new SKColor[n];
            var pos = new float[n];
            for (int i = 0; i < n; i++)
            {
                float d = i / (n - 1f);
                float a = outer ? LightModel.OuterFalloff(d) : LightModel.InnerFalloff(d);
                colors[i] = new SKColor((byte)(tint.Red * a), (byte)(tint.Green * a), (byte)(tint.Blue * a));
                pos[i] = d;
            }
            using var shader = SKShader.CreateRadialGradient(
                new SKPoint(cx, cy), r, colors, pos, SKShaderTileMode.Clamp);
            using var paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.Plus, IsAntialias = true };
            c.DrawCircle(cx, cy, r, paint);
        }
    }

    private static void RenderTileAt(DrawingContext ctx, MapRecord map, IReadOnlyList<Bitmap?> tilesets,
        int x, int y, Point pixPt, int animFrame, bool doorPreview, bool showAttributes, WorldLayer attrLayer,
        LayerType? focusStack, LayerVisibility visible)
    {
        var tile = map.Tile[x, y];
        var dstRect = new Rect(pixPt.X, pixPt.Y, TileW, TileH);

        ctx.FillRectangle(Brushes.Black, dstRect);
        // Door-open preview (toggle): a Key tile hides its topmost populated Ground layer (the door
        // graphic), matching the client's runtime reveal.  Off → the door renders closed for authoring.
        // Anim-flagged layers hide on the editor's anim-preview off phase.
        int hideGround = doorPreview && tile.Type == TileType.Key
            ? LayerCell.TopmostNonEmptyIndex(tile.Ground) : -1;
        // On the center cell (focusStack != null) every stack but the active one dims, so the layer being
        // authored stands out; neighbors pass null and render at full strength (they dim as a whole cell).
        bool Dim(LayerType s) => focusStack is { } f && f != s;
        DrawLayerStack(ctx, tilesets, tile.Ground, dstRect, animFrame, hideGround,
            visible.VisibleBits(LayerType.Ground), Dim(LayerType.Ground));
        DrawLayerStack(ctx, tilesets, tile.Fringe, dstRect, animFrame, -1,
            visible.VisibleBits(LayerType.Fringe), Dim(LayerType.Fringe));
        DrawLayerStack(ctx, tilesets, tile.Canopy, dstRect, animFrame, -1,
            visible.VisibleBits(LayerType.Canopy), Dim(LayerType.Canopy));   // topmost visual stack (over everything)

        // Attribute tints are only drawn in Attribute mode, for the ACTIVE logical layer (a ramp shows on both).
        if (showAttributes)
        {
            var (dispType, dispDir) = DisplayAttr(tile, attrLayer);
            IBrush? attrBrush = dispType switch
            {
                TileType.Blocked => BlockedBrush,
                TileType.Warp => WarpBrush,
                TileType.Item => ItemBrush,
                TileType.Key => KeyBrush,
                TileType.KeyOpen => KeyOpenBrush,
                TileType.NpcAvoid => NpcAvoidBrush,
                TileType.LayerRamp => RampOverlay.IsMixedBlock(map, x, y) ? LayerRampMixedBrush : LayerRampBrush,
                _ => null,
            };
            if (attrBrush is not null)
                ctx.FillRectangle(attrBrush, dstRect);
            if (dispType == TileType.LayerRamp)
            {
                DrawRampArrow(ctx, dstRect, LiftDirection((Direction)dispDir));
                // Broken ramp block (no ground mount point → connects nothing): red frame over the fill + arrow.
                if (RampOverlay.IsInvalidBlock(map, x, y))
                {
                    ctx.DrawRectangle(null, LayerRampInvalidPen,
                        new Rect(dstRect.X + 2, dstRect.Y + 2, dstRect.Width - 4, dstRect.Height - 4));
                }
            }
        }

        ctx.DrawRectangle(null, GridLinePen, dstRect);
    }

    // The attribute to VISUALIZE for a tile on the given active layer.  A LayerRamp occupies BOTH planes, so it
    // shows on either layer; otherwise Ground shows the inline Type and Fringe shows FringeAttr?.Type (the fringe
    // plane is walkable by default, so a missing FringeAttr reads as Walkable — no tint).  Data1 rides along for
    // the ramp's mount-direction arrow.
    private static (TileType Type, short Data1) DisplayAttr(TileRecord t, WorldLayer layer)
    {
        if (t.FringeAttr is { Type: TileType.LayerRamp } ramp)
            return (TileType.LayerRamp, (short)ramp.RampGroundSide);
        return layer == WorldLayer.Fringe
            ? (t.FringeAttr?.Type ?? TileType.Walkable, (short)(t.FringeAttr?.RampGroundSide ?? default))
            : (t.Type, (short)t.RampGroundSide);
    }

    // Ramp-block color-coding (mixed / invalid) lives in the testable RampOverlay helper.

    // The "up-ramp" lift direction the arrow points — opposite the stored ground-side mount (Data1) — so the
    // glyph faces the way that carries you UP onto the fringe, for easy visual reference.
    private static Direction LiftDirection(Direction groundSide) => groundSide switch
    {
        Direction.Up => Direction.Down,
        Direction.Down => Direction.Up,
        Direction.Left => Direction.Right,
        _ => Direction.Left,
    };

    // Draws a filled triangle inside `rect` pointing toward `dir` — the ramp's up-ramp/lift direction (the way it
    // climbs onto the fringe) — so the author sees the ramp's orientation at a glance.
    private static void DrawRampArrow(DrawingContext ctx, Rect rect, Direction dir)
    {
        double cx = rect.X + rect.Width / 2, cy = rect.Y + rect.Height / 2;
        double h = rect.Width * 0.28;   // half-extent of the arrowhead
        Point tip, baseA, baseB;
        switch (dir)
        {
            case Direction.Up:
                tip = new(cx, cy - h);
                baseA = new(cx - h, cy + h);
                baseB = new(cx + h, cy + h);
                break;
            case Direction.Down:
                tip = new(cx, cy + h);
                baseA = new(cx - h, cy - h);
                baseB = new(cx + h, cy - h);
                break;
            case Direction.Left:
                tip = new(cx - h, cy);
                baseA = new(cx + h, cy - h);
                baseB = new(cx + h, cy + h);
                break;
            default:
                tip = new(cx + h, cy);
                baseA = new(cx - h, cy - h);
                baseB = new(cx - h, cy + h);
                break;  // Right
        }
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(tip, isFilled: true);
            g.LineTo(baseA);
            g.LineTo(baseB);
            g.EndFigure(true);
        }
        ctx.DrawGeometry(LayerRampArrowBrush, LayerRampArrowPen, geo);
    }

    // Green/red hint for the Attribute brush footprint — mirrors the VM's placement gate (MapEditorViewModel):
    // a LayerRamp needs a fully-clear tile; any existing ramp blocks everything else (both planes); otherwise the
    // active layer's current attribute must be Walkable/empty or the same tool. (Pinned-NPC coverage is enforced
    // at placement, not previewed here — the tint reflects attribute occupancy only.)
    private static bool AttrPreviewAllowed(TileRecord tile, TileType sel, WorldLayer layer)
    {
        if (sel == TileType.LayerRamp)
            return tile.Type == TileType.Walkable && tile.FringeAttr is null;
        if (tile.FringeAttr is { Type: TileType.LayerRamp }) return false;
        var cur = layer == WorldLayer.Fringe ? (tile.FringeAttr?.Type ?? TileType.Walkable) : tile.Type;
        return cur == TileType.Walkable || cur == sel;
    }

    // Draws every non-empty cell of a layer stack, skipping the layer at hideIndex (door-open reveal,
    // -1 = none).  animFrame < 0 = not animating (all anim layers drawn statically); animFrame >= 0 = show
    // only the current frame's anim layer (LayerCell.VisibleAnimIndex), hiding the other anim layers.
    // visibleBits is one bit per layer index, clear when the author has hidden that layer.
    private static void DrawLayerStack(DrawingContext ctx, IReadOnlyList<Bitmap?> tilesets, ReadOnlySpan<int> layers,
        Rect dst, int animFrame, int hideIndex, int visibleBits, bool dim = false)
    {
        int visibleAnim = animFrame >= 0 ? LayerCell.VisibleAnimIndex(layers, animFrame) : 0;

        // dim => fade the whole stack (non-active layer on the center cell) so the active stack reads crisply.
        // The opacity push is scoped around the loop rather than around a local function, because a span
        // cannot be captured by one.
        using var fade = dim ? ctx.PushOpacity(DimmedStackOpacity) : default;

        for (int k = 0; k < layers.Length; k++)
        {
            int p = layers[k];
            if (LayerCell.IsEmpty(p)) continue;
            if (k == hideIndex) continue;
            if ((visibleBits >> k & 1) == 0) continue;
            if (animFrame >= 0 && LayerCell.Anim(p) && k != visibleAnim) continue;
            DrawPackedLayer(ctx, tilesets, p, dst);
        }
    }

    // Resolves a packed LayerCell to its sheet + tile and blits it.
    private static void DrawPackedLayer(DrawingContext ctx, IReadOnlyList<Bitmap?> tilesets, int packed, Rect dst)
    {
        int sheet = LayerCell.Sheet(packed);
        if (sheet < 0 || sheet >= tilesets.Count) return;
        DrawTileFromSheet(ctx, tilesets[sheet], LayerCell.Tile(packed), dst);
    }

    // Blits a 1-based tile index from a single sheet bitmap (column count derived from its width).
    private static void DrawTileFromSheet(DrawingContext ctx, Bitmap? bmp, int tileIndex, Rect dst)
    {
        if (bmp is null || tileIndex <= 0) return;
        int cols = Math.Max(1, (int)(bmp.Size.Width / TileW));
        int idx = tileIndex - 1;
        int srcCol = idx % cols;
        int srcRow = idx / cols;
        ctx.DrawImage(bmp, new Rect(srcCol * TileW, srcRow * TileH, TileW, TileH), dst);
    }
}

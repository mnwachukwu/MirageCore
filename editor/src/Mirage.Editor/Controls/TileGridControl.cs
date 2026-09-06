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

namespace Mirage.Editor.Controls;

public sealed partial class TileGridControl : Control
{
    // ── Layout constants ──────────────────────────────────────────────────────
    public const int TileW = 32;
    public const int TileH = 32;
    // The open map's size in tiles, and the 3x3 observable area built from it. Every cell of the grid is
    // this size — a map only links to maps its own size — so one figure lays out the whole control.
    private static int Cols(MapRecord? map) => map?.Width ?? Constants.DefaultMapWidth;
    private static int Rows(MapRecord? map) => map?.Height ?? Constants.DefaultMapHeight;
    private int GridCols => Cols(Map);
    private int GridRows => Rows(Map);

    // 3×3 grid: active map in center cell (1,1)
    private int TotalCols => GridCols * 3;
    private int TotalRows => GridRows * 3;
    public int OffsetCol => GridCols;      // tile-column offset of center map start in the 3×3 grid
    public int OffsetRow => GridRows;      // tile-row offset of center map start in the 3×3 grid

    // Lowest (1-based) layer index — Alt+wheel clamps to this at the bottom.
    private const int FirstLayerIndex = 1;
    // Hover layer-number badge: fixed-size readout (unscaled by zoom) left of the hovered cell.
    private const double LayerBadgeFontSize = 12;
    private const double LayerBadgeGap = 4;   // px between the badge and the cell's left edge
    private const double LayerBadgePadX = 2;  // horizontal padding inside the badge backplate

    // ── StyledProperties — center map ─────────────────────────────────────────
    public static readonly StyledProperty<MapRecord?> MapProperty =
        AvaloniaProperty.Register<TileGridControl, MapRecord?>(nameof(Map));

    public static readonly StyledProperty<Bitmap?> TileBitmapProperty =
        AvaloniaProperty.Register<TileGridControl, Bitmap?>(nameof(TileBitmap));

    public static readonly StyledProperty<EditorMode> EditorModeProperty =
        AvaloniaProperty.Register<TileGridControl, EditorMode>(nameof(EditorMode));

    public static readonly StyledProperty<TileStamp?> SelectedStampProperty =
        AvaloniaProperty.Register<TileGridControl, TileStamp?>(nameof(SelectedStamp));

    public static readonly StyledProperty<int> AttributeBrushSizeXProperty =
        AvaloniaProperty.Register<TileGridControl, int>(nameof(AttributeBrushSizeX), defaultValue: 1);
    public static readonly StyledProperty<int> AttributeBrushSizeYProperty =
        AvaloniaProperty.Register<TileGridControl, int>(nameof(AttributeBrushSizeY), defaultValue: 1);

    public static readonly StyledProperty<TileType> SelectedAttributeProperty =
        AvaloniaProperty.Register<TileGridControl, TileType>(nameof(SelectedAttribute));

    // The active logical layer for Attribute mode (uniform two-plane world): the overlay tints/borders show
    // THIS layer's attributes — Ground = inline Type, Fringe = FringeAttr — so toggling it swaps which plane
    // you see and author. A LayerRamp occupies both planes and shows on either.
    public static readonly StyledProperty<WorldLayer> AttributeLayerProperty =
        AvaloniaProperty.Register<TileGridControl, WorldLayer>(nameof(AttributeLayer));

    // Which art layers this canvas draws. View state: it changes what is on screen and nothing about the
    // map. The PNG export deliberately does not read it — an export is of the map, not of the view.
    public static readonly StyledProperty<LayerVisibility> LayerVisibilityProperty =
        AvaloniaProperty.Register<TileGridControl, LayerVisibility>(nameof(LayerVisibility));

    // MODE 2 transient NPC placement: when active, the grid draws a live footprint brush at
    // the hover cell (green/red per NpcPlacementValidAt) and routes clicks to place/cancel instead of painting.
    public static readonly StyledProperty<bool> NpcPlacementActiveProperty =
        AvaloniaProperty.Register<TileGridControl, bool>(nameof(NpcPlacementActive));
    public static readonly StyledProperty<int> NpcPlacementSizeProperty =
        AvaloniaProperty.Register<TileGridControl, int>(nameof(NpcPlacementSize), defaultValue: 1);

    public static readonly StyledProperty<LayerType> SelectedLayerTypeProperty =
        AvaloniaProperty.Register<TileGridControl, LayerType>(nameof(SelectedLayerType));
    public static readonly StyledProperty<int> SelectedLayerIndexProperty =
        AvaloniaProperty.Register<TileGridControl, int>(nameof(SelectedLayerIndex), defaultValue: 1);

    // All loaded tile sheets, indexed by sheet number (gaps may be null); lets the canvas render
    // layers from any tileset and resolve clipboard cells back to their source sheet.
    public static readonly StyledProperty<IReadOnlyList<Bitmap?>> TilesetsProperty =
        AvaloniaProperty.Register<TileGridControl, IReadOnlyList<Bitmap?>>(nameof(Tilesets), defaultValue: []);

    /// <summary>Tiles on the open map that another map's warp lands on, already filtered to the active
    /// plane. Marked in Attribute mode so the receiving map shows what opens onto it.</summary>
    public static readonly StyledProperty<IReadOnlyList<InboundWarp>> InboundWarpsProperty =
        AvaloniaProperty.Register<TileGridControl, IReadOnlyList<InboundWarp>>(
            nameof(InboundWarps), defaultValue: []);

    public static readonly StyledProperty<EditorAction> ActionProperty =
        AvaloniaProperty.Register<TileGridControl, EditorAction>(nameof(Action));

    public static readonly StyledProperty<ClipboardKind> ClipboardKindProperty =
        AvaloniaProperty.Register<TileGridControl, ClipboardKind>(nameof(ClipboardKind));

    public static readonly StyledProperty<int[,]?> ClipboardTilesProperty =
        AvaloniaProperty.Register<TileGridControl, int[,]?>(nameof(ClipboardTiles));

    public static readonly StyledProperty<TileAttr[,]?> ClipboardAttrsProperty =
        AvaloniaProperty.Register<TileGridControl, TileAttr[,]?>(nameof(ClipboardAttrs));
    public static readonly StyledProperty<LightSpec?[,]?> ClipboardLightsProperty =
        AvaloniaProperty.Register<TileGridControl, LightSpec?[,]?>(nameof(ClipboardLights));

    public static readonly StyledProperty<SelectionBox?> SelectionRectProperty =
        AvaloniaProperty.Register<TileGridControl, SelectionBox?>(nameof(SelectionRect));

    // ── StyledProperties — neighbor maps ──────────────────────────────────────
    public static readonly StyledProperty<MapRecord?> NeighborUpProperty =
        AvaloniaProperty.Register<TileGridControl, MapRecord?>(nameof(NeighborUp));
    public static readonly StyledProperty<MapRecord?> NeighborDownProperty =
        AvaloniaProperty.Register<TileGridControl, MapRecord?>(nameof(NeighborDown));
    public static readonly StyledProperty<MapRecord?> NeighborLeftProperty =
        AvaloniaProperty.Register<TileGridControl, MapRecord?>(nameof(NeighborLeft));
    public static readonly StyledProperty<MapRecord?> NeighborRightProperty =
        AvaloniaProperty.Register<TileGridControl, MapRecord?>(nameof(NeighborRight));
    public static readonly StyledProperty<MapRecord?> NeighborUpLeftProperty =
        AvaloniaProperty.Register<TileGridControl, MapRecord?>(nameof(NeighborUpLeft));
    public static readonly StyledProperty<MapRecord?> NeighborUpRightProperty =
        AvaloniaProperty.Register<TileGridControl, MapRecord?>(nameof(NeighborUpRight));
    public static readonly StyledProperty<MapRecord?> NeighborDownLeftProperty =
        AvaloniaProperty.Register<TileGridControl, MapRecord?>(nameof(NeighborDownLeft));
    public static readonly StyledProperty<MapRecord?> NeighborDownRightProperty =
        AvaloniaProperty.Register<TileGridControl, MapRecord?>(nameof(NeighborDownRight));

    // ── StyledProperty — zoom ────────────────────────────────────────────────
    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<TileGridControl, double>(nameof(Zoom), defaultValue: 1.0);

    // ── Property accessors ────────────────────────────────────────────────────
    public MapRecord? Map
    {
        get => GetValue(MapProperty);
        set => SetValue(MapProperty, value);
    }
    public Bitmap? TileBitmap
    {
        get => GetValue(TileBitmapProperty);
        set => SetValue(TileBitmapProperty, value);
    }
    public EditorMode EditorMode
    {
        get => GetValue(EditorModeProperty);
        set => SetValue(EditorModeProperty, value);
    }
    public TileStamp? SelectedStamp
    {
        get => GetValue(SelectedStampProperty);
        set => SetValue(SelectedStampProperty, value);
    }
    public int AttributeBrushSizeX
    {
        get => GetValue(AttributeBrushSizeXProperty);
        set => SetValue(AttributeBrushSizeXProperty, value);
    }
    public int AttributeBrushSizeY
    {
        get => GetValue(AttributeBrushSizeYProperty);
        set => SetValue(AttributeBrushSizeYProperty, value);
    }
    public TileType SelectedAttribute
    {
        get => GetValue(SelectedAttributeProperty);
        set => SetValue(SelectedAttributeProperty, value);
    }
    public WorldLayer AttributeLayer
    {
        get => GetValue(AttributeLayerProperty);
        set => SetValue(AttributeLayerProperty, value);
    }
    public LayerVisibility LayerVisibility
    {
        get => GetValue(LayerVisibilityProperty);
        set => SetValue(LayerVisibilityProperty, value);
    }
    public bool NpcPlacementActive
    {
        get => GetValue(NpcPlacementActiveProperty);
        set => SetValue(NpcPlacementActiveProperty, value);
    }
    public int NpcPlacementSize
    {
        get => GetValue(NpcPlacementSizeProperty);
        set => SetValue(NpcPlacementSizeProperty, value);
    }
    public LayerType SelectedLayerType
    {
        get => GetValue(SelectedLayerTypeProperty);
        set => SetValue(SelectedLayerTypeProperty, value);
    }
    public int SelectedLayerIndex
    {
        get => GetValue(SelectedLayerIndexProperty);
        set => SetValue(SelectedLayerIndexProperty, value);
    }
    public IReadOnlyList<Bitmap?> Tilesets
    {
        get => GetValue(TilesetsProperty);
        set => SetValue(TilesetsProperty, value);
    }
    public IReadOnlyList<InboundWarp> InboundWarps
    {
        get => GetValue(InboundWarpsProperty);
        set => SetValue(InboundWarpsProperty, value);
    }
    public EditorAction Action
    {
        get => GetValue(ActionProperty);
        set => SetValue(ActionProperty, value);
    }
    public ClipboardKind ClipboardKind
    {
        get => GetValue(ClipboardKindProperty);
        set => SetValue(ClipboardKindProperty, value);
    }
    public int[,]? ClipboardTiles
    {
        get => GetValue(ClipboardTilesProperty);
        set => SetValue(ClipboardTilesProperty, value);
    }
    public TileAttr[,]? ClipboardAttrs
    {
        get => GetValue(ClipboardAttrsProperty);
        set => SetValue(ClipboardAttrsProperty, value);
    }
    public LightSpec?[,]? ClipboardLights
    {
        get => GetValue(ClipboardLightsProperty);
        set => SetValue(ClipboardLightsProperty, value);
    }
    public SelectionBox? SelectionRect
    {
        get => GetValue(SelectionRectProperty);
        set => SetValue(SelectionRectProperty, value);
    }

    public MapRecord? NeighborUp
    {
        get => GetValue(NeighborUpProperty);
        set => SetValue(NeighborUpProperty, value);
    }
    public MapRecord? NeighborDown
    {
        get => GetValue(NeighborDownProperty);
        set => SetValue(NeighborDownProperty, value);
    }
    public MapRecord? NeighborLeft
    {
        get => GetValue(NeighborLeftProperty);
        set => SetValue(NeighborLeftProperty, value);
    }
    public MapRecord? NeighborRight
    {
        get => GetValue(NeighborRightProperty);
        set => SetValue(NeighborRightProperty, value);
    }
    public MapRecord? NeighborUpLeft
    {
        get => GetValue(NeighborUpLeftProperty);
        set => SetValue(NeighborUpLeftProperty, value);
    }
    public MapRecord? NeighborUpRight
    {
        get => GetValue(NeighborUpRightProperty);
        set => SetValue(NeighborUpRightProperty, value);
    }
    public MapRecord? NeighborDownLeft
    {
        get => GetValue(NeighborDownLeftProperty);
        set => SetValue(NeighborDownLeftProperty, value);
    }
    public MapRecord? NeighborDownRight
    {
        get => GetValue(NeighborDownRightProperty);
        set => SetValue(NeighborDownRightProperty, value);
    }
    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    // ── Events ────────────────────────────────────────────────────────────────
    public event Action<TileClick>? TileClicked;
    public event Action<NeighborCell>? NeighborMapClicked;
    public event Action<(short MapId, ushort X, ushort Y)>? WarpDestinationClicked;
    public event Action? NavigateBackRequested;
    public event Action? NavigateForwardRequested;
    public event Action<(int X, int Y)>? TileRightClicked;
    // Delete action: left click/drag erases the mode-dependent content under the brush at each tile (see DeleteAt).
    public event Action<(int X, int Y)>? TileDeleteRequested;
    // Fired at press time when a Tile-mode click lands on an occupied selected layer — opens the anim editor.
    public event Action<(int X, int Y)>? AnimEditRequested;
    public event Action<SelectionDrag>? SelectionChanged;
    public event Action<int, int>? HoverChanged;
    // MODE 2: a placement click landed on active-map tile (X,Y); a cancel gesture (right-click).
    public event Action<(int X, int Y)>? NpcPlacementClicked;
    public event Action? NpcPlacementCancelRequested;
    public event Action? DragBegan;
    public event Action? DragEnded;
    public event Action<double>? ZoomRequested;
    public event Action<Vector>? PanRequested;

    // ── Static brushes / pens ─────────────────────────────────────────────────
    private static readonly IBrush GridLineBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
    private static readonly IBrush HoverBrush = new SolidColorBrush(Color.FromArgb(80, 0, 200, 255));
    private static readonly IBrush HoverBlockedBrush = new SolidColorBrush(Color.FromArgb(100, 255, 60, 60));
    private static readonly IBrush BlockedBrush = new SolidColorBrush(Color.FromArgb(120, 255, 0, 0));
    private static readonly IBrush WarpBrush = new SolidColorBrush(Color.FromArgb(100, 0, 0, 255));
    private static readonly IBrush ItemBrush = new SolidColorBrush(Color.FromArgb(100, 255, 255, 0));
    private static readonly IBrush KeyBrush = new SolidColorBrush(Color.FromArgb(100, 255, 140, 0));
    private static readonly IBrush KeyOpenBrush = new SolidColorBrush(Color.FromArgb(100, 160, 32, 240));
    private static readonly IBrush NpcAvoidBrush = new SolidColorBrush(Color.FromArgb(100, 100, 100, 100));
    // LayerRamp — the sole connector between planes; shown on BOTH layers (it occupies both). Distinct green so it
    // never reads as a wall, plus a mount-direction arrow glyph (Data1) drawn over the fill.
    private static readonly IBrush LayerRampBrush = new SolidColorBrush(Color.FromArgb(115, 40, 210, 130));
    // Amber variant: this ramp sits in a MIXED-direction contiguous block (a deliberate hump/staircase, or a
    // misplacement) — flagged by color so the author notices the block isn't one clean orientation.
    private static readonly IBrush LayerRampMixedBrush = new SolidColorBrush(Color.FromArgb(125, 240, 155, 30));
    private static readonly IBrush LayerRampArrowBrush = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255));
    // Fixed NPC-spawn pin badge (Attribute mode): a teal disc with the slot number, distinct from tile tints.
    private static readonly IBrush NpcSpawnMarkerBrush = new SolidColorBrush(Color.FromArgb(210, 20, 150, 90));
    private static readonly Pen NpcSpawnMarkerPen = new(new SolidColorBrush(Color.FromArgb(230, 0, 0, 0)), 1.0);
    // Size-aware spawn footprint: a translucent teal fill + outline over the NPC's SxS body.
    private static readonly IBrush NpcSpawnFootprintBrush = new SolidColorBrush(Color.FromArgb(60, 20, 150, 90));

    // Arrival markers: violet, matching the World Preview's warp badge, and distinct from the teal NPC pin
    // and the colored light bulb so three overlapping conventions stay readable on one tile.
    private static readonly IBrush InboundWarpMarkerBrush = new SolidColorBrush(Color.FromArgb(215, 120, 60, 165));
    private static readonly Pen InboundWarpMarkerPen = new(new SolidColorBrush(Color.FromArgb(230, 0, 0, 0)), 1.0);
    private static readonly IBrush InboundWarpTileBrush = new SolidColorBrush(Color.FromArgb(58, 150, 90, 200));
    private static readonly Pen NpcSpawnFootprintPen = new(new SolidColorBrush(Color.FromArgb(180, 20, 150, 90)), 1.0);
    // MODE 2 placement brush: green when the hovered footprint is a legal pin, red otherwise.
    private static readonly IBrush NpcPlaceOkBrush = new SolidColorBrush(Color.FromArgb(90, 40, 220, 90));
    private static readonly Pen NpcPlaceOkPen = new(new SolidColorBrush(Color.FromArgb(230, 40, 220, 90)), 1.5);
    private static readonly IBrush NpcPlaceBadBrush = new SolidColorBrush(Color.FromArgb(90, 230, 50, 50));
    private static readonly Pen NpcPlaceBadPen = new(new SolidColorBrush(Color.FromArgb(230, 230, 50, 50)), 1.5);
    private static readonly IBrush NeighborOverlayBrush = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0));
    private static readonly IBrush LayerNumBgBrush = new SolidColorBrush(Color.FromArgb(190, 0, 0, 0));
    // Opacity applied to the non-active visual stacks on the CENTER cell so the layer you're authoring stands
    // out (Tile mode: the selected stack; Attribute/Light mode: the active logical plane's surface stack).
    // Faded rather than black-overlaid so an active stack drawn BENEATH a dimmed one still reads (a black
    // overlay would darken it too).
    private const double DimmedStackOpacity = 0.30;

    private static readonly Pen GridLinePen = new(GridLineBrush, 0.5);
    private static readonly Pen HoverPen = new(Brushes.Cyan, 1.5);
    private static readonly Pen HoverBlockPen = new(Brushes.Red, 1.5);
    private static readonly Pen CenterBorderPen = new(new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)), 2.0);

    // Bright-white selection rectangle (Select action).
    private static readonly IBrush SelectionFillBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));
    private static readonly Pen SelectionPen = new(Brushes.White, 1.5);

    // Attribute border pens — solid, darker than each fill; drawn on perimeter of contiguous blocks.
    private static readonly Pen BlockedBorderPen = new(new SolidColorBrush(Color.FromArgb(255, 180, 0, 0)), 1.5);
    private static readonly Pen WarpBorderPen = new(new SolidColorBrush(Color.FromArgb(255, 0, 0, 200)), 1.5);
    private static readonly Pen ItemBorderPen = new(new SolidColorBrush(Color.FromArgb(255, 160, 160, 0)), 1.5);
    private static readonly Pen KeyBorderPen = new(new SolidColorBrush(Color.FromArgb(255, 180, 80, 0)), 1.5);
    private static readonly Pen KeyOpenBorderPen = new(new SolidColorBrush(Color.FromArgb(255, 100, 0, 170)), 1.5);
    private static readonly Pen NpcAvoidBorderPen = new(new SolidColorBrush(Color.FromArgb(255, 50, 50, 50)), 1.5);
    private static readonly Pen LayerRampBorderPen = new(new SolidColorBrush(Color.FromArgb(255, 20, 150, 90)), 1.5);
    // A ramp block that can't be mounted from the ground (connects nothing) gets a hard red frame ON TOP of its
    // fill + arrow — "this is broken", distinct from the amber "mixed, double-check it" flag.
    private static readonly Pen LayerRampInvalidPen = new(new SolidColorBrush(Color.FromArgb(255, 235, 30, 30)), 2.0);
    // Dark outline on the ramp mount-direction arrow so the white glyph reads on any tile art beneath it.
    private static readonly Pen LayerRampArrowPen = new(new SolidColorBrush(Color.FromArgb(235, 0, 40, 20)), 1.2);
    // Placed-light glyph (Light Sources mode): the colored bulb dot gets this dark outline; the reach ring
    // is drawn per-light in the light's own color.
    private static readonly Pen LightGlyphOutlinePen = new(new SolidColorBrush(Color.FromArgb(220, 0, 0, 0)), 1.0);

    // ── Instance state ────────────────────────────────────────────────────────
    // Hover in map-space (0-based, center map coords). -1 = off the active map.
    private int _hoverX = -1;
    private int _hoverY = -1;

    private bool _leftDown;
    private bool _rightDown;
    private bool _altDown;
    private bool _retainDown; // Ctrl+Shift at press time → paste-with-retain
    private bool _pressWasPaste; // press fired a paste; suppress subsequent drag-paint even if clipboard cleared
    private int _lastDragX = -1;
    private int _lastDragY = -1;
    private bool _panMode;
    private Point _panLastPos;

    // Selection-drag origin (Select action). -1 = no drag in progress.
    private int _selStartX = -1;
    private int _selStartY = -1;

    // RTB cache at natural resolution (no zoom applied here).
    private RenderTargetBitmap? _tileCache;
    private bool _tileCacheDirty = true;
    private int _rtbRetryCount;

    /// <summary>Raised when the cached map render throws. The map keeps working and the next edit retries;
    /// this exists so the failure reaches the status bar rather than being swallowed.</summary>
    public event Action<Exception>? TileCacheRenderFailed;

    private bool _animPreviewMode;
    private int _animFrame;   // advances one per preview tick; animated stacks pick their frame from it
    private bool _doorPreviewMode;  // when on, Key tiles render in their open state (topmost ground hidden)
    private bool _nightPreviewMode; // when on, overlays a night approximation (also forced on for AlwaysDark maps)

    // ── Static constructor ────────────────────────────────────────────────────
    static TileGridControl()
    {
        // Tile-data or mode changes → mark cache dirty + re-render.
        AffectsRender<TileGridControl>(
            MapProperty, TileBitmapProperty,
            EditorModeProperty, SelectedStampProperty, AttributeBrushSizeXProperty, AttributeBrushSizeYProperty,
            SelectedAttributeProperty, AttributeLayerProperty, LayerVisibilityProperty, SelectedLayerTypeProperty, SelectedLayerIndexProperty, TilesetsProperty,
            ActionProperty, ClipboardKindProperty, ClipboardTilesProperty, ClipboardAttrsProperty, ClipboardLightsProperty, SelectionRectProperty,
            NpcPlacementActiveProperty, NpcPlacementSizeProperty,
            NeighborUpProperty, NeighborDownProperty,
            NeighborLeftProperty, NeighborRightProperty,
            NeighborUpLeftProperty, NeighborUpRightProperty,
            NeighborDownLeftProperty, NeighborDownRightProperty);

        // Neighbor or main-map changes → dirty the RTB.
        foreach (var p in new StyledProperty<MapRecord?>[]
        {
            MapProperty, NeighborUpProperty, NeighborDownProperty,
            NeighborLeftProperty, NeighborRightProperty,
            NeighborUpLeftProperty, NeighborUpRightProperty,
            NeighborDownLeftProperty, NeighborDownRightProperty,
        })
        {
            p.Changed.AddClassHandler<TileGridControl>((c, _) => { c._tileCacheDirty = true; c._rtbRetryCount = 0; });
        }

        TileBitmapProperty.Changed.AddClassHandler<TileGridControl>(
            (c, _) => { c._tileCacheDirty = true; c._rtbRetryCount = 0; });

        // New/removed tilesets change the rendered map → rebuild the RTB.
        TilesetsProperty.Changed.AddClassHandler<TileGridControl>(
            (c, _) => { c._tileCacheDirty = true; c._rtbRetryCount = 0; });

        // Editor mode toggles attribute highlighting in the cached render → rebuild.
        EditorModeProperty.Changed.AddClassHandler<TileGridControl>(
            (c, _) => { c._tileCacheDirty = true; c._rtbRetryCount = 0; });

        // Switching the active attribute layer changes which plane's attributes the cached overlay shows → rebuild.
        AttributeLayerProperty.Changed.AddClassHandler<TileGridControl>(
            (c, _) => { c._tileCacheDirty = true; c._rtbRetryCount = 0; });

        // Hiding a layer changes what the cached render holds, not merely what is drawn over it → rebuild.
        LayerVisibilityProperty.Changed.AddClassHandler<TileGridControl>(
            (c, _) => { c._tileCacheDirty = true; c._rtbRetryCount = 0; });

        // Arrival markers are drawn into the cached render alongside the attribute overlay → rebuild.
        InboundWarpsProperty.Changed.AddClassHandler<TileGridControl>(
            (c, _) => { c._tileCacheDirty = true; c._rtbRetryCount = 0; c.InvalidateVisual(); });

        // The active VISUAL stack (Tile mode) drives the non-active-stack dim baked into the cached render → rebuild.
        SelectedLayerTypeProperty.Changed.AddClassHandler<TileGridControl>(
            (c, _) => { c._tileCacheDirty = true; c._rtbRetryCount = 0; });

        // Zoom changes layout and display but NOT the RTB content.
        ZoomProperty.Changed.AddClassHandler<TileGridControl>((c, _) =>
        {
            c.InvalidateMeasure();
            c.InvalidateVisual();
        });
    }

    // ── Public invalidation API ───────────────────────────────────────────────
    public void InvalidateTileAt(int x, int y)
    {
        _tileCacheDirty = true;
        // An edit is a fresh opportunity, not a continuation of whatever failed before. The retry budget
        // exists to stop a doomed rebuild running on every frame; letting it carry across user actions is
        // what turns a transient RTB failure into a map that never draws again.
        _rtbRetryCount = 0;
        InvalidateVisual();
    }

    public void InvalidateMapRender()
    {
        _tileCacheDirty = true;
        _rtbRetryCount = 0;
        InvalidateVisual();
    }

    public void SetAnimPreview(bool on)
    {
        _animPreviewMode = on;
        _animFrame = 0;
        _tileCacheDirty = true;
        InvalidateVisual();
    }

    // Toggles the door-open preview: Key tiles render with their topmost Ground layer hidden.
    public void SetDoorPreview(bool on)
    {
        _doorPreviewMode = on;
        _tileCacheDirty = true;
        InvalidateVisual();
    }

    // Toggles the night preview. The Skia pass draws on top of the cached tiles, so no cache rebuild is needed.
    public void SetNightPreview(bool on)
    {
        _nightPreviewMode = on;
        RefreshNightAnimation();
        InvalidateVisual();
    }

    // Runs a low-frequency timer while the night preview toggle is on so the flicker animates; idle otherwise.
    private readonly long _nightEpochMs = Environment.TickCount64;
    private DispatcherTimer? _nightTimer;
    private void RefreshNightAnimation()
    {
        if (_nightPreviewMode)
        {
            _nightTimer ??= new DispatcherTimer(
                TimeSpan.FromMilliseconds(66), DispatcherPriority.Render, (_, _) => InvalidateVisual());
            if (!_nightTimer.IsEnabled) _nightTimer.Start();
        }
        else
        {
            _nightTimer?.Stop();
        }
    }

    // If the control is torn down while the night preview is still on, RefreshNightAnimation never runs its
    // "off" branch — so stop and drop the flicker timer here so it can't linger past the control's lifetime.
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _nightTimer?.Stop();
        _nightTimer = null;
    }

    public void TickAnimFrame()
    {
        if (!_animPreviewMode) return;
        _animFrame++;
        _tileCacheDirty = true;
        InvalidateVisual();
    }

    // The packed LayerCell value of the currently-selected layer (type + index) on a tile.
    private int SelectedLayerCellOf(TileRecord t)
    {
        var layers = SelectedLayerType switch
        {
            LayerType.Ground => t.Ground,
            LayerType.Fringe => t.Fringe,
            _ => t.Canopy,
        };
        int i = SelectedLayerIndex - 1;
        return i >= 0 && i < layers.Length ? layers[i] : LayerCell.Empty;
    }

    // ── Layout ────────────────────────────────────────────────────────────────
    protected override Size MeasureOverride(Size _) =>
        new(TotalCols * TileW * Zoom, TotalRows * TileH * Zoom);
}

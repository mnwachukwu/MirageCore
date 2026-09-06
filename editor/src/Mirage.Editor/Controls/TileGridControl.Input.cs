using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using Mirage.Editor.ViewModels;
using Mirage.Shared;
using Mirage.Shared.Records;
using SkiaSharp;
using System.Globalization;

namespace Mirage.Editor.Controls;

/// <summary>Pointer handling: paint and erase drags, rectangular selection, warp-destination and
/// NPC-placement clicks, neighbor-cell navigation, and wheel zoom.</summary>
public sealed partial class TileGridControl : Control
{
    // ── Pointer events ────────────────────────────────────────────────────────
    // Converts raw display position to map-space coordinates (center-map origin).
    private (int x, int y) ToMapCoords(Point pos)
    {
        double zoom = Zoom;
        int x = (int)(pos.X / (TileW * zoom)) - OffsetCol;
        int y = (int)(pos.Y / (TileH * zoom)) - OffsetRow;
        return (x, y);
    }

    private bool InActiveMap(int x, int y) =>
        x >= 0 && x < GridCols && y >= 0 && y < GridRows;

    // Resolves a pointer position to one of the 8 surrounding neighbor cells in
    // the 3×3 grid. Returns false for the center cell (already active) and for
    // anywhere outside the rendered block.
    private bool TryGetNeighborCell(Point pos, out NeighborCell cell)
    {
        double zoom = Zoom;
        int col = (int)(pos.X / (TileW * zoom)) / GridCols;
        int row = (int)(pos.Y / (TileH * zoom)) / GridRows;
        if (col < 0 || col > 2 || row < 0 || row > 2 || (col == 1 && row == 1))
        {
            cell = default;
            return false;
        }
        cell = (col, row) switch
        {
            (0, 0) => NeighborCell.UpLeft,
            (1, 0) => NeighborCell.Up,
            (2, 0) => NeighborCell.UpRight,
            (0, 1) => NeighborCell.Left,
            (2, 1) => NeighborCell.Right,
            (0, 2) => NeighborCell.DownLeft,
            (1, 2) => NeighborCell.Down,
            _ => NeighborCell.DownRight,
        };
        return true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_panMode)
        {
            // Use root-relative coordinates so that scrolling the content doesn't
            // shift the control and corrupt the delta on the next event.
            var root = VisualRoot as Visual;
            var pos = e.GetPosition(root);
            var delta = new Vector(pos.X - _panLastPos.X, pos.Y - _panLastPos.Y);
            _panLastPos = pos;
            PanRequested?.Invoke(delta);
            return;
        }

        var (x, y) = ToMapCoords(e.GetPosition(this));
        bool inActive = InActiveMap(x, y);

        if (x != _hoverX || y != _hoverY)
        {
            _hoverX = inActive ? x : -1;
            _hoverY = inActive ? y : -1;
            InvalidateVisual();
            HoverChanged?.Invoke(inActive ? x : -1, inActive ? y : -1);
        }

        if (!inActive) return;

        // A held button with no drag origin means the press never established one: the branches that
        // navigate, place a pin or paste all finish their work and return before the origin is set.
        // Seed it from where the cursor is now rather than interpolating from -1,-1, which is off-map
        // and clips to a streak running out of the 0,0 corner. Only the anomalous case reaches this —
        // an ordinary press sets the origin to the tile it pressed.
        if ((_leftDown || _rightDown) && _lastDragX < 0)
        {
            _lastDragX = x;
            _lastDragY = y;
            return;
        }

        // Selection-drag update — feeds the VM a normalized rect via the SelectionChanged event.
        if (Action == EditorAction.Select && _leftDown && _selStartX >= 0
            && (x != _lastDragX || y != _lastDragY))
        {
            _lastDragX = x;
            _lastDragY = y;
            SelectionChanged?.Invoke(new SelectionDrag(_selStartX, _selStartY, x, y, DragPhase.Move));
            return;
        }

        // Delete-action drag-erase — Bresenham so a quick drag erases a continuous swath (each cell runs the
        // brush-sized erase via DeleteAt).  Runs before the place-paint branch, which it would otherwise share.
        if (Action == EditorAction.Delete && _leftDown && (x != _lastDragX || y != _lastDragY))
        {
            int dFromX = _lastDragX, dFromY = _lastDragY;
            _lastDragX = x;
            _lastDragY = y;
            bool skipStart = true;
            foreach (var (cx, cy) in BresenhamLine(dFromX, dFromY, x, y))
            {
                if (skipStart)
                {
                    skipStart = false;
                    continue;
                }
                if (InActiveMap(cx, cy)) TileDeleteRequested?.Invoke((cx, cy));
            }
            return;
        }

        // Place-action drag-paint — Bresenham along the path so quick drags don't
        // leave holes.  Suppressed when a paste preview is active (so a single press
        // doesn't paste at every cell the cursor crosses) OR when the press itself
        // fired a paste (so a drag-after-paste doesn't fall back to stamp-paint).
        bool pasteActive = Action == EditorAction.Place && ClipboardKind != ClipboardKind.None;
        if (_leftDown && !pasteActive && !_pressWasPaste && (x != _lastDragX || y != _lastDragY))
        {
            int fromX = _lastDragX, fromY = _lastDragY;
            _lastDragX = x;
            _lastDragY = y;
            bool skipStart = true;
            foreach (var (cx, cy) in BresenhamLine(fromX, fromY, x, y))
            {
                if (skipStart)
                {
                    skipStart = false;
                    continue;
                }
                if (InActiveMap(cx, cy))
                    TileClicked?.Invoke(new TileClick(cx, cy, _altDown, _retainDown, Dragging: true));
            }
        }
        else if (_rightDown && (x != _lastDragX || y != _lastDragY))
        {
            int fromX = _lastDragX, fromY = _lastDragY;
            _lastDragX = x;
            _lastDragY = y;
            bool skipStart = true;
            foreach (var (cx, cy) in BresenhamLine(fromX, fromY, x, y))
            {
                if (skipStart)
                {
                    skipStart = false;
                    continue;
                }
                if (InActiveMap(cx, cy))
                    TileRightClicked?.Invoke((cx, cy));
            }
        }
    }

    /// <summary>Drops the tile cursor when the pointer leaves the grid.
    ///
    /// <para>The position decides, not the event. Pressing Alt puts the window into access-key mode,
    /// which raises a synthetic exit while the pointer has not moved at all; an exit whose coordinates
    /// are still inside the control is one of those and is ignored.</para></summary>
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (new Rect(Bounds.Size).Contains(e.GetPosition(this))) return;
        if (_hoverX != -1 || _hoverY != -1)
        {
            _hoverX = -1;
            _hoverY = -1;
            InvalidateVisual();
            HoverChanged?.Invoke(-1, -1);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var props = e.GetCurrentPoint(this).Properties;

        // Mouse side buttons (XButton1 = back, XButton2 = forward) = nav history.
        // Handle before any other state mutation so a side-button press never
        // bleeds into paint or pan logic.
        if (props.IsXButton1Pressed)
        {
            NavigateBackRequested?.Invoke();
            e.Handled = true;
            return;
        }
        if (props.IsXButton2Pressed)
        {
            NavigateForwardRequested?.Invoke();
            e.Handled = true;
            return;
        }

        _leftDown = props.IsLeftButtonPressed;
        _rightDown = props.IsRightButtonPressed;
        _altDown = (e.KeyModifiers & KeyModifiers.Alt) != 0;
        _retainDown = (e.KeyModifiers & KeyModifiers.Control) != 0
                   && (e.KeyModifiers & KeyModifiers.Shift) != 0;

        // MODE 2 NPC placement: while active, the grid owns clicks — a left-click on an
        // active-map tile places the pin, a right-click cancels. Preempts paint/pan/select/nav entirely so a
        // stray drag can't paint underneath the placement.
        if (NpcPlacementActive)
        {
            // Placing a pin ends placement synchronously (PlacingNpcRow = -1), which flips NpcPlacementActive OFF
            // while the button is still down — so clear the drag flags here or the next PointerMoved would
            // Bresenham drag-PAINT the selected attribute/tile from the stale drag origin to the placement tile.
            if (_rightDown)
            {
                _leftDown = _rightDown = false;
                NpcPlacementCancelRequested?.Invoke();
                e.Handled = true;
                return;
            }
            if (_leftDown)
            {
                var (px, py) = ToMapCoords(e.GetPosition(this));
                _leftDown = _rightDown = false;
                if (InActiveMap(px, py)) NpcPlacementClicked?.Invoke((px, py));
                e.Handled = true;
                return;
            }
        }

        // Ctrl+Alt+Shift + left-click on a neighbor cell = switch to that map.
        // Must run before the pan and paste-retain branches because the modifier
        // set is a superset of both.
        //
        // Both navigation branches clear the drag flags first, for the reason the NPC-placement branch
        // above does: loading a map is synchronous and leaves the button DOWN over a different map, so
        // the next PointerMoved would Bresenham drag-PAINT from a drag origin belonging to nothing —
        // `-1,-1` after a release, which clips to a streak running from 0,0 to wherever the cursor got to.
        if (_leftDown && _altDown && _retainDown
            && TryGetNeighborCell(e.GetPosition(this), out var cell))
        {
            _leftDown = _rightDown = false;
            NeighborMapClicked?.Invoke(cell);
            e.Handled = true;
            return;
        }

        // Ctrl+Alt+Shift + left-click on an active-map warp tile = jump to the
        // warp's destination map. Same gating as neighbor-cell navigation; runs
        // here so the pan / paste-retain branches don't swallow the click.
        if (_leftDown && _altDown && _retainDown && Map is not null)
        {
            var (wx, wy) = ToMapCoords(e.GetPosition(this));
            if (InActiveMap(wx, wy) && Map.Tile[wx, wy].Type == TileType.Warp)
            {
                var t = Map.Tile[wx, wy];
                _leftDown = _rightDown = false;
                WarpDestinationClicked?.Invoke((t.WarpMap, t.WarpX, t.WarpY));
                e.Handled = true;
                return;
            }
        }

        // Ctrl + left-click = canvas pan; capture pointer so drag works outside the control.
        // Ctrl+Shift is reserved for paste-with-retain — let it fall through to the click path.
        if (_leftDown
            && (e.KeyModifiers & KeyModifiers.Control) != 0
            && (e.KeyModifiers & KeyModifiers.Shift) == 0)
        {
            _panMode = true;
            _panLastPos = e.GetPosition(VisualRoot as Visual);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        var (x, y) = ToMapCoords(e.GetPosition(this));
        if (!InActiveMap(x, y)) return;

        // Select action: LMB starts/refreshes a selection rect.  RMB is a no-op.
        // Do NOT invoke DragBegan/DragEnded — those open an undo batch and a selection
        // drag mutates nothing.
        if (Action == EditorAction.Select)
        {
            if (_leftDown)
            {
                _selStartX = x;
                _selStartY = y;
                _lastDragX = x;
                _lastDragY = y;
                SelectionChanged?.Invoke(new SelectionDrag(x, y, x, y, DragPhase.Begin));
            }
            return;
        }

        // Delete action: LMB brush-erases (drag continues in OnPointerMoved); RMB is the usual single-tile erase.
        // Opens an undo batch (DragBegan) so a whole erase stroke undoes as one — must run before the Tile-mode
        // anim-edit check so deleting an occupied cell erases it instead of opening the animation editor.
        if (Action == EditorAction.Delete)
        {
            if (_leftDown || _rightDown)
            {
                _lastDragX = x;
                _lastDragY = y;
                DragBegan?.Invoke();
                if (_leftDown) TileDeleteRequested?.Invoke((x, y));
                else TileRightClicked?.Invoke((x, y));
            }
            return;
        }

        // Tile mode: a press on a cell whose selected layer is already filled opens the animation editor
        // instead of painting. Occupied cells never paint (the stamp loop skips them), so deciding here --
        // before DragBegan/paint/paste -- blocks the whole stamp AND leaves the clipboard untouched, and a
        // drag that starts on an empty cell never triggers it (this fires only on the initial press).
        if (_leftDown && !_retainDown && EditorMode == EditorMode.Tile && Map is not null
            && !LayerCell.IsEmpty(SelectedLayerCellOf(Map.Tile[x, y])))
        {
            _leftDown = false;                       // stop OnPointerMoved from drag-painting
            AnimEditRequested?.Invoke((x, y));
            e.Handled = true;
            return;
        }

        DragBegan?.Invoke();

        if (_leftDown)
        {
            // Mark that this press will paste so a continued drag doesn't fall back
            // to stamp-paint after the paste clears the clipboard.
            _pressWasPaste = ClipboardKind != ClipboardKind.None
                && ((ClipboardKind == ClipboardKind.Tile && EditorMode == EditorMode.Tile)
                 || (ClipboardKind == ClipboardKind.Attribute && EditorMode == EditorMode.Attribute));
            _lastDragX = x;
            _lastDragY = y;
            TileClicked?.Invoke(new TileClick(x, y, _altDown, _retainDown));
        }
        else if (_rightDown)
        {
            _lastDragX = x;
            _lastDragY = y;
            TileRightClicked?.Invoke((x, y));
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_panMode)
        {
            _panMode = false;
            e.Pointer.Capture(null);
            _leftDown = false;
            _altDown = false;
            _retainDown = false;
            return;
        }
        bool wasSelectionDrag = Action == EditorAction.Select && _selStartX >= 0;
        if (wasSelectionDrag)
        {
            int ex = _lastDragX >= 0 ? _lastDragX : _selStartX;
            int ey = _lastDragY >= 0 ? _lastDragY : _selStartY;
            SelectionChanged?.Invoke(new SelectionDrag(_selStartX, _selStartY, ex, ey, DragPhase.End));
        }
        _selStartX = -1;
        _selStartY = -1;
        _leftDown = false;
        _rightDown = false;
        _altDown = false;
        _retainDown = false;
        _pressWasPaste = false;
        _lastDragX = -1;
        _lastDragY = -1;
        InvalidateVisual();
        if (!wasSelectionDrag) DragEnded?.Invoke();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        bool alt = (e.KeyModifiers & KeyModifiers.Alt) != 0;
        bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        int step = e.Delta.Y > 0 ? 1 : -1;

        // Ctrl+Alt + wheel steps the LOGICAL layer, clamped so it can't wrap (mirroring Alt+wheel's clamp).
        // In Tile mode it steps the tile-art stack you paint (Ground/Fringe[/Canopy]); in Attribute/Light mode
        // the logical plane (Ground/Fringe). Checked BEFORE the Alt- and Ctrl-only branches, since Ctrl+Alt
        // sets both modifier bits.
        if (ctrl && alt)
        {
            e.Handled = true;
            if (EditorMode == EditorMode.Tile)
            {
                SelectedLayerType = (LayerType)Math.Clamp(
                    (int)SelectedLayerType + step, 0, Enum.GetValues<LayerType>().Length - 1);
            }
            else
            {
                AttributeLayer = (WorldLayer)Math.Clamp(
                    (int)AttributeLayer + step, 0, Enum.GetValues<WorldLayer>().Length - 1);
            }

            return;
        }

        // Alt + wheel steps the selected layer index within the current layer type, clamped to the
        // valid range (no wrap-around).
        if (alt)
        {
            e.Handled = true;
            int max = SelectedLayerType switch
            {
                LayerType.Ground => Constants.MaxGroundLayers,
                LayerType.Fringe => Constants.MaxFringeLayers,
                _ => Constants.MaxCanopyLayers,
            };
            SelectedLayerIndex = Math.Clamp(SelectedLayerIndex + step, FirstLayerIndex, max);
            return;
        }
        if (ctrl)
        {
            e.Handled = true; // prevent ScrollViewer from also scrolling
            double factor = Math.Pow(1.1, e.Delta.Y);
            ZoomRequested?.Invoke(Math.Clamp(Zoom * factor, 0.125, 4.0));
            return;
        }
        base.OnPointerWheelChanged(e);
    }
}

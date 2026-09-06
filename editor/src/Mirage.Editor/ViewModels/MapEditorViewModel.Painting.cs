using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Controls;
using Mirage.Editor.Localization;
using Mirage.Editor.Models;
using Mirage.Editor.Services;
using Mirage.Shared;
using Mirage.Shared.Records;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
namespace Mirage.Editor.ViewModels;

/// <summary>The paint entry points — left click, right click, delete and drag-selection phases —
/// and what each editor mode does with a tile: place or erase a layer cell, stamp an attribute,
/// apply a warp/item/key dialog, or navigate to a clicked neighbor map. Also the description text
/// the tools panel shows for the selected attribute.</summary>
public sealed partial class MapEditorViewModel : ObservableObject
{
    /// <summary>Stamps the selected tiles onto the selected layer, anchored at (x, y).
    ///
    /// <para>Its own method because the hidden-layer prompt replays exactly this: the tile it refused is
    /// laid once the author has said to turn the layer back on.</para></summary>
    private void PaintTileAt(int x, int y)
    {
        if (SelectedMap is null) return;
        // A tile placed on a layer you cannot see lands correctly and is invisible, which reads as the
        // editor ignoring the click. Nothing is painted until the author says what they meant.
        if (BlockedByHiddenLayer(x, y)) return;

        var map = SelectedMap.Record;
        // Stamp all cells in the selection; skip out-of-bounds and occupied layer slots.
        var stamp = SelectedStamp;
        int stampCols = stamp?.Cols ?? 1;
        int stampRows = stamp?.Rows ?? 1;

        for (int dr = 0; dr < stampRows; dr++)
        {
            for (int dc = 0; dc < stampCols; dc++)
            {
                int tileIdx = stamp?.Indices[dc, dr] ?? 0;
                if (tileIdx == 0) continue;

                int tx = x + dc, ty = y + dr;
                if (!InMapBounds(tx, ty)) continue;

                var tTile = map.Tile[tx, ty];
                int li = SelectedLayerArrayIndex;
                if (!LayerCell.IsEmpty(SelectedLayers(tTile)[li])) continue;

                var before = Snap(tTile);
                tTile = tTile.WithCell(SelectedLayerType, li, PackSelected(tileIdx));
                map.Tile[tx, ty] = tTile;
                SelectedMap.UpdateRecord(map);
                RepaintTile(tx, ty);
                Record(tx, ty, before, Snap(tTile));
            }
        }
    }

    /// <summary>Called by TileGridControl on left-click at tile (x, y).</summary>
    [RelayCommand]
    public void TileClicked(object? param)
    {
        if (param is not TileClick click) return;
        (int x, int y, bool altHeld, bool retain) = (click.X, click.Y, click.Alt, click.Retain);
        // 🔴 An authoring dialog opens on the PRESS and never on a dragged cell. A stroke laying a run of
        // walls crosses tiles that already hold one, and each of those is an "edit this attribute" click —
        // so the dialog fired mid-stroke, over and over. Alt+drag still lays runs, because the retained
        // values path above never reaches a dialog.
        bool dragging = click.Dragging;
        if (SelectedMap is null) return;

        // Select action: left-click is handled by the SelectionChanged event pipeline,
        // not the regular place pipeline.  Bail out so we don't paint a stamp underneath.
        if (SelectedAction == EditorAction.Select) return;

        // Place action with a mode-matching clipboard: paste instead of stamping.
        if (ClipboardKind == ClipboardKind.Tile && SelectedMode == EditorMode.Tile)
        {
            PasteTilesAt(x, y, retain);
            return;
        }
        if (ClipboardKind == ClipboardKind.Attribute && SelectedMode == EditorMode.Attribute
            && SelectedMap.Record.Tile[x, y].Type != SelectedAttribute)   // editing an existing attr → keep clipboard, open its dialog
        {
            PasteAttrsAt(x, y, retain);
            return;
        }
        if (ClipboardKind == ClipboardKind.Light && SelectedMode == EditorMode.Light)
        {
            PasteLightsAt(x, y, retain);
            return;
        }

        var map = SelectedMap.Record;

        if (SelectedMode == EditorMode.Tile)
        {
            PaintTileAt(x, y);
        }
        else if (SelectedMode == EditorMode.Attribute)
        {
            // Alt+Click: silently apply last retained values to the entire brush footprint.
            // Regular click: open the dialog once; all legal cells in the footprint get the result.
            // Instant attrs (Blocked, NpcAvoid): applied directly to the footprint, no dialog.

            var footprint = GetBrushFootprint(x, y);

            // NPC-spawn pin (editor-only attribute): single tile, no footprint fan-out, no tile.Type write.
            // Click an unpinned in-bounds tile → open the eligible-slot picker; a tile that already holds a pin
            // is left for right-click to clear, so every placement stays a clean single add.
            if (IsNpcSpawnTool)
            {
                if (!InMapBounds(x, y)) return;
                if (EntryPinnedAt(map, x, y, SelectedAttributeLayer) is not null) return;
                BuildNpcSpawnChoices();
                if (NpcSpawnChoices.Count == 0)
                {
                    StatusMessage = EditorStrings.Get(EditorStrings.MapEditorStatus_NoEligibleNpcSlots);
                    return;
                }
                NpcSpawnChoice = NpcSpawnChoices[0];
                _pendingTiles.Clear();
                _pendingTiles.Add((x, y));
                DialogError = "";
                if (dragging) return;   // a dialog opens on the press, never on a dragged cell
                ShowNpcSpawnDialog = true;
                return;
            }

            // LayerRamp: the sole connector between the two planes.  Stored on FringeAttr.Type = LayerRamp, but it
            // OCCUPIES BOTH planes — so it may only land on a fully-clear tile (no ground attr, no fringe attr, not
            // under a pinned NPC), and nothing else may be authored on it afterward.  Instant, footprint-wide, no
            // dialog; a tile that isn't clear is skipped.  Right-click clears it (see TileRightClicked).
            if (IsLayerRampTool)
            {
                foreach (var (tx, ty) in footprint)
                {
                    var t = map.Tile[tx, ty];
                    if (!TileAttrClear(t) || TileCoveredByPinnedFootprint(map, tx, ty)) continue;  // ramp needs a clear tile
                    var before = Snap(t);
                    t = t with { FringeAttr = new FringeAttr { Type = TileType.LayerRamp, RampGroundSide = LayerRampDirection } };
                    map.Tile[tx, ty] = t;
                    SelectedMap.UpdateRecord(map);
                    RepaintTile(tx, ty);
                    Record(tx, ty, before, Snap(t));
                }
                return;
            }

            // Footprint occupancy: a pinned NPC reserves its whole SxS footprint, and a LayerRamp claims the whole
            // tile on BOTH planes — no other attribute may be written on either.  Drop covered/ramp tiles from the
            // brush; if that leaves nothing, report why.
            var placeable = footprint.Where(p => !TileCoveredByPinnedFootprint(map, p.X, p.Y) && !TileHasRamp(map.Tile[p.X, p.Y])).ToList();
            if (placeable.Count == 0)
            {
                if (footprint.Count > 0)
                    StatusMessage = EditorStrings.Get(EditorStrings.MapEditorStatus_AttrUnderNpc);
                return;
            }
            footprint = placeable;

            // Uniform two-plane world: every attribute authors EITHER plane via SelectedAttributeLayer — the dialog
            // attributes (Warp/Item/Key/KeyOpen) through SetActiveAttr/ActiveAttrData, Blocked/NpcAvoid (default
            // case) through SetActiveAttr.  Per-layer door state (§1b) makes a fringe Key/KeyOpen fire on the deck.
            switch (SelectedAttribute)
            {
                case TileType.Warp:
                    if (altHeld)
                    {
                        if (_hasRetainedWarp)
                        {
                            foreach (var (tx, ty) in footprint)
                            {
                                var t = map.Tile[tx, ty];
                                var cur = ActiveAttrType(t);
                                if (cur != TileType.Walkable && cur != TileType.Warp) continue;
                                var before = Snap(t);
                                t = WithActiveAttr(t, new TileAttr { Type = TileType.Warp, WarpMap = _retWarpMapNum, WarpX = _retWarpX, WarpY = _retWarpY, WarpLayer = _retWarpDestLayer });
                                map.Tile[tx, ty] = t;
                                SelectedMap.UpdateRecord(map);
                                RepaintTile(tx, ty);
                                Record(tx, ty, before, Snap(t));
                            }
                        }

                        return;
                    }
                    {
                        var attr = ActiveAttrData(map.Tile[x, y]);
                        bool isWarp = attr.Type == TileType.Warp;
                        WarpMapNum = isWarp ? attr.WarpMap : (short)0;
                        WarpX = isWarp ? attr.WarpX : (ushort)0;
                        WarpY = isWarp ? attr.WarpY : (ushort)0;
                        WarpDestLayer = isWarp ? attr.WarpLayer : WorldLayer.Ground;
                        _pendingTiles.Clear();
                        // Editing an existing one anchors the connected-run fill; laying a new one
                        // leaves it inert, since a run grown from open ground would swallow the map.
                        _runAnchor = isWarp ? (x, y) : null;
                        OnPropertyChanged(nameof(CanFillRun));
                        if (isWarp)
                        {
                            _pendingTiles.Add((x, y));
                        }
                        else
                        {
                            foreach (var p in footprint)
                            {
                                var cur = ActiveAttrType(map.Tile[p.X, p.Y]);
                                if (cur == TileType.Walkable || cur == TileType.Warp)
                                    _pendingTiles.Add(p);
                            }
                        }

                        if (_pendingTiles.Count == 0) return;  // all tiles blocked by other attributes
                        DialogError = "";
                        if (dragging) return;   // a dialog opens on the press, never on a dragged cell
                        ShowWarpDialog = true;
                    }
                    return;

                case TileType.Item:
                    if (altHeld)
                    {
                        if (_hasRetainedItem)
                        {
                            foreach (var (tx, ty) in footprint)
                            {
                                var t = map.Tile[tx, ty];
                                var cur = ActiveAttrType(t);
                                if (cur != TileType.Walkable && cur != TileType.Item) continue;
                                var before = Snap(t);
                                t = WithActiveAttr(t, new TileAttr { Type = TileType.Item, ItemNum = _retItemNum, ItemQuantity = _retItemQuantity, ItemRespawnSecs = _retItemRespawn });
                                map.Tile[tx, ty] = t;
                                SelectedMap.UpdateRecord(map);
                                RepaintTile(tx, ty);
                                Record(tx, ty, before, Snap(t));
                            }
                        }

                        return;
                    }
                    {
                        var attr = ActiveAttrData(map.Tile[x, y]);
                        bool isItem = attr.Type == TileType.Item;
                        ItemTileNum = isItem ? attr.ItemNum : (short)0;
                        ItemTileQuantity = isItem ? attr.ItemQuantity : (short)0;
                        ItemTileRespawnSeconds = isItem ? attr.ItemRespawnSecs : (short)0;
                        _pendingTiles.Clear();
                        // Editing an existing one anchors the connected-run fill; laying a new one
                        // leaves it inert, since a run grown from open ground would swallow the map.
                        _runAnchor = isItem ? (x, y) : null;
                        OnPropertyChanged(nameof(CanFillRun));
                        if (isItem)
                        {
                            _pendingTiles.Add((x, y));
                        }
                        else
                        {
                            foreach (var p in footprint)
                            {
                                var cur = ActiveAttrType(map.Tile[p.X, p.Y]);
                                if (cur == TileType.Walkable || cur == TileType.Item)
                                    _pendingTiles.Add(p);
                            }
                        }

                        if (_pendingTiles.Count == 0) return;  // all tiles blocked by other attributes
                        DialogError = "";
                        if (dragging) return;   // a dialog opens on the press, never on a dragged cell
                        ShowItemDialog = true;
                    }
                    return;

                case TileType.Key:
                    if (altHeld)
                    {
                        if (_hasRetainedKey)
                        {
                            foreach (var (tx, ty) in footprint)
                            {
                                var t = map.Tile[tx, ty];
                                var cur = ActiveAttrType(t);
                                if (cur != TileType.Walkable && cur != TileType.Key) continue;
                                var before = Snap(t);
                                t = WithActiveAttr(t, new TileAttr { Type = TileType.Key, KeyItemNum = _retKeyItemNum, KeyIsConsumed = _retKeyTake });
                                map.Tile[tx, ty] = t;
                                SelectedMap.UpdateRecord(map);
                                RepaintTile(tx, ty);
                                Record(tx, ty, before, Snap(t));
                            }
                        }

                        return;
                    }
                    {
                        var attr = ActiveAttrData(map.Tile[x, y]);
                        bool isKey = attr.Type == TileType.Key;
                        KeyItemNum = isKey ? attr.KeyItemNum : (short)0;
                        KeyTake = isKey && attr.KeyIsConsumed;
                        _pendingTiles.Clear();
                        // Editing an existing one anchors the connected-run fill; laying a new one
                        // leaves it inert, since a run grown from open ground would swallow the map.
                        _runAnchor = isKey ? (x, y) : null;
                        OnPropertyChanged(nameof(CanFillRun));
                        if (isKey)
                        {
                            _pendingTiles.Add((x, y));
                        }
                        else
                        {
                            foreach (var p in footprint)
                            {
                                var cur = ActiveAttrType(map.Tile[p.X, p.Y]);
                                if (cur == TileType.Walkable || cur == TileType.Key)
                                    _pendingTiles.Add(p);
                            }
                        }

                        if (_pendingTiles.Count == 0) return;  // all tiles blocked by other attributes
                        DialogError = "";
                        if (dragging) return;   // a dialog opens on the press, never on a dragged cell
                        ShowKeyDialog = true;
                    }
                    return;

                case TileType.KeyOpen:
                    if (altHeld)
                    {
                        if (_hasRetainedKeyOpen)
                        {
                            foreach (var (tx, ty) in footprint)
                            {
                                var t = map.Tile[tx, ty];
                                var cur = ActiveAttrType(t);
                                if (cur != TileType.Walkable && cur != TileType.KeyOpen) continue;
                                var before = Snap(t);
                                t = WithActiveAttr(t, new TileAttr { Type = TileType.KeyOpen, DoorX = _retKeyOpenDoorX, DoorY = _retKeyOpenDoorY, DoorLayer = _retKeyOpenDoorLayer });
                                map.Tile[tx, ty] = t;
                                SelectedMap.UpdateRecord(map);
                                RepaintTile(tx, ty);
                                Record(tx, ty, before, Snap(t));
                            }
                        }

                        return;
                    }
                    {
                        var attr = ActiveAttrData(map.Tile[x, y]);
                        bool isKeyOpen = attr.Type == TileType.KeyOpen;
                        KeyOpenDoorX = isKeyOpen ? attr.DoorX : (ushort)0;
                        KeyOpenDoorY = isKeyOpen ? attr.DoorY : (ushort)0;
                        KeyOpenDoorLayer = isKeyOpen ? attr.DoorLayer : WorldLayer.Ground;
                        _pendingTiles.Clear();
                        // Editing an existing one anchors the connected-run fill; laying a new one
                        // leaves it inert, since a run grown from open ground would swallow the map.
                        _runAnchor = isKeyOpen ? (x, y) : null;
                        OnPropertyChanged(nameof(CanFillRun));
                        if (isKeyOpen)
                        {
                            _pendingTiles.Add((x, y));
                        }
                        else
                        {
                            foreach (var p in footprint)
                            {
                                var cur = ActiveAttrType(map.Tile[p.X, p.Y]);
                                if (cur == TileType.Walkable || cur == TileType.KeyOpen)
                                    _pendingTiles.Add(p);
                            }
                        }

                        if (_pendingTiles.Count == 0) return;  // all tiles blocked by other attributes
                        if (dragging) return;   // a dialog opens on the press, never on a dragged cell
                        ShowKeyOpenDialog = true;
                    }
                    return;

                case TileType.Blocked:
                    // A wall carries what it stops, so it opens a dialog like the other authored attributes.
                    // Alt+Click lays the retained pair straight onto the footprint.
                    if (altHeld)
                    {
                        foreach (var (tx, ty) in footprint)
                        {
                            var t = map.Tile[tx, ty];
                            var cur = ActiveAttrType(t);
                            if (cur != TileType.Walkable && cur != TileType.Blocked) continue;
                            var before = Snap(t);
                            t = WithActiveAttr(t, new TileAttr
                            {
                                Type = TileType.Blocked,
                                BlocksLight = _retBlocksLight,
                                BlocksSight = _retBlocksSight,
                            });
                            map.Tile[tx, ty] = t;
                            SelectedMap.UpdateRecord(map);
                            RepaintTile(tx, ty);
                            Record(tx, ty, before, Snap(t));
                        }
                        return;
                    }
                    {
                        var attr = ActiveAttrData(map.Tile[x, y]);
                        if (attr.Type != TileType.Blocked)
                        {
                            // Laying a wall is instant. Almost every wall stops everything, so declaring
                            // that each time would be a dialog that only ever gets confirmed.
                            foreach (var (tx, ty) in footprint)
                            {
                                var t = map.Tile[tx, ty];
                                if (ActiveAttrType(t) != TileType.Walkable) continue;
                                var before = Snap(t);
                                t = WithActiveAttr(t, TileType.Blocked);
                                map.Tile[tx, ty] = t;
                                SelectedMap.UpdateRecord(map);
                                RepaintTile(tx, ty);
                                Record(tx, ty, before, Snap(t));
                            }
                            return;
                        }

                        // Clicking a wall that is already there is how its exceptions are set.
                        BlockedBlocksLight = attr.BlocksLight;
                        BlockedBlocksSight = attr.BlocksSight;
                        _pendingTiles.Clear();
                        _pendingTiles.Add((x, y));
                        _runAnchor = (x, y);
                        OnPropertyChanged(nameof(CanFillRun));
                        DialogError = "";
                        if (dragging) return;   // a dialog opens on the press, never on a dragged cell
                        ShowBlockedDialog = true;
                    }
                    return;

                default:
                    // NpcAvoid — nothing to author, so it goes straight onto the brush footprint, on the ACTIVE
                    // plane (Ground = inline Type, Fringe = FringeAttr).
                    foreach (var (tx, ty) in footprint)
                    {
                        var t = map.Tile[tx, ty];
                        var cur = ActiveAttrType(t);
                        if (cur != TileType.Walkable && cur != SelectedAttribute) continue;   // occupied by another attr on this layer
                        var before = Snap(t);
                        t = WithActiveAttr(t, SelectedAttribute);
                        map.Tile[tx, ty] = t;
                        SelectedMap.UpdateRecord(map);
                        RepaintTile(tx, ty);
                        Record(tx, ty, before, Snap(t));
                    }
                    return;
            }
        }
        else // EditorMode.Light
        {
            if (!InMapBounds(x, y)) return;
            var existing = LightAt(map, x, y, SelectedAttributeLayer);

            // Alt+Click applies the retained light to the clicked tile without opening the dialog.
            if (altHeld)
            {
                if (!_hasRetainedLight) return;
                var pl = new PlacedLight(existing?.Id ?? Guid.NewGuid(), x, y, _retLight, SelectedAttributeLayer);
                SetLightSlot(map, x, y, SelectedAttributeLayer, pl);
                SelectedMap.UpdateRecord(map);
                RepaintTile(x, y);
                RecordLight(x, y, existing, pl);
                return;
            }

            // Regular click opens the dialog: existing light's values for an edit, torch defaults for a new one.
            var seed = existing?.Light ?? LightSpec.Torch;
            LightColor = ColorHex.ToColor(seed.Rgb);
            LightRadius = seed.Radius;
            LightFlicker = seed.Flicker;
            LightIntensity = (int)Math.Round(seed.Intensity * 100);
            _pendingTiles.Clear();
            _pendingTiles.Add((x, y));
            DialogError = "";
            if (dragging) return;   // a dialog opens on the press, never on a dragged cell
            ShowLightDialog = true;
        }
    }

    /// <summary>Called by TileGridControl on Ctrl+Alt+Shift + left-click on a neighbor cell.</summary>
    [RelayCommand]
    public void NeighborMapClicked(NeighborCell cell)
    {
        if (SelectedMap is null) return;
        // Resolve the destination by map id and look the row up by index (like WarpDestinationClicked),
        // not by record-reference: id-based navigation lands on the right row regardless of whether the
        // neighbor's record has been fetched yet, so switching to it triggers the normal load path
        // (OnSelectedMapChanged → LoadMapAsync) and its own connected properties fill in.
        int id = NeighborTargetId(cell);
        var row = RowFor(id);
        EditorLog.Info("Neighbor-cell navigation to map {Map} ({Cell}).", id, cell);
        if (row is not null && row != SelectedMap) SelectedMap = row;
    }

    // Map id behind a neighbor cell. Orthogonal cells read the active map's link directly, so navigation
    // works even before that neighbor has loaded (online). Diagonal cells mirror the NeighborMap*Diagonal
    // getters: follow the vertical-then-horizontal hop, else horizontal-then-vertical.
    private int NeighborTargetId(NeighborCell cell) => cell switch
    {
        NeighborCell.Up => MapUp,
        NeighborCell.Down => MapDown,
        NeighborCell.Left => MapLeft,
        NeighborCell.Right => MapRight,
        NeighborCell.UpLeft => DiagonalTargetId(MapUp, m => m.Left, MapLeft, m => m.Up),
        NeighborCell.UpRight => DiagonalTargetId(MapUp, m => m.Right, MapRight, m => m.Up),
        NeighborCell.DownLeft => DiagonalTargetId(MapDown, m => m.Left, MapLeft, m => m.Down),
        NeighborCell.DownRight => DiagonalTargetId(MapDown, m => m.Right, MapRight, m => m.Down),
        _ => 0,
    };

    // Diagonal target id, matching NeighborMap*Diagonal exactly: each hop requires the in-between map to be
    // resolved (loaded), so the id is that of the map actually shown in the diagonal cell.
    private int DiagonalTargetId(int firstOrtho, Func<MapRecord, int> firstPick,
                                 int secondOrtho, Func<MapRecord, int> secondPick)
    {
        if (Resolve(firstOrtho) is { } a) { int id = firstPick(a); if (Resolve(id) is not null) return id; }
        if (Resolve(secondOrtho) is { } b) { int id = secondPick(b); if (Resolve(id) is not null) return id; }
        return 0;
    }

    private IReadOnlyList<(int X, int Y)> GetBrushFootprint(int cx, int cy)
    {
        int sw = Math.Max(1, AttributeBrushSizeX);
        int sh = Math.Max(1, AttributeBrushSizeY);
        var cells = new List<(int, int)>(sw * sh);
        for (int dy = 0; dy < sh; dy++)
        {
            for (int dx = 0; dx < sw; dx++)
            {
                int tx = cx + dx, ty = cy + dy;
                if (InMapBounds(tx, ty))
                    cells.Add((tx, ty));
            }
        }

        return cells;
    }

    private void ApplyWarp(int x, int y)
    {
        if (SelectedMap is null) return;
        // Writes the ACTIVE plane (Ground inline vs FringeAttr) so a warp can be authored on a bridge deck.
        SelectedMap.Record.Tile[x, y] = WithActiveAttr(SelectedMap.Record.Tile[x, y], new TileAttr { Type = TileType.Warp, WarpMap = WarpMapNum, WarpX = WarpX, WarpY = WarpY, WarpLayer = WarpDestLayer });
        SelectedMap.UpdateRecord(SelectedMap.Record);
        RepaintTile(x, y);
    }

    private void ApplyItem(int x, int y)
    {
        if (SelectedMap is null) return;
        SelectedMap.Record.Tile[x, y] = WithActiveAttr(SelectedMap.Record.Tile[x, y], new TileAttr { Type = TileType.Item, ItemNum = ItemTileNum, ItemQuantity = ItemTileQuantity, ItemRespawnSecs = ItemTileRespawnSeconds });
        SelectedMap.UpdateRecord(SelectedMap.Record);
        RepaintTile(x, y);
    }

    private void ApplyKey(int x, int y)
    {
        if (SelectedMap is null) return;
        SelectedMap.Record.Tile[x, y] = WithActiveAttr(SelectedMap.Record.Tile[x, y], new TileAttr { Type = TileType.Key, KeyItemNum = KeyItemNum, KeyIsConsumed = KeyTake });
        SelectedMap.UpdateRecord(SelectedMap.Record);
        RepaintTile(x, y);
    }

    private void ApplyKeyOpen(int x, int y)
    {
        if (SelectedMap is null) return;
        // DoorLayer lets a KeyOpen open a Key door on either plane.
        SelectedMap.Record.Tile[x, y] = WithActiveAttr(SelectedMap.Record.Tile[x, y], new TileAttr { Type = TileType.KeyOpen, DoorX = KeyOpenDoorX, DoorY = KeyOpenDoorY, DoorLayer = KeyOpenDoorLayer });
        SelectedMap.UpdateRecord(SelectedMap.Record);
        RepaintTile(x, y);
    }

    /// <summary>Called by TileGridControl on right-click — clears based on current mode.</summary>
    [RelayCommand]
    public void TileRightClicked(object? param)
    {
        if (param is not ValueTuple<int, int> coords) return;
        var (x, y) = coords;
        if (SelectedMap is null) return;

        // Select action: right-click is a no-op (selections are made/cleared with
        // LMB-drag and ESC; deletion uses Ctrl+X).
        if (SelectedAction == EditorAction.Select) return;

        var map = SelectedMap.Record;

        EraseTileAt(map, x, y);
    }

    /// <summary>Erase the ACTIVE, mode-dependent content at one tile — the shared body of right-click erase and the
    /// Delete-action brush (see <see cref="DeleteAt"/>): a light, an NPC-spawn pin, or the active-layer tile-art /
    /// attribute. Records an undo op; no-op when there's nothing to remove.</summary>
    private void EraseTileAt(MapRecord map, int x, int y)
    {
        if (SelectedMode == EditorMode.Light)
        {
            var lb = LightAt(map, x, y, SelectedAttributeLayer);
            if (lb is null) return;
            SetLightSlot(map, x, y, SelectedAttributeLayer, null);
            SelectedMap!.UpdateRecord(map);
            RepaintTile(x, y);
            RecordLight(x, y, lb, null);
            return;
        }

        // NPC-spawn pin removal (Attribute mode): erasing a pinned tile unpins it — REGARDLESS of the selected
        // attribute tool (a pin reserves its whole footprint, so no tile attribute can coexist there anyway). If
        // there's no pin here, fall through to the normal active-layer attribute erase.
        if (SelectedMode == EditorMode.Attribute && EntryPinnedAt(map, x, y, SelectedAttributeLayer) is { } pinBefore)
        {
            SetEntryPinAt(map, x, y, SelectedAttributeLayer, null);
            SelectedMap!.UpdateRecord(map);
            RepaintTile(x, y);
            RecordNpcSpawn(x, y, SelectedAttributeLayer, pinBefore, null);
            RefreshNpcRow(pinBefore);
            return;
        }

        var tile = map.Tile[x, y];

        var before = Snap(tile);
        if (SelectedMode == EditorMode.Tile)
        {
            tile = tile.WithCell(SelectedLayerType, SelectedLayerArrayIndex, LayerCell.Empty);
        }
        else if (TileHasRamp(tile))
        {
            tile = tile with { FringeAttr = null };   // a ramp occupies both planes — erasing on EITHER layer removes it
        }
        else if (AttrLayerIsFringe)
        {
            tile = tile with { FringeAttr = null };   // clear this tile's fringe-plane attribute (back to the default walkable plane)
        }
        else
        {
            // Walkable authors nothing, so normalizing clears whatever the old type held.
            tile = (tile with { Type = TileType.Walkable }).Normalized();
        }
        map.Tile[x, y] = tile;
        SelectedMap!.UpdateRecord(map);
        RepaintTile(x, y);
        Record(x, y, before, Snap(tile));
    }

    /// <summary>Delete action: erase the mode-dependent content under the custom-sized brush (like right-click, but
    /// area-sized). Called on each Delete-brush click/drag step from the grid; the surrounding drag opens one undo
    /// batch (DragBegan/DragEnded) so a whole erase stroke undoes as one.</summary>
    [RelayCommand]
    public void DeleteAt(object? param)
    {
        if (param is not ValueTuple<int, int> coords || SelectedMap is null) return;
        var (x, y) = coords;
        var map = SelectedMap.Record;
        foreach (var (tx, ty) in GetBrushFootprint(x, y))
            EraseTileAt(map, tx, ty);
    }

    // ── Selection / Clipboard ─────────────────────────────────────────────────

    public void SelectionPhase(int x1, int y1, int x2, int y2, DragPhase phase)
    {
        if (SelectedMap is null || SelectedAction != EditorAction.Select) return;
        int nx1 = Math.Clamp(Math.Min(x1, x2), 0, MapCols - 1);
        int nx2 = Math.Clamp(Math.Max(x1, x2), 0, MapCols - 1);
        int ny1 = Math.Clamp(Math.Min(y1, y2), 0, MapRows - 1);
        int ny2 = Math.Clamp(Math.Max(y1, y2), 0, MapRows - 1);
        SelectionRect = new SelectionBox(nx1, ny1, nx2, ny2);
    }

    // ── Selected attribute description (shown in attribute mode tools panel) ──
    // Each description opens with the attribute's name, which EditorVocabulary supplies in English
    // for every language; the explanation after it is translated.
    public string SelectedAttributeDescription => AttributeDescription(SelectedAttributeTool);

    private static string AttributeDescription(AttributeTool tool)
    {
        string key = tool switch
        {
            AttributeTool.Blocked => EditorStrings.MapEditor_AttrDesc_Blocked,
            AttributeTool.Warp => EditorStrings.MapEditor_AttrDesc_Warp,
            AttributeTool.Item => EditorStrings.MapEditor_AttrDesc_Item,
            AttributeTool.NpcAvoid => EditorStrings.MapEditor_AttrDesc_NpcAvoid,
            AttributeTool.Key => EditorStrings.MapEditor_AttrDesc_Key,
            AttributeTool.KeyOpen => EditorStrings.MapEditor_AttrDesc_KeyOpen,
            AttributeTool.NpcSpawn => EditorStrings.MapEditor_AttrDesc_NpcSpawn,
            AttributeTool.LayerRamp => EditorStrings.MapEditor_AttrDesc_LayerRamp,
            _ => "",
        };
        return key.Length == 0
            ? EditorVocabulary.NameOf(tool)
            : EditorStrings.Format(key, ("Name", EditorVocabulary.NameOf(tool)));
    }
}

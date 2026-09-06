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

/// <summary>Committing each dialog: warp, NPC-spawn pin, item, key, key-open, light and tile
/// animation — plus the transient per-row NPC placement mode with its live footprint brush.</summary>
public sealed partial class MapEditorViewModel : ObservableObject
{
    /// <summary>The tiles a confirmed dialog writes: the brush footprint, or the whole connected run of
    /// the same attribute when the author asked for one.</summary>
    private IReadOnlyList<(int X, int Y)> TargetTiles() =>
        FillRun && _runAnchor is { } anchor ? ContiguousRun(anchor.X, anchor.Y) : _pendingTiles;

    /// <summary>Every tile reachable from (<paramref name="x"/>, <paramref name="y"/>) by orthogonal steps
    /// across tiles carrying the same attribute on the active plane.
    ///
    /// <para>Orthogonal rather than diagonal: two walls meeting at a corner are two walls, and an author
    /// editing one of them is not asking to edit the other.</para></summary>
    private List<(int X, int Y)> ContiguousRun(int x, int y)
    {
        var run = new List<(int X, int Y)>();
        if (SelectedMap is null) return run;
        var map = SelectedMap.Record;
        var want = ActiveAttrType(map.Tile[x, y]);

        var seen = new bool[MapCols, MapRows];
        var stack = new Stack<(int X, int Y)>();
        stack.Push((x, y));
        seen[x, y] = true;
        while (stack.Count > 0)
        {
            var (cx, cy) = stack.Pop();
            run.Add((cx, cy));
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = cx + dx, ny = cy + dy;
                if (!InMapBounds(nx, ny)) continue;
                if (seen[nx, ny]) continue;
                if (ActiveAttrType(map.Tile[nx, ny]) != want) continue;
                seen[nx, ny] = true;
                stack.Push((nx, ny));
            }
        }
        return run;
    }

    // ── Warp dialog confirm ───────────────────────────────────────────────────

    [RelayCommand]
    private void ConfirmWarp()
    {
        if (SelectedMap is null) return;
        if (WarpMapNum <= 0)
        {
            DialogError = EditorStrings.Get(EditorStrings.AttrDialog_SelectMap);
            return;
        }
        DialogError = "";
        BeginBatch();
        foreach (var (tx, ty) in TargetTiles())
        {
            var before = Snap(SelectedMap.Record.Tile[tx, ty]);
            ApplyWarp(tx, ty);
            Record(tx, ty, before, Snap(SelectedMap.Record.Tile[tx, ty]));
        }
        CommitBatch();
        if (WarpRetain)
        {
            _hasRetainedWarp = true;
            _retWarpMapNum = WarpMapNum;
            _retWarpX = WarpX;
            _retWarpY = WarpY;
            _retWarpDestLayer = WarpDestLayer;
        }
        ShowWarpDialog = false;
    }

    [RelayCommand]
    private void CancelWarp()
    {
        DialogError = "";
        ShowWarpDialog = false;
    }

    // ── Blocked dialog confirm ───────────────────────────────────────────────

    [RelayCommand]
    private void ConfirmBlocked()
    {
        if (SelectedMap is null) return;
        DialogError = "";
        BeginBatch();
        foreach (var (tx, ty) in TargetTiles())
        {
            var t = SelectedMap.Record.Tile[tx, ty];
            var before = Snap(t);
            t = WithActiveAttr(t, new TileAttr
            {
                Type = TileType.Blocked,
                BlocksLight = BlockedBlocksLight,
                BlocksSight = BlockedBlocksSight,
            });
            SelectedMap.Record.Tile[tx, ty] = t;
            SelectedMap.UpdateRecord(SelectedMap.Record);
            RepaintTile(tx, ty);
            Record(tx, ty, before, Snap(t));
        }
        CommitBatch();
        if (BlockedRetain)
        {
            _retBlocksLight = BlockedBlocksLight;
            _retBlocksSight = BlockedBlocksSight;
        }
        ShowBlockedDialog = false;
    }

    [RelayCommand]
    private void CancelBlocked()
    {
        DialogError = "";
        ShowBlockedDialog = false;
    }

    // ── NPC-spawn pin dialog (Attribute mode, NpcSpawn tool) ──────────────────
    // Eligible rows = a non-empty NPC type (entry.Npc != 0) not already pinned to a tile, in row order.
    private void BuildNpcSpawnChoices()
    {
        NpcSpawnChoices.Clear();
        if (SelectedMap is null) return;
        var npcs = SelectedMap.Record.Npcs;
        for (int i = 0; i < npcs.Count; i++)
        {
            if (npcs[i].Npc <= 0) continue;   // empty row — nothing to place
            if (npcs[i].HasPin) continue;      // already pinned — one pin per entry
            string name = NpcEntryForRow(i)?.Name ?? npcs[i].Npc.ToString();
            NpcSpawnChoices.Add(new NpcSpawnChoice(i, $"{i + 1} — {name}"));   // display the runtime post number
        }
    }

    [RelayCommand]
    private void ConfirmNpcSpawn()
    {
        if (SelectedMap is null) return;
        if (NpcSpawnChoice is not { } choice) { DialogError = EditorStrings.Get(EditorStrings.AttrDialog_SelectNpcSlot); return; }
        var map = SelectedMap.Record;
        // Size-aware validation: the chosen NPC's footprint must fit at every pending tile
        // (on-map, all walkable, no overlap with another placed NPC) before we commit the pin.
        // A pin is one NPC at one tile, so this is the brush footprint and never a connected run.
        foreach (var (tx, ty) in _pendingTiles)
        {
            var err = MapNpcPlacement.ValidatePin(map, choice.RowIndex, tx, ty, SelectedAttributeLayer, NpcSize);
            if (err != NpcPlacementError.None)
            {
                DialogError = PlacementErrorText(err);
                return;
            }
        }
        DialogError = "";
        BeginBatch();
        foreach (var (tx, ty) in _pendingTiles)
        {
            var before = EntryPinnedAt(map, tx, ty, SelectedAttributeLayer);
            SetEntryPinAt(map, tx, ty, SelectedAttributeLayer, choice.RowIndex);
            SelectedMap.UpdateRecord(map);
            RepaintTile(tx, ty);
            RecordNpcSpawn(tx, ty, SelectedAttributeLayer, before, choice.RowIndex);
        }
        CommitBatch();
        RowAt(choice.RowIndex)?.Refresh();
        ShowNpcSpawnDialog = false;
    }

    [RelayCommand]
    private void CancelNpcSpawn()
    {
        DialogError = "";
        ShowNpcSpawnDialog = false;
    }

    // ── MODE 2: transient per-row NPC placement ────────────────────────────────
    public bool IsPlacingNpc => PlacingNpcRow >= 0;

    // Footprint size of the NPC on the row being placed (drives the brush); 1 when idle / row empty.
    public int PlacingNpcSize
    {
        get
        {
            if (PlacingNpcRow < 0 || SelectedMap is null) return 1;
            var npcs = SelectedMap.Record.Npcs;
            return PlacingNpcRow < npcs.Count ? Math.Max(1, NpcSize(npcs[PlacingNpcRow].Npc)) : 1;
        }
    }

    // Enter placement mode bound to row `rowIndex`: subsequent grid clicks pin that row's NPC (validated live).
    // Forces Attribute mode so the existing pins/attributes that drive validity are visible while aiming.
    public void BeginPlaceNpcRow(int rowIndex)
    {
        if (SelectedMap is null) return;
        var npcs = SelectedMap.Record.Npcs;
        if (rowIndex < 0 || rowIndex >= npcs.Count || npcs[rowIndex].Npc <= 0)
        {
            StatusMessage = EditorStrings.Get(EditorStrings.MapEditorStatus_PlaceNeedsNpc);
            return;
        }
        SelectedMode = EditorMode.Attribute;
        PlacingNpcRow = rowIndex;
        StatusMessage = EditorStrings.Format(EditorStrings.MapEditorStatus_PlacePrompt, ("Post", rowIndex + 1));
    }

    [RelayCommand]
    public void CancelPlaceNpc()
    {
        if (!IsPlacingNpc) return;
        PlacingNpcRow = -1;
        StatusMessage = EditorStrings.Get(EditorStrings.MapEditorStatus_PlaceCanceled);
    }

    // True if the row being placed would form a legal pin at (x,y) — drives the grid's green/red brush.
    public bool CanPlacePlacingNpcAt(int x, int y)
    {
        if (SelectedMap is null || PlacingNpcRow < 0) return false;
        var map = SelectedMap.Record;
        if (PlacingNpcRow >= map.Npcs.Count || map.Npcs[PlacingNpcRow].Npc <= 0) return false;
        return MapNpcPlacement.ValidatePin(map, PlacingNpcRow, x, y, SelectedAttributeLayer, NpcSize) == NpcPlacementError.None;
    }

    // Commit the current placement at (x,y): pin the row (MOVING its old pin if any), then exit the mode. An
    // invalid tile reports why and STAYS in placement mode so the author can pick another spot. Shares
    // ValidatePin + the pin/undo helpers with the MODE-1 dialog path.
    public void PlaceNpcAtHover(int x, int y)
    {
        if (SelectedMap is null || PlacingNpcRow < 0) return;
        var map = SelectedMap.Record;
        int rowIndex = PlacingNpcRow;
        if (rowIndex >= map.Npcs.Count || map.Npcs[rowIndex].Npc <= 0)
        {
            CancelPlaceNpc();
            return;
        }

        var err = MapNpcPlacement.ValidatePin(map, rowIndex, x, y, SelectedAttributeLayer, NpcSize);
        if (err != NpcPlacementError.None)
        {
            StatusMessage = PlacementErrorText(err);
            return;
        }

        BeginBatch();
        // If this row is already pinned elsewhere, free the old tile first (its own undo op, on the OLD pin's own
        // layer) so the pin MOVES rather than duplicating — an entry holds a single pin, so the ops undo/redo
        // independently. The new pin lands on the ACTIVE layer.
        var entry = map.Npcs[rowIndex];
        if (entry.HasPin && (entry.PinX!.Value != x || entry.PinY!.Value != y || entry.PinLayer != SelectedAttributeLayer))
        {
            int ox = entry.PinX!.Value, oy = entry.PinY!.Value;
            SetEntryPinAt(map, ox, oy, entry.PinLayer, null);
            RepaintTile(ox, oy);
            RecordNpcSpawn(ox, oy, entry.PinLayer, rowIndex, null);
        }
        var before = EntryPinnedAt(map, x, y, SelectedAttributeLayer);   // null for a valid placement (overlap is rejected above)
        SetEntryPinAt(map, x, y, SelectedAttributeLayer, rowIndex);
        SelectedMap.UpdateRecord(map);
        RepaintTile(x, y);
        RecordNpcSpawn(x, y, SelectedAttributeLayer, before, rowIndex);
        CommitBatch();
        RowAt(rowIndex)?.Refresh();

        PlacingNpcRow = -1;
        StatusMessage = EditorStrings.Format(EditorStrings.MapEditorStatus_PlaceDone, ("Post", rowIndex + 1));
    }

    // ── Item dialog confirm ───────────────────────────────────────────────────

    [RelayCommand]
    private void ConfirmItem()
    {
        if (SelectedMap is null) return;
        if (ItemTileNum <= 0)
        {
            DialogError = EditorStrings.Get(EditorStrings.AttrDialog_SelectItem);
            return;
        }
        if (ItemTileQuantity <= 0)
        {
            DialogError = EditorStrings.Get(EditorStrings.AttrDialog_ValueAtLeastOne);
            return;
        }
        if (ItemTileQuantity > 1 && !_data.IsCurrencyItem(ItemTileNum))
        {
            DialogError = EditorStrings.Get(EditorStrings.AttrDialog_NonCurrencyQtyOne);
            return;
        }
        DialogError = "";
        BeginBatch();
        foreach (var (tx, ty) in TargetTiles())
        {
            var before = Snap(SelectedMap.Record.Tile[tx, ty]);
            ApplyItem(tx, ty);
            Record(tx, ty, before, Snap(SelectedMap.Record.Tile[tx, ty]));
        }
        CommitBatch();
        if (ItemRetain)
        {
            _hasRetainedItem = true;
            _retItemNum = ItemTileNum;
            _retItemQuantity = ItemTileQuantity;
            _retItemRespawn = ItemTileRespawnSeconds;
        }
        ShowItemDialog = false;
    }

    [RelayCommand]
    private void CancelItem()
    {
        DialogError = "";
        ShowItemDialog = false;
    }

    // ── Key dialog confirm ────────────────────────────────────────────────────

    [RelayCommand]
    private void ConfirmKey()
    {
        if (SelectedMap is null) return;
        if (KeyItemNum <= 0)
        {
            DialogError = EditorStrings.Get(EditorStrings.AttrDialog_SelectKeyItem);
            return;
        }
        DialogError = "";
        BeginBatch();
        foreach (var (tx, ty) in TargetTiles())
        {
            var before = Snap(SelectedMap.Record.Tile[tx, ty]);
            ApplyKey(tx, ty);
            Record(tx, ty, before, Snap(SelectedMap.Record.Tile[tx, ty]));
        }
        CommitBatch();
        if (KeyRetain)
        {
            _hasRetainedKey = true;
            _retKeyItemNum = KeyItemNum;
            _retKeyTake = KeyTake;
        }
        ShowKeyDialog = false;
    }

    [RelayCommand]
    private void CancelKey()
    {
        DialogError = "";
        ShowKeyDialog = false;
    }

    // ── KeyOpen dialog confirm ────────────────────────────────────────────────

    [RelayCommand]
    private void ConfirmKeyOpen()
    {
        if (SelectedMap is null) return;
        BeginBatch();
        foreach (var (tx, ty) in TargetTiles())
        {
            var before = Snap(SelectedMap.Record.Tile[tx, ty]);
            ApplyKeyOpen(tx, ty);
            Record(tx, ty, before, Snap(SelectedMap.Record.Tile[tx, ty]));
        }
        CommitBatch();
        if (KeyOpenRetain)
        {
            _hasRetainedKeyOpen = true;
            _retKeyOpenDoorX = KeyOpenDoorX;
            _retKeyOpenDoorY = KeyOpenDoorY;
            _retKeyOpenDoorLayer = KeyOpenDoorLayer;
        }
        ShowKeyOpenDialog = false;
    }

    [RelayCommand] private void CancelKeyOpen() => ShowKeyOpenDialog = false;

    // ── Light dialog confirm / clear ──────────────────────────────────────────

    [RelayCommand]
    private void ConfirmLight()
    {
        if (SelectedMap is null) return;
        if (LightRadius <= 0)
        {
            DialogError = EditorStrings.Get(EditorStrings.AttrDialog_RadiusPositive);
            return;
        }
        DialogError = "";
        var spec = new LightSpec(ColorHex.ToRgb(LightColor), (float)LightRadius, LightFlicker,
            Math.Clamp(LightIntensity, 0, 100) / 100f);
        var map = SelectedMap.Record;
        BeginBatch();
        foreach (var (tx, ty) in _pendingTiles)
        {
            var before = LightAt(map, tx, ty, SelectedAttributeLayer);
            var pl = new PlacedLight(before?.Id ?? Guid.NewGuid(), tx, ty, spec, SelectedAttributeLayer);   // keep Id on edit
            SetLightSlot(map, tx, ty, SelectedAttributeLayer, pl);
            SelectedMap.UpdateRecord(map);
            RepaintTile(tx, ty);
            RecordLight(tx, ty, before, pl);
        }
        CommitBatch();
        if (LightRetain)
        {
            _hasRetainedLight = true;
            _retLight = spec;
        }
        ShowLightDialog = false;
    }

    [RelayCommand]
    private void CancelLight()
    {
        DialogError = "";
        ShowLightDialog = false;
    }

    // ── Tile-animation dialog ──────────────────────────────────────────────────

    /// <summary>Opens the animation editor for a placed tile — fired at press time by the grid when a
    /// click lands on an occupied selected layer. Lists every occupied Ground+Fringe layer.</summary>
    [RelayCommand]
    public void AnimEdit(object? param)
    {
        if (param is ValueTuple<int, int> cell) OpenAnimDialog(cell.Item1, cell.Item2);
    }

    private void OpenAnimDialog(int x, int y)
    {
        if (SelectedMap is null) return;
        var tile = SelectedMap.Record.Tile[x, y];
        foreach (var r in AnimLayers) r.PropertyChanged -= OnAnimRowChanged;
        AnimLayers.Clear();
        AddAnimRows(tile.Ground, LayerType.Ground);
        AddAnimRows(tile.Fringe, LayerType.Fringe);
        if (AnimLayers.Count == 0) return;   // empty tile — nothing to animate
        foreach (var r in AnimLayers) r.PropertyChanged += OnAnimRowChanged;
        GroundAnimStyle = StackStyle(tile.Ground);
        FringeAnimStyle = StackStyle(tile.Fringe);
        _animDialogX = x;
        _animDialogY = y;
        OnPropertyChanged(nameof(GroundStyleEnabled));
        OnPropertyChanged(nameof(FringeStyleEnabled));
        ShowAnimDialog = true;
    }

    private void AddAnimRows(ReadOnlySpan<int> layers, LayerType type)
    {
        for (int i = 0; i < layers.Length; i++)
        {
            int packed = layers[i];
            if (LayerCell.IsEmpty(packed)) continue;
            int sheet = LayerCell.Sheet(packed);
            Bitmap? bmp = sheet >= 0 && sheet < Tilesets.Count ? Tilesets[sheet] : null;
            string label = $"{ColumnStringFor(type)} {i + 1}";
            AnimLayers.Add(new AnimLayerRow(type, i, label, bmp, LayerCell.Tile(packed), LayerCell.Anim(packed)));
        }
    }

    // A stack's style = the style bit on its lowest animated layer (defaults Cycle when none animate).
    private static AnimStyle StackStyle(ReadOnlySpan<int> layers)
    {
        for (int i = 0; i < layers.Length; i++)
            if (!LayerCell.IsEmpty(layers[i]) && LayerCell.Anim(layers[i])) return LayerCell.Style(layers[i]);
        return AnimStyle.Cycle;
    }

    private void OnAnimRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AnimLayerRow.IsAnim)) return;
        OnPropertyChanged(nameof(GroundStyleEnabled));
        OnPropertyChanged(nameof(FringeStyleEnabled));
    }

    [RelayCommand]
    private void ConfirmAnim()
    {
        if (SelectedMap is null)
        {
            ShowAnimDialog = false;
            return;
        }
        var map = SelectedMap.Record;
        var tile = map.Tile[_animDialogX, _animDialogY];
        var before = Snap(tile);
        bool changed = false;
        foreach (var row in AnimLayers)
        {
            int packed = tile.Art(row.Type)[row.ArrayIndex];
            // Non-anim layers store the neutral Cycle style; anim layers take their stack's chosen style.
            var style = !row.IsAnim ? AnimStyle.Cycle
                : row.Type == LayerType.Ground ? GroundAnimStyle : FringeAnimStyle;
            int repacked = LayerCell.Pack(LayerCell.Tile(packed), LayerCell.Sheet(packed), row.IsAnim, style);
            if (repacked != packed)
            {
                tile = tile.WithCell(row.Type, row.ArrayIndex, repacked);
                changed = true;
            }
        }
        if (changed)
        {
            map.Tile[_animDialogX, _animDialogY] = tile;
            BeginBatch();
            SelectedMap.UpdateRecord(map);
            RepaintTile(_animDialogX, _animDialogY);
            Record(_animDialogX, _animDialogY, before, Snap(tile));
            CommitBatch();
        }
        ShowAnimDialog = false;
    }

    [RelayCommand] private void CancelAnim() => ShowAnimDialog = false;

    [RelayCommand]
    private async Task ClearLightsAsync()
    {
        if (SelectedMap is null) return;
        var map = SelectedMap.Record;
        if (map.Lights.Count == 0) return;
        if (ConfirmAsync is not null &&
            !await ConfirmAsync(EditorStrings.Get(EditorStrings.MapEditor_ConfirmClearLights)))
        {
            return;
        }

        BeginBatch();
        foreach (var pl in map.Lights.ToArray())
        {
            SetLightSlot(map, pl.X, pl.Y, pl.Layer, null);
            RepaintTile(pl.X, pl.Y);
            RecordLight(pl.X, pl.Y, pl, null);
        }
        SelectedMap.UpdateRecord(map);
        CommitBatch();
        StatusMessage = EditorStrings.Get(EditorStrings.MapEditorStatus_ClearedLights);
    }

    // Localized "can't place here" reason for a failed footprint validation.
    private static string PlacementErrorText(NpcPlacementError err) => err switch
    {
        NpcPlacementError.OffMap => EditorStrings.Get(EditorStrings.MapEditorStatus_PlaceOffMap),
        NpcPlacementError.OnBlocked => EditorStrings.Get(EditorStrings.MapEditorStatus_PlaceOnBlocked),
        NpcPlacementError.Overlap => EditorStrings.Get(EditorStrings.MapEditorStatus_PlaceOverlap),
        _ => "",
    };
}

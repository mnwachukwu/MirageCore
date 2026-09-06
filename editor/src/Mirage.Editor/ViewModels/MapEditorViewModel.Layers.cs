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

/// <summary>Layer and tileset selection — which layer type and index paint receives, which sheet
/// new tiles are tagged with — and the fill/clear operations over a whole layer.</summary>
public sealed partial class MapEditorViewModel : ObservableObject
{
    // ── Layer & tileset selection ──────────────────────────────────────────────

    public IEnumerable<LayerType> LayerTypes { get; } = Enum.GetValues<LayerType>();
    public IReadOnlyList<int> LayerIndices { get; } =
        [.. Enumerable.Range(1, Math.Max(Math.Max(Constants.MaxGroundLayers, Constants.MaxFringeLayers), Constants.MaxCanopyLayers))];

    // Per-sheet display names (filename minus numeric prefix), parallel to Tilesets.
    [ObservableProperty] private IReadOnlyList<string> _tilesetNames = [];

    // Typeahead entries for the tileset picker: Id = sheet index, Name = sheet name.  NamedEntry renders
    // as "0: Tiles", giving the index-prefixed label.  Rebuilt when the sheets or their names change.
    private NamedEntry[] _tilesetEntries = [];
    public NamedEntry[] TilesetEntries => _tilesetEntries;

    private void RebuildTilesetEntries()
    {
        _tilesetEntries = SheetEntries.Build(Tilesets.Count, TilesetNames);
        OnPropertyChanged(nameof(TilesetEntries));
        OnPropertyChanged(nameof(SelectedTilesetEntry));
    }

    // Selected sheet as a NamedEntry for the typeahead picker (two-way with SelectedTileset).
    public NamedEntry? SelectedTilesetEntry
    {
        get => SelectedTileset >= 0 && SelectedTileset < _tilesetEntries.Length ? _tilesetEntries[SelectedTileset] : null;
        set { if (value is not null) SelectedTileset = value.Id; }
    }

    // Sheet index → display name (filename minus numeric prefix), falling back to the shared
    // "(unnamed)" string. Shared by the tileset picker entries and the Used Tilesheets list.
    private string TilesetDisplayName(int sheet) => SheetEntries.DisplayName(TilesetNames, sheet);

    // Index-prefixed labels ("0: Tiles", the same form the tileset picker uses) for every sheet used by
    // a non-empty Ground/Fringe layer on the current map, one per line, ordered by sheet index. Read-only
    // display in the Properties panel. Stays current on its own: NotifyMapProperties (every tile edit,
    // undo/redo, and map switch) and OnTilesetNamesChanged (sheet names loading) both re-raise it.
    public string UsedTilesheets
    {
        get
        {
            if (SelectedMap is null) return string.Empty;
            var used = new SortedSet<int>();
            var map = SelectedMap.Record;
            for (int y = 0; y < MapRows; y++)
            {
                for (int x = 0; x < MapCols; x++)
                {
                    AddUsedSheets(map.Tile[x, y].Ground, used);
                    AddUsedSheets(map.Tile[x, y].Fringe, used);
                    AddUsedSheets(map.Tile[x, y].Canopy, used);
                }
            }
            // Same "index: name" rendering as the picker (NamedEntry.ToString) — single source of truth.
            return string.Join('\n', used.Select(sheet => new NamedEntry(sheet, TilesetDisplayName(sheet)).ToString()));
        }
    }

    private static void AddUsedSheets(ReadOnlySpan<int> layers, SortedSet<int> used)
    {
        foreach (int cell in layers)
            if (!LayerCell.IsEmpty(cell)) used.Add(LayerCell.Sheet(cell));
    }

    partial void OnTilesetsChanged(IReadOnlyList<Bitmap?> value)
    {
        if (SelectedTileset >= value.Count) SelectedTileset = 0;
        RebuildTilesetEntries();
        UpdateTileBitmap();
    }
    partial void OnTilesetNamesChanged(IReadOnlyList<string> value)
    {
        RebuildTilesetEntries();
        OnPropertyChanged(nameof(UsedTilesheets));
    }
    partial void OnSelectedTilesetChanged(int value)
    {
        UpdateTileBitmap();
        OnPropertyChanged(nameof(SelectedTilesetEntry));
    }
    partial void OnSelectedLayerTypeChanged(LayerType value)
    {
        OnPropertyChanged(nameof(SelectedLayerLabel));
        OnPropertyChanged(nameof(StatusLayerText));
    }
    partial void OnSelectedLayerIndexChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedLayerLabel));
        OnPropertyChanged(nameof(StatusLayerText));
    }

    private void UpdateTileBitmap() =>
        TileBitmap = SelectedTileset >= 0 && SelectedTileset < Tilesets.Count ? Tilesets[SelectedTileset] : null;

    // 0-based array index of the selected layer within its layer type's stack.
    private int SelectedLayerArrayIndex => SelectedLayerIndex - 1;
    // The tile-art stack (Ground / Fringe / Canopy) for a layer type, and its layer count.
    private static ReadOnlySpan<int> StackOf(in TileRecord t, LayerType type) => t.Art(type);
    // Nullable overload for the hover preview, which may have no tile under the cursor.
    private static int CellAt(TileRecord? t, LayerType type, int index) =>
        t is { } tile && index < TileRecord.Depth(type) ? tile.Art(type)[index] : LayerCell.Empty;
    private static int MaxLayersOf(LayerType type) => type switch
    {
        LayerType.Ground => Constants.MaxGroundLayers,
        LayerType.Fringe => Constants.MaxFringeLayers,
        _ => Constants.MaxCanopyLayers,
    };
    // The selected layer type's stack within a tile.
    private ReadOnlySpan<int> SelectedLayers(in TileRecord t) => StackOf(in t, SelectedLayerType);
    // Pack a palette tile index with the currently-selected sheet, Anim flag, and animation style.
    private int PackSelected(int tileIdx) => LayerCell.Pack(tileIdx, SelectedTileset, SelectedAnim, SelectedAnimStyle);
    // "Ground 2" / "Fringe 5" for the layer label, the anim rows, and the status messages. The layer
    // word is vocabulary, not translated prose: it names the same stack the map files and the layer
    // picker name, so it reads identically in every language.
    private static string ColumnStringFor(LayerType type) => EditorVocabulary.NameOf(type);
    public string SelectedLayerLabel => $"{ColumnStringFor(SelectedLayerType)} {SelectedLayerIndex}";
    // Footer form of the layer label, prefixed "Layer:".  SelectedLayerLabel itself stays bare because
    // it is also interpolated into the "Filled {Layer}" / "Cleared {Layer}" status messages.
    public string StatusLayerText =>
        EditorStrings.Format(EditorStrings.MapEditor_StatusLayer, ("Layer", SelectedLayerLabel));

    // ── Fill / Clear layer ────────────────────────────────────────────────────

    // Assigned by the View so the VM can show a confirmation without a direct Window reference.
    public Func<string, Task<bool>>? ConfirmAsync { get; set; }
    public Func<string, Task>? ShowAlertAsync { get; set; }
    // Assigned by the window: shows a native Save-As dialog for a PNG and returns the chosen local path
    // (or null if canceled). The VM does the rendering/encoding; only the file dialog needs the Window.
    public Func<string, Task<string?>>? SaveFilePngAsync { get; set; }

    public bool CanFillLayer =>
        SelectedMap is not null &&
        SelectedStamp is { Cols: 1, Rows: 1 } stamp &&
        stamp.Indices[0, 0] > 0;

    partial void OnSelectedStampChanged(TileStamp? value) => FillLayerCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanFillLayer))]
    private void FillLayer()
    {
        if (SelectedMap is null || SelectedStamp is null) return;
        var map = SelectedMap.Record;
        int packed = PackSelected(SelectedStamp.Indices[0, 0]);
        int idx = SelectedLayerArrayIndex;
        BeginBatch();
        for (int y = 0; y < MapRows; y++)
        {
            for (int x = 0; x < MapCols; x++)
            {
                var tile = map.Tile[x, y];
                if (!LayerCell.IsEmpty(SelectedLayers(tile)[idx])) continue;
                var before = Snap(tile);
                tile = tile.WithCell(SelectedLayerType, idx, packed);
                map.Tile[x, y] = tile;
                SelectedMap.UpdateRecord(map);
                RepaintTile(x, y);
                Record(x, y, before, Snap(tile));
            }
        }

        CommitBatch();
        StatusMessage = EditorStrings.Format(EditorStrings.MapEditorStatus_Filled,
            ("Layer", SelectedLayerLabel));
    }

    [RelayCommand]
    private async Task ClearLayerAsync()
    {
        if (SelectedMap is null) return;
        EditorLog.Info("Clear layer {Layer} requested on map {Map}; awaiting confirmation.",
            SelectedLayerLabel, SelectedMap.Index);
        if (ConfirmAsync is not null &&
            !await ConfirmAsync(EditorStrings.Format(EditorStrings.MapEditor_ConfirmClearLayer,
                ("Layer", SelectedLayerLabel))))
        {
            return;
        }

        var map = SelectedMap.Record;
        int idx = SelectedLayerArrayIndex;
        EditorLog.Info("Clearing layer {Layer} on map {Map}.", SelectedLayerLabel, SelectedMap.Index);
        BeginBatch();
        for (int y = 0; y < MapRows; y++)
        {
            for (int x = 0; x < MapCols; x++)
            {
                var tile = map.Tile[x, y];
                if (LayerCell.IsEmpty(SelectedLayers(tile)[idx])) continue;
                var before = Snap(tile);
                tile = tile.WithCell(SelectedLayerType, idx, LayerCell.Empty);
                map.Tile[x, y] = tile;
                SelectedMap.UpdateRecord(map);
                RepaintTile(x, y);
                Record(x, y, before, Snap(tile));
            }
        }

        CommitBatch();
        EditorLog.Info("Cleared layer {Layer} on map {Map}.", SelectedLayerLabel, SelectedMap.Index);
        StatusMessage = EditorStrings.Format(EditorStrings.MapEditorStatus_ClearedLayer,
            ("Layer", SelectedLayerLabel));
    }

    [RelayCommand]
    private async Task ClearAttributesAsync()
    {
        if (SelectedMap is null) return;
        EditorLog.Info("Clear attributes requested on map {Map}; awaiting confirmation.", SelectedMap.Index);
        if (ConfirmAsync is not null &&
            !await ConfirmAsync(EditorStrings.Get(EditorStrings.MapEditor_ConfirmClearAttrs)))
        {
            return;
        }

        var map = SelectedMap.Record;
        BeginBatch();
        for (int y = 0; y < MapRows; y++)
        {
            for (int x = 0; x < MapCols; x++)
            {
                var tile = map.Tile[x, y];
                if (tile.Type == TileType.Walkable) continue;
                var before = Snap(tile);
                // Walkable authors nothing, so normalizing clears whatever the old type held.
                tile = (tile with { Type = TileType.Walkable }).Normalized();
                map.Tile[x, y] = tile;
                SelectedMap.UpdateRecord(map);
                RepaintTile(x, y);
                Record(x, y, before, Snap(tile));
            }
        }
        // NPC-spawn pins are attribute-mode data too — clear them in the same batch.
        for (int i = 0; i < map.Npcs.Count; i++)
        {
            var e = map.Npcs[i];
            if (!e.HasPin) continue;
            int px = e.PinX!.Value, py = e.PinY!.Value;
            map.Npcs[i] = e with { PinX = null, PinY = null };
            RepaintTile(px, py);
            RecordNpcSpawn(px, py, e.PinLayer, i, null);
            RefreshNpcRow(i);
        }
        SelectedMap.UpdateRecord(map);
        CommitBatch();
        StatusMessage = EditorStrings.Get(EditorStrings.MapEditorStatus_ClearedAttributes);
    }
}

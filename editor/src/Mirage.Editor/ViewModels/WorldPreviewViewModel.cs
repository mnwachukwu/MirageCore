using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Editor.Services;
using Mirage.Shared.Records;
using System.ComponentModel;

namespace Mirage.Editor.ViewModels;

/// <summary>
/// The World Preview window's state: which maps are on the grid, how far out it reaches, and how far in
/// it is zoomed.
///
/// <para>Read-only over the map editor. It reads maps through <see cref="MapEditorViewModel.ReadMapAsync"/>,
/// which neither changes the open map nor dirties a row, and the only thing it writes back is a selection
/// when somebody clicks a cell.</para>
///
/// <para>The layout is re-walked rather than patched whenever a map reports a change, because a link edit
/// moves maps around the grid instead of repainting one. Re-walking is a dictionary walk over resident
/// records in both modes — online, the first walk caches every row it touches — so it is cheap enough to
/// do unconditionally and simpler than reasoning about which edits can move a cell.</para>
/// </summary>
public sealed partial class WorldPreviewViewModel : ObservableObject, IDisposable
{
    private readonly MapEditorViewModel _maps;
    private bool _disposed;
    private int _floodGeneration;

    /// <summary>How far out from the open map the preview walks links, in maps: a 21x21 box.
    ///
    /// <para>Fixed rather than offered. It is a reach, not a preference — the point of the window is "the
    /// region around here", and a dial that changes how much of the world is real invites tuning a number
    /// instead of reading a map. It is still stated in the status line, because a reader has to know the
    /// edge of the picture is the edge of the picture and not the edge of the world.</para></summary>
    public const int Radius = 10;

    public WorldPreviewViewModel(MapEditorViewModel maps)
    {
        _maps = maps;
        _zoom = AppSettings.Current.WorldPreviewZoom;

        _maps.PropertyChanged += OnMapEditorPropertyChanged;
        _maps.MapContentChanged += OnMapContentChanged;
        EditorStrings.LanguageChanged += ApplyStrings;
    }

    /// <summary>Maps on the grid, with the cell each one sits in. Empty until the first walk finishes.</summary>
    public MapLinkLayoutResult Layout { get; private set; } = MapLinkLayoutResult.Empty;

    /// <summary>The map group the open map belongs to; 0 for none. Maps sharing it are tinted.</summary>
    public int OriginGroup { get; private set; }

    /// <summary>The open map, drawn with its own marker. 0 when no map is open.</summary>
    public int OriginMap { get; private set; }

    /// <summary>Raised when the drawn set changed and the canvas should rebuild.</summary>
    public event Action? LayoutChanged;

    /// <summary>Raised with a map number whose art changed but whose place on the grid did not.</summary>
    public event Action<int>? MapInvalidated;

    /// <summary>Raised when the editor swapped its tile sheets and the canvas must re-read them.
    ///
    /// <para>The sheets load after the shell does, and the window can be restored open before a world is
    /// even chosen — so whatever it read at construction was an empty list. Reading them once left every map
    /// drawn as a black rectangle with a label on it.</para></summary>
    public event Action? TilesetsChanged;

    /// <summary>Tileset sheets, indexed by sheet number. The editor owns these; the canvas only reads them.</summary>
    public IReadOnlyList<Avalonia.Media.Imaging.Bitmap?> Tilesets => _maps.Tilesets;

    [ObservableProperty] private double _zoom;

    [ObservableProperty] private string _status = "";

    /// <summary>False while no world is open or the open map has no reachable maps, which is what the
    /// window shows its empty state on.</summary>
    public bool HasMaps => Layout.Placements.Count > 0;

    partial void OnZoomChanged(double value)
    {
        AppSettings.Current.WorldPreviewZoom = value;
        ApplyStrings();
    }

    /// <summary>Walks the links out from the open map and replaces the grid.</summary>
    public async Task RefreshAsync()
    {
        if (_disposed) return;
        int generation = ++_floodGeneration;

        var origin = _maps.SelectedMap;
        if (origin is null)
        {
            Layout = MapLinkLayoutResult.Empty;
            OriginMap = 0;
            OriginGroup = 0;
            Announce();
            return;
        }

        var layout = await MapLinkLayout.FloodAsync(origin.Index, Radius, _maps.ReadMapAsync);

        // A second refresh may have started and finished while this one was awaiting the wire; the newest
        // walk owns the grid.
        if (_disposed || generation != _floodGeneration) return;

        Layout = layout;
        OriginMap = origin.Index;
        OriginGroup = origin.Record.MapGroup;
        Announce();
    }

    private void Announce()
    {
        OnPropertyChanged(nameof(HasMaps));
        ApplyStrings();
        LayoutChanged?.Invoke();
    }

    private void ApplyStrings() => Status = Layout.Placements.Count == 0
        ? EditorStrings.Get(EditorStrings.WorldPreview_NoMaps)
        : EditorStrings.Format(
            Layout.TruncatedByRadius
                ? EditorStrings.WorldPreview_CountTruncated
                : EditorStrings.WorldPreview_Count,
            ("Count", Layout.Placements.Count), ("Radius", Radius),
            ("Zoom", (int)Math.Round(Zoom * 100)));

    private void OnMapEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MapEditorViewModel.SelectedMap)) _ = RefreshAsync();
        else if (e.PropertyName == nameof(MapEditorViewModel.Tilesets)) TilesetsChanged?.Invoke();
    }

    // A changed map either sits somewhere new, which needs a re-walk, or just looks different, which needs
    // one bitmap. Re-walking settles which without having to classify the edit.
    private void OnMapContentChanged(int mapNum)
    {
        if (_disposed) return;
        _ = RepaintAsync(mapNum);
    }

    private async Task RepaintAsync(int mapNum)
    {
        var before = Layout;
        await RefreshAsync();
        if (_disposed) return;
        if (SamePlaces(before, Layout)) MapInvalidated?.Invoke(mapNum);
    }

    // Same maps in the same cells. Only then is a one-map repaint enough.
    private static bool SamePlaces(MapLinkLayoutResult a, MapLinkLayoutResult b)
    {
        if (a.Placements.Count != b.Placements.Count) return false;
        var cells = a.Placements.ToDictionary(p => p.MapNum, p => (p.CellX, p.CellY));
        foreach (var p in b.Placements)
            if (!cells.TryGetValue(p.MapNum, out var cell) || cell != (p.CellX, p.CellY)) return false;
        return true;
    }

    /// <summary>Opens a map in the editor, as an ordinary selection so it joins the back/forward trail.</summary>
    [RelayCommand]
    public void OpenMap(int mapNum)
    {
        if (mapNum > 0) _maps.SelectByIndex(mapNum);
    }

    /// <summary>Shows a dialog to the caller. Set by the window; null in headless tests.</summary>
    public Func<WarpTargetsDialogViewModel, Task>? ShowWarpTargetsAsync { get; set; }

    /// <summary>Opens the warp-destination view for one map on the grid.
    ///
    /// <para>Only maps already on the grid can be asked, so the records are in hand and the previews render
    /// without a fetch. Reads go through the same side-effect-free path as the walk.</para></summary>
    public async Task ShowWarpsForAsync(int mapNum)
    {
        if (ShowWarpTargetsAsync is null) return;
        var placed = Layout.Placements.FirstOrDefault(p => p.MapNum == mapNum);
        if (placed.Record is not { } source) return;

        var vm = await WarpTargetsDialogViewModel.BuildAsync(
            mapNum, source, _maps.ReadMapAsync, _maps.Tilesets, OpenMap);
        if (_disposed) return;
        await ShowWarpTargetsAsync(vm);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _maps.PropertyChanged -= OnMapEditorPropertyChanged;
        _maps.MapContentChanged -= OnMapContentChanged;
        EditorStrings.LanguageChanged -= ApplyStrings;
    }
}

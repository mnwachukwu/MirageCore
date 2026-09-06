using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Mirage.Editor.ViewModels;

/// <summary>Canvas zoom, and the two callbacks the view hands back so the view model can invalidate
/// the tile grid without holding a reference to the control.</summary>
public sealed partial class MapEditorViewModel : ObservableObject
{
    // Canvas zoom factor: 1.0 = one screen pixel per map pixel. Bound by the view's zoom transform.
    [ObservableProperty] private double _mapZoom = 1.0;

    /// <summary>Redraws one tile at (x, y). Set by the view; null in headless tests.</summary>
    public Action<int, int>? InvalidateTileGrid { get; set; }

    /// <summary>Redraws the whole grid — for edits whose effect is not confined to one tile (a fill,
    /// a paste, a map switch, a light change). Set by the view; null in headless tests.</summary>
    public Action? InvalidateAllTiles { get; set; }

    /// <summary>A map's tiles changed in this session, carrying the map number.
    ///
    /// <para>Multicast, unlike the two invalidate callbacks above: those are a private line to the one
    /// canvas that owns them, and a second listener would overwrite the first. Anything else drawing the
    /// same maps — the World Preview window — listens here instead.</para>
    ///
    /// <para>Held back while a paint batch is open and raised once at <c>CommitBatch</c>, so a drag across
    /// forty tiles is one notification rather than forty.</para></summary>
    public event Action<int>? MapContentChanged;

    // Repaints one tile and reports the map as changed.
    private void RepaintTile(int x, int y)
    {
        InvalidateTileGrid?.Invoke(x, y);
        NotifyMapContentChanged();
    }

    // Repaints the whole grid and reports the map as changed.
    private void RepaintMap()
    {
        InvalidateAllTiles?.Invoke();
        NotifyMapContentChanged();
    }

    private void NotifyMapContentChanged()
    {
        if (_batchOpen) return;
        // A warp edited anywhere can change what arrives on the open map, including a warp on the open map
        // pointing back at itself.
        InvalidateInboundWarps();
        if (SelectedMap is { } row) MapContentChanged?.Invoke(row.Index);
    }

    /// <summary>Reports a change to a map that is not the open one — a link edit rewriting a neighbor's
    /// side, or another session's save arriving over the wire.</summary>
    internal void RaiseMapContentChanged(int mapNum)
    {
        if (mapNum <= 0) return;
        InvalidateInboundWarps();
        MapContentChanged?.Invoke(mapNum);
    }

    // Zoom steps by a factor of 1.5 per press and clamps at 1/8x .. 4x, so the extremes stay
    // reachable in a few presses without the canvas becoming unusable at either end.
    [RelayCommand]
    private void ZoomIn() => MapZoom = Math.Min(MapZoom * 1.5, 4.0);

    [RelayCommand]
    private void ZoomOut() => MapZoom = Math.Max(MapZoom / 1.5, 0.125);

    [RelayCommand]
    private void ZoomReset() => MapZoom = 1.0;
}

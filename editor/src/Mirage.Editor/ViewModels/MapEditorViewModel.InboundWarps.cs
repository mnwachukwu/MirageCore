using CommunityToolkit.Mvvm.ComponentModel;
using Mirage.Editor.Localization;
using Mirage.Editor.Services;
using Mirage.Shared;
using Mirage.Shared.Records;

namespace Mirage.Editor.ViewModels;

/// <summary>Who warps INTO the open map, and where they land.</summary>
public sealed partial class MapEditorViewModel : ObservableObject
{
    private IReadOnlyList<InboundWarp>? _inboundWarps;

    /// <summary>
    /// Arrival points on the open map: every tile another map's warp lands on, with the maps that send
    /// somebody there.
    ///
    /// <para>A warp is authored entirely on the departing map, so until now the receiving map showed no sign
    /// that anything opened onto it — an author could wall off or repurpose a tile that three other maps
    /// drop players onto and see nothing. This is that missing half.</para>
    ///
    /// <para>Computed on demand and cached until a map changes. Online it can only see maps already fetched,
    /// so it reports what is knowable rather than claiming a map has no arrivals.</para>
    /// </summary>
    public IReadOnlyList<InboundWarp> InboundWarps =>
        _inboundWarps ??= SelectedMap is { } row
            ? WarpLinks.InboundTo(row.Index, ReadableMaps())
            : [];

    /// <summary>Arrivals on the plane being authored, which is what the canvas marks.</summary>
    public IReadOnlyList<InboundWarp> InboundWarpsOnActiveLayer =>
        [.. InboundWarps.Where(w => w.Layer == SelectedAttributeLayer)];

    // Offline every map is resident; online only the rows already fetched can be read, and an unloaded row
    // carries a placeholder record whose tiles are not the map's.
    private IEnumerable<(int Num, MapRecord Map)> ReadableMaps()
    {
        if (!_data.IsOnline)
        {
            for (int i = 1; i < _data.OfflineMaps.Length; i++)
                if (_data.OfflineMaps[i] is { } m) yield return (i, m);
            yield break;
        }
        foreach (var row in Maps)
            if (row.IsLoaded) yield return (row.Index, row.Record);
    }

    private void InvalidateInboundWarps()
    {
        _inboundWarps = null;
        OnPropertyChanged(nameof(InboundWarps));
        OnPropertyChanged(nameof(InboundWarpsOnActiveLayer));
        OnPropertyChanged(nameof(HoveredHasInboundWarps));
        OnPropertyChanged(nameof(HoveredInboundWarpText));
    }

    // Arrivals on the hovered tile, on the plane being authored.
    private InboundWarp? HoveredInboundWarp =>
        InMapBounds(HoveredX, HoveredY)
            ? InboundWarps.FirstOrDefault(w =>
                w.X == HoveredX && w.Y == HoveredY && w.Layer == SelectedAttributeLayer)
            : null;

    public bool HoveredHasInboundWarps => HoveredInboundWarp is not null;

    /// <summary>The full list of maps arriving on the hovered tile.
    ///
    /// <para>The marker on the map can only carry one number and a count; this is where the rest of them are
    /// named, because "three maps land here" is not answerable by looking harder at a badge.</para></summary>
    public string HoveredInboundWarpText => HoveredInboundWarp is { } w
        ? EditorStrings.Format(EditorStrings.MapEditor_InboundWarpText,
            ("Maps", string.Join(", ", w.SourceMaps.Select(n => MapLabel((short)n)))),
            ("Warps", w.WarpCount))
        : "";
}

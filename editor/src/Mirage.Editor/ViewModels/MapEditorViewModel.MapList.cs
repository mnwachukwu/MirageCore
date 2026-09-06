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
using Mirage.Shared.Serialization;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json;
namespace Mirage.Editor.ViewModels;

/// <summary>The map list: the row collection, the name filter over it, the per-row subscriptions,
/// and the dirty flags the save commands read.</summary>
public sealed partial class MapEditorViewModel : ObservableObject
{
    // Tracks the map row we've subscribed to for Record changes
    private MapRowViewModel? _subscribedMap;
    private readonly List<MapRowViewModel> _subscribedMapRows = [];

    public bool IsSelectedMapDirty => SelectedMap is not null && SelectedMap.IsDirty;
    public bool HasAnyDirtyMap => Maps.Any(m => m.IsDirty);

    private void NotifyMapDirtyState()
    {
        OnPropertyChanged(nameof(IsSelectedMapDirty));
        OnPropertyChanged(nameof(HasAnyDirtyMap));
        OnPropertyChanged(nameof(CanCopyMap));
        OnPropertyChanged(nameof(CopyMapTooltip));
    }

    private void HookMaps()
    {
        Maps.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var row in _subscribedMapRows)
                    row.PropertyChanged -= OnMapItemPropertyChanged;
                _subscribedMapRows.Clear();
                NotifyMapDirtyState();
                OnPropertyChanged(nameof(FilteredMaps));
                OnPropertyChanged(nameof(FilterStatus));
                return;
            }
            if (e.NewItems is not null)
            {
                foreach (MapRowViewModel row in e.NewItems.Cast<MapRowViewModel>())
                {
                    row.PropertyChanged += OnMapItemPropertyChanged;
                    _subscribedMapRows.Add(row);
                }
            }

            if (e.OldItems is not null)
            {
                foreach (MapRowViewModel row in e.OldItems.Cast<MapRowViewModel>())
                {
                    row.PropertyChanged -= OnMapItemPropertyChanged;
                    _subscribedMapRows.Remove(row);
                }
            }

            NotifyMapDirtyState();
            OnPropertyChanged(nameof(FilteredMaps));
            OnPropertyChanged(nameof(FilterStatus));
        };
    }

    private void OnMapItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != "IsDirty") return;
        NotifyMapDirtyState();
        ClaimOrReleaseLock(sender);
    }

    public ObservableCollection<MapRowViewModel> Maps { get; } = [];
    public IEnumerable<MapRowViewModel> FilteredMaps =>
        string.IsNullOrEmpty(FilterText)
            ? Maps
            : Maps.Where(m => m.DisplayName.Contains(FilterText, StringComparison.OrdinalIgnoreCase));
    public bool IsFilterActive => !string.IsNullOrEmpty(FilterText);
    public string FilterStatus => EditorStrings.Format(EditorStrings.Status_FilterCount,
        ("Filtered", FilteredMaps.Count()), ("Total", Maps.Count));
    [RelayCommand] private void ClearFilter() => FilterText = "";

    // ── Copy ──────────────────────────────────────────────────────────────────

    /// <summary>Whether Copy can run: a map is open and it is not itself blank.
    /// <para>The free-slot search is deliberately left until the button is pressed. This is re-read on
    /// every dirty notification — which includes every painted tile — and <see cref="IsBlankMap"/> walks a
    /// whole map to answer, so scanning a thousand of them here would cost a scan per brush stroke.
    /// Checking the OPEN map is one walk, and it is the case an author actually hits.</para></summary>
    public bool CanCopyMap => SelectedMap is { } m && !IsBlankMap(m);

    /// <summary>What the map editor's Copy button says on hover: why it cannot run, or what it does.</summary>
    public string CopyMapTooltip =>
        SelectedMap is null ? EditorStrings.Get(EditorStrings.Common_CopyNeedsSelection)
        : IsBlankMap(SelectedMap) ? EditorStrings.Get(EditorStrings.Common_CopyNeedsRecord)
        : EditorStrings.Get(EditorStrings.Common_CopyTooltip);

    /// <summary>An unused map slot: unnamed, nothing painted, nothing placed. Stricter than the name-only
    /// test the other editors use, because a map can hold a fully authored layout and no name at all —
    /// and the list would still label that slot "(empty)".</summary>
    private static bool IsBlankMap(MapRowViewModel row)
    {
        var m = row.Record;
        if (!string.IsNullOrWhiteSpace(m.Name) || !string.IsNullOrWhiteSpace(m.DisplayName)) return false;
        if (m.Npcs.Count > 0 || m.Lights.Count > 0) return false;
        for (int y = 0; y < m.Height; y++)
        {
            for (int x = 0; x < m.Width; x++)
            {
                var t = m.Tile[x, y];
                if (t.Type != TileType.Walkable || t.FringeAttr is not null) return false;
                foreach (int cell in t.Ground) if (!LayerCell.IsEmpty(cell)) return false;
                foreach (int cell in t.Fringe) if (!LayerCell.IsEmpty(cell)) return false;
                foreach (int cell in t.Canopy) if (!LayerCell.IsEmpty(cell)) return false;
            }
        }
        return true;
    }

    /// <summary>Duplicate the open map into the first blank slot and select it.
    ///
    /// <para>The four neighbor links are dropped. They are the one part of a map that cannot be copied:
    /// each names a map whose own link still points back at the ORIGINAL, so a copy that kept them would
    /// claim an adjacency the other side does not agree with. Warps, boot point and group membership are
    /// kept — those are properties of the map rather than edges of the neighbor graph.</para>
    ///
    /// <para>The revision resets to zero rather than being carried over. It counts saves of THIS slot, and
    /// the copy has never been saved; the first save takes it to 1. Clients compare revisions for equality
    /// (<c>cachedRev == p.Revision</c>), never for order, so a number lower than the one they hold still
    /// reads as "not what I cached" and refetches.</para></summary>
    [RelayCommand]
    private async Task CopyMapAsync()
    {
        if (SelectedMap is null) return;
        var source = SelectedMap;

        // Online a row can be a name-only placeholder; copying one would duplicate a blank map.
        if (_data.IsOnline && !source.IsLoaded)
        {
            var pkt = await _conn.RequestMapAsync(source.Index);
            if (pkt is not null) source.LoadRecord(EditorDataService.MapRecordFromPacket(pkt));
        }

        var target = Maps.FirstOrDefault(IsBlankMap);
        if (target is null)
        {
            StatusMessage = EditorStrings.Get(EditorStrings.MapEditorStatus_NoEmptyMapSlot);
            return;
        }

        var copy = JsonSerializer.Deserialize<MapRecord>(
            JsonSerializer.Serialize(source.Record, RecordJson.Options), RecordJson.Options)!;
        copy.Name += RecordCopy.Suffix;
        copy.Up = copy.Down = copy.Left = copy.Right = 0;
        copy.Revision = 0;

        target.CopyFromRecord(copy);
        SelectedMap = target;
        NotifyMapDirtyState();
        StatusMessage = EditorStrings.Format(EditorStrings.MapEditorStatus_MapCopied,
            ("From", source.Index), ("To", target.Index));
    }
}

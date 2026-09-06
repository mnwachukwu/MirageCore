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

/// <summary>The selected maps NPC roster rows: adding, removing and reindexing them, writing an
/// NPC type into a row, and the pinned-footprint lookups a live NPC resize invalidates.</summary>
public sealed partial class MapEditorViewModel : ObservableObject
{
    // Per-map dynamic NPC list: one facade row per Record.Npcs entry. "+" adds a row (up to
    // MaxMapNpcs), each row's "−" removes it, and only non-empty rows are saved — so a reload restores exactly the
    // authored rows. Rebuilt on map switch; refreshed on entry-list change. Only authored rows are listed,
    // never the full 1..MaxMapNpcs slot range.
    public ObservableCollection<MapNpcRowViewModel> MapNpcRows { get; } = new();

    /// <summary>True when the map has no NPC rows yet — drives the editor's empty-state hint.</summary>
    public bool HasNoNpcRows => MapNpcRows.Count == 0;

    // Localized placeholder for every NPC-row autocomplete box (rows delegate here so it stays one source).
    public string NpcSlotPlaceholder => EditorStrings.Get(EditorStrings.MapEditor_SearchNpcsPlaceholder);

    // Localized tooltip for each row's "place on map" button (rows delegate here).
    public string NpcPlaceTooltip => EditorStrings.Get(EditorStrings.MapEditor_PlaceNpcTooltip);

    // NPC footprint size (EffectiveSize) for the size-aware placement overlay + validation.
    // A method group so it binds to TileGridControl.NpcSizeLookup and MapNpcPlacement.ValidatePin's size Func.
    public int NpcSize(int npcNum) => _data.NpcSize(npcNum);

    // True when (x,y) lies under any pinned NPC entry's SxS footprint on this map. A placed NPC reserves its
    // whole footprint (top-left anchor), so tile attributes can't be written under it — collision is
    // bidirectional (MapNpcPlacement.ValidatePin already blocks the reverse: pinning onto a non-Walkable
    // attribute tile, since it requires the whole footprint to be Walkable).
    private bool TileCoveredByPinnedFootprint(MapRecord map, int x, int y)
    {
        foreach (var e in map.Npcs)
        {
            if (e.HasPin && WorldCoordHelper.FootprintContains(e.PinX!.Value, e.PinY!.Value, NpcSize(e.Npc), x, y))
                return true;
        }

        return false;
    }

    // True when any pinned entry on this map spawns NPC #npcNum — used to scope the resize re-prompt.
    private static bool MapPinsNpc(MapRecord map, int npcNum)
    {
        foreach (var e in map.Npcs)
            if (e.HasPin && e.Npc == npcNum) return true;
        return false;
    }

    // A live NPC-size change arrived (any editor's NPC save → server SendToAll(UpdateNpc); marshaled to the UI
    // thread by MainWindowViewModel). OnlineNpcSizes is otherwise a connect-time snapshot, so refresh the size the
    // footprint overlay + placement validation read, redraw the active map, and flag every LOADED map that pins
    // this NPC dirty — a resize can turn a prior-valid pin into an off-map/overlap, so the author is re-prompted
    // to re-validate + re-save (re-saving runs the HandleEditorSaveMap backstop, which drops any now-invalid pin).
    public void OnNpcLiveUpdated(int npcNum, int newSize)
    {
        _data.UpdateOnlineNpcSize(npcNum, newSize);

        if (SelectedMap is not null && MapPinsNpc(SelectedMap.Record, npcNum))
            RepaintMap();   // full render-cache rebuild so footprints redraw at the new size

        foreach (var m in Maps)
        {
            if (m.IsLoaded && MapPinsNpc(m.Record, npcNum))
                m.MarkDirty();
        }
    }

    // The NPC-type entry at row `index` (0-based), or null if empty / out of range.
    public NamedEntry? NpcEntryForRow(int index)
    {
        var npcs = SelectedMap?.Record.Npcs;
        return npcs is not null && index >= 0 && index < npcs.Count
            ? EntryFor(_data.LiveNpcEntries, npcs[index].Npc) : null;
    }

    // Write the NPC TYPE into row `index`. Clearing it (id 0) also drops the row's fixed-spawn pin (an empty row
    // can't stay pinned). Marks the map dirty + refreshes the row; a dropped pin is cleared from the grid.
    public void SetRowNpc(int index, NamedEntry? value)
    {
        if (SelectedMap is null) return;
        var npcs = SelectedMap.Record.Npcs;
        if (index < 0 || index >= npcs.Count) return;
        int id = value?.Id ?? 0;
        var cur = npcs[index];
        if (cur.Npc == id) return;
        if (id == 0)
        {
            npcs[index] = cur with { Npc = 0, PinX = null, PinY = null };
            if (cur.HasPin) RepaintTile(cur.PinX!.Value, cur.PinY!.Value);
        }
        else
        {
            npcs[index] = cur with { Npc = id };
        }

        SelectedMap.MarkDirty();
        RowAt(index)?.Refresh();
    }

    // "@ x,y" when row `index` is pinned to a fixed spawn tile (else "" = spawns randomly) — the row read-out.
    public string NpcPlacementLabel(int index)
    {
        var npcs = SelectedMap?.Record.Npcs;
        if (npcs is null || index < 0 || index >= npcs.Count) return "";
        var e = npcs[index];
        return e.HasPin ? $"@ {e.PinX},{e.PinY}" : "";
    }

    private MapNpcRowViewModel? RowAt(int index)
        => index >= 0 && index < MapNpcRows.Count ? MapNpcRows[index] : null;

    // Add an empty NPC row (appended, so existing indices don't shift). Capped at MaxMapNpcs runtime posts.
    [RelayCommand(CanExecute = nameof(CanAddNpcRow))]
    private void AddNpcRow()
    {
        if (SelectedMap is null) return;
        SelectedMap.Record.Npcs.Add(new MapNpcEntry(0, null, null));
        MapNpcRows.Add(new MapNpcRowViewModel(this, MapNpcRows.Count));
        SelectedMap.MarkDirty();
        OnPropertyChanged(nameof(HasNoNpcRows));
        AddNpcRowCommand.NotifyCanExecuteChanged();
    }

    private bool CanAddNpcRow() => SelectedMap is not null && MapNpcRows.Count < Constants.MaxMapNpcs;

    // Remove a row (and its entry). Later rows slide down a post, so any tile-undo pin ops keyed by the old
    // indices are cleared — a deliberate structural edit, like the shop's non-undoable trade-row removal.
    [RelayCommand]
    private void RemoveNpcRow(MapNpcRowViewModel row)
    {
        if (SelectedMap is null) return;
        int index = row.Index;
        var npcs = SelectedMap.Record.Npcs;
        if (index < 0 || index >= npcs.Count) return;
        PlacingNpcRow = -1;   // a row removal shifts indices — abandon any in-progress placement
        npcs.RemoveAt(index);
        MapNpcRows.RemoveAt(index);
        ReindexNpcRows();
        AdjustPinOpsAfterRemoval(index);   // shift/void queued pin ops in place so the tile-undo history survives
        SelectedMap.MarkDirty();
        OnPropertyChanged(nameof(HasNoNpcRows));
        AddNpcRowCommand.NotifyCanExecuteChanged();
        // The reindex shifted every later pin's post-number label (and dropped the removed row's pin), so redraw
        // all markers rather than a single tile.
        RepaintMap();
    }

    // Re-stamp each row's Index to its list position after a removal so the facades track the shifted entries.
    private void ReindexNpcRows()
    {
        for (int i = 0; i < MapNpcRows.Count; i++)
        {
            MapNpcRows[i].Index = i;
            MapNpcRows[i].Refresh();
        }
    }

    // Rebuild the row facades from the selected map's entries (called on map switch).
    private void RebuildMapNpcRows()
    {
        PlacingNpcRow = -1;   // switching maps invalidates a row-bound placement in progress
        MapNpcRows.Clear();
        var npcs = SelectedMap?.Record.Npcs;
        if (npcs is not null)
        {
            for (int i = 0; i < npcs.Count; i++)
                MapNpcRows.Add(new MapNpcRowViewModel(this, i));
        }

        OnPropertyChanged(nameof(HasNoNpcRows));
        AddNpcRowCommand.NotifyCanExecuteChanged();
    }
}

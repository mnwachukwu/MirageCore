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

/// <summary>Moving between maps: back/forward history, and the placed-light and NPC-spawn-pin
/// lookups keyed by tile and layer.</summary>
public sealed partial class MapEditorViewModel : ObservableObject
{
    // ── Map navigation history ────────────────────────────────────────────────
    // Browser-style back/forward across SelectedMap changes. Pushed automatically
    // in OnSelectedMapChanged; the _isNavigatingHistory guard suppresses the push
    // when Back/Forward themselves drive the change.
    private readonly Stack<int> _navBack = new();
    private readonly Stack<int> _navFwd = new();
    private bool _isNavigatingHistory;

    public bool CanNavigateBack => _navBack.Count > 0;
    public bool CanNavigateForward => _navFwd.Count > 0;

    /// <summary>The hinge for everything derived from the current map. Selection alone raises no
    /// property change on the record, so without this the derived state simply never refreshes.
    /// <para>Re-running <see cref="NotifyMapProperties"/> matters most when the row is ALREADY
    /// loaded: <see cref="MapRowViewModel.LoadRecord"/> announces itself only on a fetch, so
    /// selecting an eagerly-loaded map would otherwise leave the NPC-spawn rows showing the
    /// previous map's entries and leave <c>AddNpcRowCommand</c> stuck at whatever
    /// <c>CanExecute</c> returned when nothing was selected — i.e. permanently disabled.</para></summary>
    partial void OnSelectedMapChanged(MapRowViewModel? oldValue, MapRowViewModel? newValue)
    {
        OnPropertyChanged(nameof(IsSelectedLocked));
        // Undo/Redo are gated on the open map's lock, so switching maps re-decides them.
        UpdateUndoRedo();
        // Arrivals belong to the map being viewed, so they are re-read for the new one.
        InvalidateInboundWarps();
        // Browser-style history: a normal switch records where we came from and forks the trail.
        // Back/Forward drive SelectedMap themselves, and set _isNavigatingHistory so their own
        // writes don't re-push what they just popped.
        if (!_isNavigatingHistory && oldValue is not null && oldValue != newValue)
        {
            _navBack.Push(oldValue.Index);
            _navFwd.Clear();
        }
        UpdateNavCommands();

        // Follow the selection with the Record subscription. A lazy fetch swaps the record in
        // place and announces it via PropertyChanged(Record); OnMapRowPropertyChanged is what
        // turns that into a NotifyMapProperties, so without this the panel never catches up.
        if (!ReferenceEquals(_subscribedMap, newValue))
        {
            if (_subscribedMap is not null) _subscribedMap.PropertyChanged -= OnMapRowPropertyChanged;
            _subscribedMap = newValue;
            if (_subscribedMap is not null) _subscribedMap.PropertyChanged += OnMapRowPropertyChanged;
        }

        NotifyMapProperties();
        // Dirtiness is a property of the SELECTED map, so switching maps changes the answer without any
        // row raising anything. Save's IsEnabled binds to it, and stale it reads as the previous map's
        // state: enabled over a clean map, disabled over the dirty one you just came back to.
        NotifyMapDirtyState();
        UpdateUndoRedo();

        // A placeholder row carries no tiles until it is fetched; selecting it is the lazy-load
        // trigger (offline maps are all resident, so there is nothing to request).
        if (newValue is { IsLoaded: false } && _data.IsOnline)
            _ = LoadMapAsync(newValue);
    }

    private void UpdateNavCommands()
    {
        NavigateBackCommand.NotifyCanExecuteChanged();
        NavigateForwardCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanNavigateBack))]
    private void NavigateBack()
    {
        if (_navBack.Count == 0) return;
        var target = PopValidTarget(_navBack);
        if (target is null)
        {
            UpdateNavCommands();
            return;
        }
        if (SelectedMap is not null) _navFwd.Push(SelectedMap.Index);
        _isNavigatingHistory = true;
        SelectedMap = target;
        _isNavigatingHistory = false;
        UpdateNavCommands();
    }

    [RelayCommand(CanExecute = nameof(CanNavigateForward))]
    private void NavigateForward()
    {
        if (_navFwd.Count == 0) return;
        var target = PopValidTarget(_navFwd);
        if (target is null)
        {
            UpdateNavCommands();
            return;
        }
        if (SelectedMap is not null) _navBack.Push(SelectedMap.Index);
        _isNavigatingHistory = true;
        SelectedMap = target;
        _isNavigatingHistory = false;
        UpdateNavCommands();
    }

    /// <summary>Open a map by number, as an ordinary selection. Deliberately NOT a history-suppressed jump: a
    /// link followed in from another section should land on the history trail like any other map switch, so Back
    /// returns to the map you were on and Forward returns to the one the link opened. Returns false when the
    /// number names no row, so a caller can leave the current section alone rather than switching to nothing.</summary>
    public bool SelectByIndex(int mapNum)
    {
        var row = RowFor(mapNum);
        if (row is null) return false;
        SelectedMap = row;
        return true;
    }

    // Pops until a row that still exists is found; collapses no-op entries that
    // equal the current map (can happen after Maps is rebuilt on reconnect).
    private MapRowViewModel? PopValidTarget(Stack<int> stack)
    {
        while (stack.Count > 0)
        {
            int id = stack.Pop();
            var row = RowFor(id);
            if (row is not null && row != SelectedMap) return row;
        }
        return null;
    }

    /// <summary>Called by TileGridControl on Ctrl+Alt+Shift + left-click on an active-map warp tile.</summary>
    [RelayCommand]
    public void WarpDestinationClicked((short MapId, ushort X, ushort Y) warp)
    {
        if (warp.MapId <= 0) return;
        var target = RowFor(warp.MapId);
        EditorLog.Info("Warp-tile navigation to map {Map} at {X},{Y}.", warp.MapId, warp.X, warp.Y);
        if (target is null || target == SelectedMap) return;
        SelectedMap = target;
    }

    // ── Placed-light helpers (at most one light per tile PER LAYER — a ground torch under a bridge and a fringe
    // lamp on the deck may share a tile) ──────────────────────────────────────
    private static int FindLightIndex(MapRecord map, int x, int y, WorldLayer layer)
    {
        for (int i = 0; i < map.Lights.Count; i++)
            if (map.Lights[i].X == x && map.Lights[i].Y == y && map.Lights[i].Layer == layer) return i;
        return -1;
    }

    private static PlacedLight? LightAt(MapRecord map, int x, int y, WorldLayer layer)
    {
        int i = FindLightIndex(map, x, y, layer);
        return i >= 0 ? map.Lights[i] : null;
    }

    // Sets the light at (x,y) on `layer`: removes any existing one on that layer, then adds `value` when present.
    private static void SetLightSlot(MapRecord map, int x, int y, WorldLayer layer, PlacedLight? value)
    {
        int i = FindLightIndex(map, x, y, layer);
        if (i >= 0) map.Lights.RemoveAt(i);
        if (value is { } pl) map.Lights.Add(pl);
    }

    // ── Fixed NPC-spawn pin helpers (pin lives on the entry; at most one pin per tile PER LAYER — a ground mob
    // under a bridge and a mob on the deck may share a tile, like placed lights) ────────────────
    // The index of the Npcs entry pinned at (x,y) on `layer`, or null for none.
    private static int? EntryPinnedAt(MapRecord map, int x, int y, WorldLayer layer)
    {
        for (int i = 0; i < map.Npcs.Count; i++)
            if (map.Npcs[i].PinX == x && map.Npcs[i].PinY == y && map.Npcs[i].PinLayer == layer) return i;
        return null;
    }

    // Sets the pin at (x,y) on `layer`: clears whichever entry currently owns that (tile,layer), then pins
    // `entryIndex` there (when given). Positional by (tile,layer) (like SetLightSlot) so per-cell undo is exact and
    // a Ground pin + a Fringe pin can coexist on one tile; the one-pin-per-entry invariant is held by the entry
    // points (the picker offers only unpinned rows). Entries are readonly record structs, so `with` replaces them.
    private static void SetEntryPinAt(MapRecord map, int x, int y, WorldLayer layer, int? entryIndex)
    {
        for (int i = 0; i < map.Npcs.Count; i++)
        {
            if (map.Npcs[i].PinX == x && map.Npcs[i].PinY == y && map.Npcs[i].PinLayer == layer)
                map.Npcs[i] = map.Npcs[i] with { PinX = null, PinY = null };
        }

        if (entryIndex is int idx && idx >= 0 && idx < map.Npcs.Count)
            map.Npcs[idx] = map.Npcs[idx] with { PinX = x, PinY = y, PinLayer = layer };
    }

    // Refresh the authoring-list row at `index` so its "@ x,y" read-out tracks placement changes.
    private void RefreshNpcRow(int? index) { if (index is int i) RowAt(i)?.Refresh(); }
}

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

/// <summary>Entity-picker autocomplete for the map properties and the warp/item/key dialogs —
/// resolving a typed name to an item, NPC, map or shop id.</summary>
public sealed partial class MapEditorViewModel : ObservableObject
{
    // ── Entity-picker autocomplete lists ────────────────────────────────────
    public NamedEntry[] MapEntries => _data.LiveMapEntries;
    public NamedEntry[] NpcEntries => _data.LiveNpcEntries;
    public NamedEntry[] ItemEntries => _data.LiveItemEntries;
    public NamedEntry[] MapGroupEntries => _data.LiveMapGroupEntries;

    private static NamedEntry? EntryFor(NamedEntry[] entries, int id) =>
        id > 0 && id < entries.Length ? entries[id] : null;

    // Helper: write an entity field, mark dirty, and raise the given property names.
    private void SetMapEntityField(Action<int> write, int current, NamedEntry? value, params string[] props)
    {
        var id = value?.Id ?? 0;
        if (SelectedMap is null || current == id) return;
        write(id);
        SelectedMap.MarkDirty();
        foreach (var p in props) OnPropertyChanged(p);
    }

    // ── Selected entity pickers for map properties ───────────────────────────
    public NamedEntry? SelectedMapUp
    {
        get => EntryFor(_data.LiveMapEntries, MapUp);
        set
        {
            var oldId = MapUp;
            var newId = value?.Id ?? 0;
            SetMapEntityField(id => SelectedMap!.Record.Up = id, MapUp, value,
                nameof(SelectedMapUp), nameof(NeighborMapUp), nameof(NeighborMapUpLeft), nameof(NeighborMapUpRight));
            if (_data.IsOnline) _ = EagerLoadNeighborsAsync();
            if (oldId != newId && SelectedMap is not null)
                _ = HandleDirectionChangeAsync(MapDirection.Up, newId, oldId);
        }
    }
    public NamedEntry? SelectedMapDown
    {
        get => EntryFor(_data.LiveMapEntries, MapDown);
        set
        {
            var oldId = MapDown;
            var newId = value?.Id ?? 0;
            SetMapEntityField(id => SelectedMap!.Record.Down = id, MapDown, value,
                nameof(SelectedMapDown), nameof(NeighborMapDown), nameof(NeighborMapDownLeft), nameof(NeighborMapDownRight));
            if (_data.IsOnline) _ = EagerLoadNeighborsAsync();
            if (oldId != newId && SelectedMap is not null)
                _ = HandleDirectionChangeAsync(MapDirection.Down, newId, oldId);
        }
    }
    public NamedEntry? SelectedMapLeft
    {
        get => EntryFor(_data.LiveMapEntries, MapLeft);
        set
        {
            var oldId = MapLeft;
            var newId = value?.Id ?? 0;
            SetMapEntityField(id => SelectedMap!.Record.Left = id, MapLeft, value,
                nameof(SelectedMapLeft), nameof(NeighborMapLeft), nameof(NeighborMapUpLeft), nameof(NeighborMapDownLeft));
            if (_data.IsOnline) _ = EagerLoadNeighborsAsync();
            if (oldId != newId && SelectedMap is not null)
                _ = HandleDirectionChangeAsync(MapDirection.Left, newId, oldId);
        }
    }
    public NamedEntry? SelectedMapRight
    {
        get => EntryFor(_data.LiveMapEntries, MapRight);
        set
        {
            var oldId = MapRight;
            var newId = value?.Id ?? 0;
            SetMapEntityField(id => SelectedMap!.Record.Right = id, MapRight, value,
                nameof(SelectedMapRight), nameof(NeighborMapRight), nameof(NeighborMapUpRight), nameof(NeighborMapDownRight));
            if (_data.IsOnline) _ = EagerLoadNeighborsAsync();
            if (oldId != newId && SelectedMap is not null)
                _ = HandleDirectionChangeAsync(MapDirection.Right, newId, oldId);
        }
    }
    public NamedEntry? SelectedMapBootMap
    {
        get => EntryFor(_data.LiveMapEntries, MapBootMap);
        set => SetMapEntityField(id => SelectedMap!.Record.BootMap = id, MapBootMap, value, nameof(SelectedMapBootMap));
    }
    public NamedEntry? SelectedMapGroup
    {
        get => EntryFor(_data.LiveMapGroupEntries, MapGroup);
        set
        {
            SetMapEntityField(id => SelectedMap!.Record.MapGroup = id, MapGroup, value, nameof(SelectedMapGroup));
            NotifyInheritedPlaceholders();
        }
    }

    /// <summary>The X beside each picker. Routed through the property setters rather than writing the
    /// record, so clearing a direction still takes the target's back-link with it.</summary>
    [RelayCommand]
    private void ClearMapEntity(string? which)
    {
        switch (which)
        {
            case "Up": SelectedMapUp = null; break;
            case "Down": SelectedMapDown = null; break;
            case "Left": SelectedMapLeft = null; break;
            case "Right": SelectedMapRight = null; break;
            case "BootMap": SelectedMapBootMap = null; break;
            case "MapGroup": SelectedMapGroup = null; break;
        }
    }
    // (Per-slot NPC-type pickers now live in the MapNpcSlots row collection above — see SetMapNpcSlot.)

    // ── Dialog entity pickers (warp/item/key attribute dialogs) ─────────────
    public NamedEntry? SelectedWarpMapEntry
    {
        get => EntryFor(_data.LiveMapEntries, WarpMapNum);
        set
        {
            var id = (short)(value?.Id ?? 0);
            if (WarpMapNum == id) return;
            WarpMapNum = id;
        }
    }
    partial void OnWarpMapNumChanged(short value) => OnPropertyChanged(nameof(SelectedWarpMapEntry));

    public NamedEntry? SelectedItemTileEntry
    {
        get => EntryFor(_data.LiveItemEntries, ItemTileNum);
        set
        {
            var id = (short)(value?.Id ?? 0);
            if (ItemTileNum == id) return;
            ItemTileNum = id;
        }
    }
    partial void OnItemTileNumChanged(short value)
    {
        OnPropertyChanged(nameof(SelectedItemTileEntry));
        OnPropertyChanged(nameof(ItemTileValueMax));
    }

    // Currency ground items stack, so any quantity is valid; every other item type is a single item on the
    // tile, so the quantity is capped at 1. Drives the quantity spinner's Maximum (ConfirmItem re-checks).
    public int ItemTileValueMax => _data.IsCurrencyItem(ItemTileNum) ? 9999 : 1;

    public NamedEntry? SelectedKeyItemEntry
    {
        get => EntryFor(_data.LiveItemEntries, KeyItemNum);
        set
        {
            var id = (short)(value?.Id ?? 0);
            if (KeyItemNum == id) return;
            KeyItemNum = id;
        }
    }
    partial void OnKeyItemNumChanged(short value) => OnPropertyChanged(nameof(SelectedKeyItemEntry));
}

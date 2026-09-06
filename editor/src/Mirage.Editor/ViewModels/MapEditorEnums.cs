using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Models;
using Mirage.Shared;
using Mirage.Shared.Records;
namespace Mirage.Editor.ViewModels;

/// <summary>One cell of the hovered-tile exploded preview: a layer's tile drawn from its own sheet.
/// <paramref name="SheetText"/> is the source sheet index (blank for an empty layer).
/// <paramref name="IsHidden"/> marks a layer the canvas is not currently drawing — it is still reported,
/// because a layer left out of this list reads as a layer with nothing on it.</summary>
public sealed record HoveredLayerPreview(string Label, Bitmap? Bitmap, int TileIndex, string SheetText,
    bool IsHidden = false)
{
    /// <summary>Fades a hidden layer's cell so the readout shows what is there and that it is put away.</summary>
    public double CellOpacity => IsHidden ? 0.35 : 1.0;
}

/// <summary>One row of the tile-animation editor: an occupied layer with a toggleable Anim flag.
/// <see cref="Type"/> + <see cref="ArrayIndex"/> locate it back in the tile's Ground/Fringe stack.</summary>
public sealed partial class AnimLayerRow : ObservableObject
{
    public LayerType Type { get; }
    public int ArrayIndex { get; }        // 0-based index within its Ground/Fringe stack
    public string Label { get; }          // e.g. "Ground 3"
    public Bitmap? Bitmap { get; }        // the source sheet
    public int TileIndex { get; }         // 1-based tile within the sheet
    [ObservableProperty] private bool _isAnim;
    public AnimLayerRow(LayerType type, int arrayIndex, string label, Bitmap? bitmap, int tileIndex, bool isAnim)
    {
        Type = type;
        ArrayIndex = arrayIndex;
        Label = label;
        Bitmap = bitmap;
        TileIndex = tileIndex;
        _isAnim = isAnim;
    }
}

/// <summary>One row of the map's dynamic NPC list: a facade over the VM's
/// MapRecord.Npcs[Index]. It picks the NPC TYPE spawned at this row's runtime post (Index + 1) and reads out
/// the row's optional fixed-spawn pin. The VM keeps <see cref="Index"/> in step with the row's list position
/// after an add/remove and refreshes rows on map switch, entry-list change, and pin change.</summary>
public sealed partial class MapNpcRowViewModel : ObservableObject
{
    private readonly MapEditorViewModel _vm;
    // 0-based position in the list = runtime spawn post Index + 1. The VM re-stamps this after a removal.
    public int Index { get; set; }
    public MapNpcRowViewModel(MapEditorViewModel vm, int index)
    {
        _vm = vm;
        Index = index;
    }

    public string SlotLabel => (Index + 1).ToString();
    public string Placeholder => _vm.NpcSlotPlaceholder;
    public string PlaceTooltip => _vm.NpcPlaceTooltip;
    public NamedEntry[] NpcEntries => _vm.NpcEntries;

    public NamedEntry? SelectedNpc
    {
        get => _vm.NpcEntryForRow(Index);
        set => _vm.SetRowNpc(Index, value);
    }

    // "@ x,y" when this row is pinned to a fixed spawn tile (else "" = random spawn).
    public string PlacementLabel => _vm.NpcPlacementLabel(Index);

    // The "place on map" button (MODE 2) shows only for a row that has an NPC to place.
    public bool CanPlace => SelectedNpc is { Id: > 0 };

    // Enter the transient footprint-brush placement mode bound to this row.
    [RelayCommand]
    private void Place() => _vm.BeginPlaceNpcRow(Index);

    // Row-identity refresh: post number, pin state, this row's own NPC pick. Does NOT touch NpcEntries —
    // re-notifying that mid-selection was re-binding the picker's ItemsSource while AutoCompleteBox was still
    // committing the user's click, which reset the pick back to blank (only this row's picker ever regressed;
    // every other autocomplete in the editor sets its ItemsSource once and never re-raises it on selection).
    public void Refresh()
    {
        OnPropertyChanged(nameof(SlotLabel));
        OnPropertyChanged(nameof(SelectedNpc));
        OnPropertyChanged(nameof(PlacementLabel));
        OnPropertyChanged(nameof(CanPlace));
    }

    // Entries-list refresh: the NPC name table itself changed elsewhere (save/rename), so both the picker's
    // ItemsSource and this row's displayed selection (same id, possibly new name) need updating.
    public void RefreshEntries()
    {
        OnPropertyChanged(nameof(NpcEntries));
        OnPropertyChanged(nameof(SelectedNpc));
    }
}

/// <summary>One eligible option in the NPC-spawn placement picker: a non-empty, not-yet-pinned NPC row
/// (<see cref="RowIndex"/>, 0-based) + a display label ("post — NpcName") built by the VM.</summary>
public sealed record NpcSpawnChoice(int RowIndex, string Display);

/// <summary>The Attribute-mode palette. An entry belongs here for one of two reasons:
///  * a REAL serialized tile attribute, which also has a matching TileType and round-trips through map
///    data. Placing it writes the tile's inline attribute OR its FringeAttr, chosen by the active
///    WorldLayer — the uniform two-plane world authors both planes the same way.
///  * an EDITOR-ONLY gesture writing something other than a tile attribute, kept OFF the shared TileType
///    enum so it can never leak into tile serialization. NpcSpawn is the only one today.
///
/// <para>That second case is why this is a hand-authored enum rather than
/// <c>Enum.GetValues<TileType>()</c>.</para>
///
/// <para>LayerRamp is the sole connector between the two planes. Stored on
/// <c>FringeAttr.Type = LayerRamp</c>, but it LOGICALLY OCCUPIES BOTH: no other attribute may share its
/// tile on either layer, and it can only be placed on a fully-clear tile. See LayerLogic.</para></summary>
public enum AttributeTool { Blocked, Warp, Item, NpcAvoid, Key, KeyOpen, NpcSpawn, LayerRamp }

public enum EditorMode { Tile, Attribute, Light }
// Place = paint/stamp; Select = marquee for copy/cut/paste; Delete = brush-erase the mode-dependent content
// (tile-art layer / attribute / light) under a custom-sized brush — like right-click, but area-sized.
public enum EditorAction { Place, Select, Delete }
public enum ClipboardKind { None, Tile, Attribute, Light }
public enum DragPhase { Begin, Move, End }
public enum NeighborCell { Up, Down, Left, Right, UpLeft, UpRight, DownLeft, DownRight }

/// <summary>A left-click on tile (<see cref="X"/>, <see cref="Y"/>), carrying the two modifier keys held at
/// press time: <see cref="Alt"/> paints with the retained attribute instead of opening its dialog, and
/// <see cref="Retain"/> re-uses the last dialog's values. Named rather than an
/// <c>(int, int, bool, bool)</c> tuple — the two trailing bools mean opposite things, and nothing about the
/// tuple made transposing them visible at either the raise or the handle site.</summary>
/// <summary>One tile touched by the left button.
///
/// <para><paramref name="Dragging"/> separates the press from the cells the pointer is then dragged across.
/// They are otherwise the same event, and an authoring dialog must only ever open on the press: a drag that
/// crosses a tile already holding the attribute would pop the dialog for it mid-stroke, which is what
/// laying a run of walls across an existing one does.</para></summary>
public readonly record struct TileClick(int X, int Y, bool Alt, bool Retain, bool Dragging = false);

/// <summary>A marquee drag's current rectangle plus which end of the gesture produced it. The four ints are
/// two CORNERS, not a position and a size, which is exactly the confusion a five-element tuple invited.</summary>
public readonly record struct SelectionDrag(int X1, int Y1, int X2, int Y2, DragPhase Phase);

/// <summary>The committed marquee rectangle, normalized so <see cref="X1"/>/<see cref="Y1"/> is always the
/// top-left. Same two-corners-not-a-size hazard as <see cref="SelectionDrag"/>, minus the phase.</summary>
public readonly record struct SelectionBox(int X1, int Y1, int X2, int Y2);

public sealed class TileStamp
{
    public static readonly TileStamp Empty = new(1, 1, new int[1, 1]);

    public int Cols { get; }
    public int Rows { get; }
    // Indices[dc, dr] — 1-based tile index; 0 = blank slot (past end-of-tileset).
    public int[,] Indices { get; }

    public TileStamp(int cols, int rows, int[,] indices)
    {
        Cols = cols;
        Rows = rows;
        Indices = indices;
    }

    public static TileStamp Single(int idx)
    {
        var arr = new int[1, 1];
        arr[0, 0] = idx;
        return new TileStamp(1, 1, arr);
    }
}

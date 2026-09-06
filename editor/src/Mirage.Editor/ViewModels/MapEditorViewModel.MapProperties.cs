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

/// <summary>The selected map's own record fields — every property the Properties panel binds and
/// edits — and the notification that re-raises them all when the record is swapped wholesale.</summary>
public sealed partial class MapEditorViewModel : ObservableObject
{
    // Tri-state Moral choices for the map's inherit/override ComboBox. Rebuilt on language change.
    public IReadOnlyList<MoralChoice> MoralOptions { get; private set; } = MoralChoices.Build();

    // Routes map name edits through the ViewModel so the list item DisplayName updates live.
    public string MapName
    {
        get => SelectedMap?.Record.Name ?? "";
        set
        {
            if (SelectedMap is null) return;
            SelectedMap.Record.Name = value;
            SelectedMap.NotifyDisplayName();
        }
    }

    // Player-facing name. Routed through NotifyDisplayName so the list row's parenthetical updates live.
    public string MapDisplayName
    {
        get => SelectedMap?.Record.DisplayName ?? "";
        set
        {
            if (SelectedMap is null) return;
            SelectedMap.Record.DisplayName = value;
            SelectedMap.NotifyDisplayName();
        }
    }

    // Pass-through properties for map record fields — each setter marks the map dirty
    // so that edits to numeric fields and NPC slots register correctly.

    // Tri-state Moral: "(Inherit)" (null) or an explicit MapMoral. The map's own value overrides
    // its group; null inherits the group (else the hard default None).
    public MoralChoice? SelectedMapMoral
    {
        get => MoralOptions.FirstOrDefault(c => c.Value == SelectedMap?.Record.Moral) ?? MoralOptions[0];
        set
        {
            if (SelectedMap is null || value is null || SelectedMap.Record.Moral == value.Value) return;
            SelectedMap.Record.Moral = value.Value;
            SelectedMap.MarkDirty();
            OnPropertyChanged(nameof(SelectedMapMoral));
        }
    }
    public int MapUp
    {
        get => SelectedMap?.Record.Up ?? 0;
        set
        {
            if (SelectedMap is null || SelectedMap.Record.Up == value) return;
            SelectedMap.Record.Up = value;
            SelectedMap.MarkDirty();
        }
    }
    public int MapDown
    {
        get => SelectedMap?.Record.Down ?? 0;
        set
        {
            if (SelectedMap is null || SelectedMap.Record.Down == value) return;
            SelectedMap.Record.Down = value;
            SelectedMap.MarkDirty();
        }
    }
    public int MapLeft
    {
        get => SelectedMap?.Record.Left ?? 0;
        set
        {
            if (SelectedMap is null || SelectedMap.Record.Left == value) return;
            SelectedMap.Record.Left = value;
            SelectedMap.MarkDirty();
        }
    }
    public int MapRight
    {
        get => SelectedMap?.Record.Right ?? 0;
        set
        {
            if (SelectedMap is null || SelectedMap.Record.Right == value) return;
            SelectedMap.Record.Right = value;
            SelectedMap.MarkDirty();
        }
    }
    public int MapMusic
    {
        get => SelectedMap?.Record.Music ?? 0;
        set
        {
            if (SelectedMap is null || SelectedMap.Record.Music == value) return;
            SelectedMap.Record.Music = value;
            SelectedMap.MarkDirty();
        }
    }
    public int MapBootMap
    {
        get => SelectedMap?.Record.BootMap ?? 0;
        set
        {
            if (SelectedMap is null || SelectedMap.Record.BootMap == value) return;
            SelectedMap.Record.BootMap = value;
            SelectedMap.MarkDirty();
        }
    }
    public int MapBootX
    {
        get => SelectedMap?.Record.BootX ?? 0;
        set
        {
            if (SelectedMap is null || SelectedMap.Record.BootX == value) return;
            SelectedMap.Record.BootX = value;
            SelectedMap.MarkDirty();
        }
    }
    public int MapBootY
    {
        get => SelectedMap?.Record.BootY ?? 0;
        set
        {
            if (SelectedMap is null || SelectedMap.Record.BootY == value) return;
            SelectedMap.Record.BootY = value;
            SelectedMap.MarkDirty();
        }
    }
    // Map-enter/leave greeting, authored per map (shops are not map-bound).
    // Blank on the map inherits the field from its MapGroup; blank everywhere = no greeting.
    public string MapGreetingSpeaker
    {
        get => SelectedMap?.Record.GreetingSpeaker ?? "";
        set
        {
            if (SelectedMap is null || SelectedMap.Record.GreetingSpeaker == value) return;
            SelectedMap.Record.GreetingSpeaker = value;
            SelectedMap.MarkDirty();
        }
    }
    public string MapJoinSay
    {
        get => SelectedMap?.Record.JoinSay ?? "";
        set
        {
            if (SelectedMap is null || SelectedMap.Record.JoinSay == value) return;
            SelectedMap.Record.JoinSay = value;
            SelectedMap.MarkDirty();
        }
    }
    public string MapLeaveSay
    {
        get => SelectedMap?.Record.LeaveSay ?? "";
        set
        {
            if (SelectedMap is null || SelectedMap.Record.LeaveSay == value) return;
            SelectedMap.Record.LeaveSay = value;
            SelectedMap.MarkDirty();
        }
    }
    // ── What a blank field would inherit ─────────────────────────────────────
    // Each greeting field falls back to the map's group, so the placeholder shows the value that
    // would actually be used rather than a generic hint — leaving a box empty is then an informed
    // choice instead of a guess. Offline only: the online session holds group NAMES, not records, so
    // there is nothing to read and these fall back to the plain hint.

    /// <summary>Resolves a map-group id to its record. Set by the shell to read the MapGroup editor's
    /// own rows, which are live in BOTH modes — the offline folder is the fallback, and online the
    /// service holds group NAMES only, so without this the hints would go blank the moment you
    /// connected.</summary>
    public Func<int, MapGroupRecord?>? ResolveMapGroup { get; set; }

    private MapGroupRecord? SelectedGroupRecord
    {
        get
        {
            int id = MapGroup;
            if (id <= 0) return null;
            if (ResolveMapGroup?.Invoke(id) is { } fromShell) return fromShell;
            var groups = _data.OfflineMapGroups;
            return id < groups.Length ? groups[id] : null;
        }
    }

    private static string InheritedOr(string? inherited, string fallbackKey) =>
        string.IsNullOrWhiteSpace(inherited)
            ? EditorStrings.Get(fallbackKey)
            : inherited;

    /// <summary>What the map would be CALLED if this box stays blank — the group's display name, then
    /// its plain name, then "Map N", exactly as <see cref="MapGroupResolve.DisplayName"/> resolves it.</summary>
    public string MapDisplayNamePlaceholder
    {
        get
        {
            var g = SelectedGroupRecord;
            if (!string.IsNullOrWhiteSpace(g?.DisplayName)) return g!.DisplayName;
            if (!string.IsNullOrWhiteSpace(g?.Name)) return g!.Name;
            if (!string.IsNullOrWhiteSpace(MapName)) return MapName;
            return SelectedMap is null
                ? EditorStrings.Get(EditorStrings.MapEditor_InheritsPlaceholder)
                : EditorStrings.Format(EditorStrings.MapEditor_MapWithId, ("Id", SelectedMap.Index));
        }
    }

    public string MapGreetingSpeakerPlaceholder =>
        InheritedOr(SelectedGroupRecord?.GreetingSpeaker, EditorStrings.MapEditor_GreetingPlaceholder);
    public string MapJoinSayPlaceholder =>
        InheritedOr(SelectedGroupRecord?.JoinSay, EditorStrings.MapEditor_InheritsPlaceholder);
    public string MapLeaveSayPlaceholder =>
        InheritedOr(SelectedGroupRecord?.LeaveSay, EditorStrings.MapEditor_InheritsPlaceholder);

    private void NotifyInheritedPlaceholders()
    {
        OnPropertyChanged(nameof(MapDisplayNamePlaceholder));
        OnPropertyChanged(nameof(MapGreetingSpeakerPlaceholder));
        OnPropertyChanged(nameof(MapJoinSayPlaceholder));
        OnPropertyChanged(nameof(MapLeaveSayPlaceholder));
    }

    // Tri-state (null = inherit from the map group) — bound to IsThreeState CheckBoxes.
    public bool? MapIndoors
    {
        get => SelectedMap?.Record.Indoors;
        set
        {
            if (SelectedMap is null || SelectedMap.Record.Indoors == value) return;
            SelectedMap.Record.Indoors = value;
            SelectedMap.MarkDirty();
        }
    }
    // The two lighting flags are mutually exclusive, so turning one ON clears the other rather than leaving a
    // pair the resolver has to arbitrate. Cleared to null, not false: an explicit false would also block the
    // group from supplying that flag, which is a second decision the author did not make by ticking one box.
    public bool? MapAlwaysLit
    {
        get => SelectedMap?.Record.AlwaysLit;
        set
        {
            if (SelectedMap is null || SelectedMap.Record.AlwaysLit == value) return;
            SelectedMap.Record.AlwaysLit = value;
            if (value == true && SelectedMap.Record.AlwaysDark is not null)
            {
                SelectedMap.Record.AlwaysDark = null;
                OnPropertyChanged(nameof(MapAlwaysLit));
        OnPropertyChanged(nameof(MapAlwaysDark));
            }
            SelectedMap.MarkDirty();
        }
    }
    public bool? MapAlwaysDark
    {
        get => SelectedMap?.Record.AlwaysDark;
        set
        {
            if (SelectedMap is null || SelectedMap.Record.AlwaysDark == value) return;
            SelectedMap.Record.AlwaysDark = value;
            if (value == true && SelectedMap.Record.AlwaysLit is not null)
            {
                SelectedMap.Record.AlwaysLit = null;
                OnPropertyChanged(nameof(MapAlwaysLit));
            }
            SelectedMap.MarkDirty();
        }
    }
    public int MapGroup
    {
        get => SelectedMap?.Record.MapGroup ?? 0;
        set
        {
            if (SelectedMap is null || SelectedMap.Record.MapGroup == value) return;
            SelectedMap.Record.MapGroup = value;
            SelectedMap.MarkDirty();
        }
    }
    // Server-bumped revision.  Display-only — surfaces what the live server has (or will have after
    // the next push); editing it client-side would just be cosmetic since the server ignores it.
    public int MapRevision => SelectedMap?.Record.Revision ?? 0;
    // Status-bar readout: "Revision: N" (built like SelectedLayerLabel, reusing the localized label).
    public string MapRevisionText =>
        $"{EditorStrings.Get(EditorStrings.MapEditor_RevisionLabel)} {MapRevision}";

    // ── Size ──────────────────────────────────────────────────────────────────

    /// <summary>The open map's size, for the Properties panel. Read-only here: changing it discards tiles,
    /// so it goes through a dialog that says what would be lost — see <see cref="ResizeMapCommand"/>.</summary>
    public string MapSizeText =>
        EditorStrings.Format(EditorStrings.MapEditor_SizeText, ("Width", MapCols), ("Height", MapRows));

    /// <summary>Set by the View: shows the resize dialog and answers with the chosen size.</summary>
    public Func<MapResizeDialogViewModel, Task>? ShowMapResizeDialogAsync { get; set; }

    /// <summary>Resizes the open map. The dialog refuses a linked map, itemizes what a smaller size would
    /// discard, and says that none of it can be taken back.</summary>
    [RelayCommand]
    private async Task ResizeMapAsync()
    {
        if (ShowMapResizeDialogAsync is null || SelectedMap is null) return;

        var dlg = new MapResizeDialogViewModel(SelectedMap.Record, AllMapsForResize(), SelectedMap.Index);
        MapSize? chosen = null;
        dlg.Confirmed += size => chosen = size;
        await ShowMapResizeDialogAsync(dlg);
        if (chosen is not { } size || (size.Width == MapCols && size.Height == MapRows)) return;

        MapResize.Apply(SelectedMap.Record, size);
        SelectedMap.MarkDirty();
        RebuildMapNpcRows();
        NotifyMapProperties();
        StatusMessage = EditorStrings.Format(EditorStrings.MapEditorStatus_Resized,
            ("Width", size.Width), ("Height", size.Height));
    }

    // Every map the world holds, so the dialog can find this one's links and the warps aimed at it. Online
    // the editor holds only what it has fetched, which is exactly what it can report on.
    private IReadOnlyList<MapRecord?> AllMapsForResize()
    {
        var all = new MapRecord?[_data.Limits.Maps + 1];
        foreach (var row in Maps)
            if (row.Index >= 0 && row.Index < all.Length) all[row.Index] = row.Record;
        return all;
    }

    /// <summary>Re-raises every property the map Properties panel and the neighbor grid bind to.
    /// Called whenever the selected map changes wholesale — a different map selected, a record
    /// swapped in by a load, or an edit applied outside the bound setters — because those paths
    /// mutate the underlying <c>MapRecord</c> without going through the individual properties.
    /// <para>Also rebuilds the map's NPC-spawn rows, which are derived from the record rather than
    /// bound to it.</para></summary>
    /// <summary>Runs after every property above has been re-raised. The view uses it to settle controls that
    /// hold their own edit state, which a property notification alone does not reach.</summary>
    public Action? MapPropertiesRefreshed { get; set; }

    private void NotifyMapProperties()
    {
        OnPropertyChanged(nameof(MapName));
        OnPropertyChanged(nameof(MapDisplayName));
        OnPropertyChanged(nameof(SelectedMapMoral));
        OnPropertyChanged(nameof(MapGroup));
        OnPropertyChanged(nameof(SelectedMapGroup));
        OnPropertyChanged(nameof(MapUp));
        OnPropertyChanged(nameof(MapDown));
        OnPropertyChanged(nameof(MapLeft));
        OnPropertyChanged(nameof(MapRight));
        OnPropertyChanged(nameof(MapMusic));
        OnPropertyChanged(nameof(MapBootMap));
        OnPropertyChanged(nameof(MapBootX));
        OnPropertyChanged(nameof(MapBootY));
        OnPropertyChanged(nameof(MapGreetingSpeaker));
        OnPropertyChanged(nameof(MapJoinSay));
        OnPropertyChanged(nameof(MapLeaveSay));
        NotifyInheritedPlaceholders();
        OnPropertyChanged(nameof(MapIndoors));
        OnPropertyChanged(nameof(MapAlwaysLit));
        OnPropertyChanged(nameof(MapAlwaysDark));
        OnPropertyChanged(nameof(MapRevisionText));
        OnPropertyChanged(nameof(MapSizeText));
        OnPropertyChanged(nameof(UsedTilesheets));
        OnPropertyChanged(nameof(HasSelectedMap));
        RebuildMapNpcRows();
        OnPropertyChanged(nameof(SelectedMapUp));
        OnPropertyChanged(nameof(SelectedMapDown));
        OnPropertyChanged(nameof(SelectedMapLeft));
        OnPropertyChanged(nameof(SelectedMapRight));
        OnPropertyChanged(nameof(SelectedMapBootMap));
        OnPropertyChanged(nameof(NeighborMapUp));
        OnPropertyChanged(nameof(NeighborMapDown));
        OnPropertyChanged(nameof(NeighborMapLeft));
        OnPropertyChanged(nameof(NeighborMapRight));
        OnPropertyChanged(nameof(NeighborMapUpLeft));
        OnPropertyChanged(nameof(NeighborMapUpRight));
        OnPropertyChanged(nameof(NeighborMapDownLeft));
        OnPropertyChanged(nameof(NeighborMapDownRight));
        MapPropertiesRefreshed?.Invoke();
    }

    /// <summary>Bridges a <see cref="MapRowViewModel"/> edit into this view model. Only the
    /// <c>Record</c> notification matters: the row raises it when its whole record is replaced (see
    /// MapRowViewModel), which invalidates the bound properties, the hovered-tile readout, and every
    /// cached tile in the grid at once.</summary>
    private void OnMapRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MapRowViewModel.Record))
        {
            NotifyMapProperties();
            NotifyHoveredTile();
            RepaintMap();
        }
    }
}

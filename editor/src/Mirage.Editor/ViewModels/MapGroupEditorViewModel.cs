using CommunityToolkit.Mvvm.ComponentModel;
using Mirage.Editor.Localization;
using Mirage.Editor.Models;
using Mirage.Editor.Services;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using System.Collections.ObjectModel;
namespace Mirage.Editor.ViewModels;

/// <summary>The MapGroup editor: a sibling of the record editors that authors map groups
/// (map-like fallback props + the Territory flag). Dual-mode like the others; the server stores groups in a
/// sparse Dictionary but the editor presents the standard 1-based slot list.</summary>
public sealed partial class MapGroupEditorViewModel : EditorViewModelBase<MapGroupRowViewModel>
{
    [ObservableProperty] private MapGroupRowViewModel? _selectedMapGroup;
    public override MapGroupRowViewModel? Selected => SelectedMapGroup;
    protected override void SetSelected(MapGroupRowViewModel? row) => SelectedMapGroup = row;
    public ObservableCollection<MapGroupRowViewModel> MapGroups { get; } = [];
    public override ObservableCollection<MapGroupRowViewModel> Items => MapGroups;
    protected override string GetFilterText(MapGroupRowViewModel row) => row.DisplayName;

    // Type-ahead source for the group's BootMap picker (the view's DataContext is this VM).
    public NamedEntry[] MapEntries => _data.LiveMapEntries;

    /// <summary>Supplies the maps whose <c>MapGroup</c> names the given group. Assigned by
    /// <see cref="MainWindowViewModel"/>, which owns the map editor — membership lives on the MAP, so a group
    /// cannot answer this from its own record. Mirrors <c>MapEditor.ResolveMapGroup</c> in the other direction.</summary>
    public Func<int, IReadOnlyList<ReferenceLinkViewModel>>? ResolveGroupMaps { get; set; }

    /// <summary>The maps in the selected group, as links that open them. Recomputed rather than cached: it is a
    /// scan over the map list, and a cache would have to be invalidated every time a map's group changed.</summary>
    public IReadOnlyList<ReferenceLinkViewModel> GroupMaps =>
        SelectedMapGroup is { } g && ResolveGroupMaps is { } resolve ? resolve(g.Index) : [];

    /// <summary>True once the group has members, so the panel can say "no maps" instead of showing nothing.</summary>
    public bool HasGroupMaps => GroupMaps.Count > 0;

    /// <summary>Re-read <see cref="GroupMaps"/>. Membership is read off map records, which this editor does not
    /// own, so it cannot see them change: the eager load that fills those records finishes long after the group
    /// list is built, and a map's group can be reassigned in the map editor entirely behind this one's back.
    /// <see cref="MainWindowViewModel"/> calls this at both of those moments.</summary>
    public void NotifyGroupMapsChanged()
    {
        OnPropertyChanged(nameof(GroupMaps));
        OnPropertyChanged(nameof(HasGroupMaps));
    }

    public MapGroupEditorViewModel(EditorDataService data, EditorConnection conn) : base(data, conn)
    {
        HookItems();
        _data.EntriesInvalidated += () =>
        {
            OnPropertyChanged(nameof(MapEntries));
            foreach (var g in MapGroups) g.NotifyEntriesChanged();
        };
        EditorStrings.LanguageChanged += () => { foreach (var g in MapGroups) g.RefreshMoralOptions(); };
    }

    protected override string SectionId => "MapGroups";
    protected override string TypeName => EditorStrings.Get(EditorStrings.MapGroupEditor_TypeName);
    protected override string TypeNamePlural => EditorStrings.Get(EditorStrings.MapGroupEditor_TypeNamePlural);
    protected override int GetIndex(MapGroupRowViewModel vm) => vm.Index;
    protected override bool GetIsDirty(MapGroupRowViewModel vm) => vm.IsDirty;
    // ── Copy ──────────────────────────────────────────────────────────────────

    /// <summary>An unused slot, by the same rule the list already labels one: it has no name.</summary>
    protected override string GetName(MapGroupRowViewModel row) => row.Name;

    protected override bool GetIsLoaded(MapGroupRowViewModel row) => row.IsLoaded;

    protected override void CopyInto(MapGroupRowViewModel source, MapGroupRowViewModel target)
    {
        var rec = source.ToRecord();
        rec.Name += RecordCopy.Suffix;
        // The only record that stores its own slot number; carrying the source number over would leave the
        // copy disagreeing with the slot it lives in.
        rec.Index = target.Index;
        target.CopyFromRecord(rec);
    }

    protected override void ClearDirtyState(MapGroupRowViewModel vm) => vm.ClearDirty();

    public async Task EagerLoadAllAsync(CancellationToken ct)
    {
        if (!_data.IsOnline) return;
        var bulk = await _conn.RequestAllMapGroupsAsync(ct);
        if (bulk is null) return;
        foreach (var pkt in bulk.MapGroups)
        {
            var vm = Items.FirstOrDefault(v => v.Index == pkt.GroupNum);
            if (vm is not null) ApplyServerResponse(vm, pkt);
        }
        OnPropertyChanged(nameof(FilteredItems));
    }

    partial void OnSelectedMapGroupChanged(MapGroupRowViewModel? value)
    {
        NotifyDirtyState();
        NotifyGroupMapsChanged();
        if (value is not null && !value.IsLoaded && _data.IsOnline)
            _ = LoadEntityAsync(value);
    }

    public void LoadOffline()
    {
        SelectedMapGroup = null;
        MapGroups.Clear();
        for (int i = 1; i < _data.OfflineMapGroups.Length; i++)
            MapGroups.Add(new MapGroupRowViewModel(i, _data.OfflineMapGroups[i], () => _data.LiveMapEntries));
        StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_LoadedOffline,
            ("Count", MapGroups.Count), ("EntityType", TypeNamePlural));
    }

    public void LoadOnline()
    {
        if (_data.OnlineMapGroups is null) return;
        SelectedMapGroup = null;
        MapGroups.Clear();
        foreach (var entry in _data.OnlineMapGroups)
        {
            MapGroups.Add(new MapGroupRowViewModel(entry.Num, new MapGroupRecord { Index = entry.Num, Name = entry.Name },
                () => _data.LiveMapEntries, isLoaded: false));
        }

        StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_LoadedOnline,
            ("Count", MapGroups.Count), ("EntityType", TypeNamePlural));
    }

    protected override async Task<IPacket?> RequestFromServerAsync(MapGroupRowViewModel vm)
        => await _conn.RequestMapGroupAsync(vm.Index);

    protected override void ApplyServerResponse(MapGroupRowViewModel vm, IPacket pkt)
        => vm.ApplyPacket((UpdateMapGroupPacket)pkt);

    protected override IPacket BuildSavePacket(MapGroupRowViewModel vm) => vm.BuildSavePacket();

    protected override void AfterSave(MapGroupRowViewModel vm)
    {
        if (_data.IsOnline) _data.PatchOnlineMapGroupName(vm.Index, vm.Name);
    }

    protected override Task SaveOfflineAsync(MapGroupRowViewModel vm)
        => _data.SaveOfflineMapGroupAsync(vm.Index, vm.ToRecord());

    protected override void LoadFromOfflineRecord(MapGroupRowViewModel vm)
        => vm.LoadFromRecord(_data.OfflineMapGroups[vm.Index]);
}

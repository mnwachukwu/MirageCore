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

/// <summary>Construction and map loading: the offline and online entry points, the eager
/// load-everything pass with progress, and the language-changed refresh.</summary>
public sealed partial class MapEditorViewModel : ObservableObject
{
    public MapEditorViewModel(EditorDataService data, EditorConnection conn)
    {
        _data = data;
        _conn = conn;
        // NPC rows are dynamic now: rebuilt per map from Record.Npcs in RebuildMapNpcRows.
        HookMaps();
        _data.EntriesInvalidated += NotifyEntryLists;
        EditorStrings.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(AnimPreviewLabel));
        OnPropertyChanged(nameof(HoveredText));
        OnPropertyChanged(nameof(StatusModeText));
        OnPropertyChanged(nameof(FilterStatus));
        OnPropertyChanged(nameof(SelectedAttributeDescription));
        OnPropertyChanged(nameof(HoveredGroundAttributeText));
        OnPropertyChanged(nameof(HoveredFringeAttributeText));
        OnPropertyChanged(nameof(HoveredNpcSpawnText));
        MoralOptions = MoralChoices.Build();
        OnPropertyChanged(nameof(MoralOptions));
        OnPropertyChanged(nameof(SelectedMapMoral));
        // A lock held by another window of your own account is worded, not just named.
        RefreshLockState();
    }

    private void NotifyEntryLists()
    {
        OnPropertyChanged(nameof(MapEntries));
        OnPropertyChanged(nameof(NpcEntries));
        OnPropertyChanged(nameof(ItemEntries));
        OnPropertyChanged(nameof(MapGroupEntries));
        OnPropertyChanged(nameof(SelectedMapGroup));
        foreach (var row in MapNpcRows) row.RefreshEntries();   // NPC list changed → refresh each row's picker source
    }

    public void LoadOffline()
    {
        SelectedMap = null;
        Maps.Clear();
        for (int i = 1; i < _data.OfflineMaps.Length; i++)
        {
            var row = new MapRowViewModel(i, _data.OfflineMaps[i]);
            Maps.Add(row);
        }
        StatusMessage = EditorStrings.Format(EditorStrings.MapEditorStatus_LoadedOffline,
            ("Count", Maps.Count));
    }

    public void LoadOnline()
    {
        if (_data.OnlineMaps is null) return;
        SelectedMap = null;
        Maps.Clear();
        foreach (var entry in _data.OnlineMaps)
        {
            var row = new MapRowViewModel(entry.Num, new MapRecord { Name = entry.Name }, isLoaded: false);
            Maps.Add(row);
        }
        StatusMessage = EditorStrings.Format(EditorStrings.MapEditorStatus_LoadedOnline,
            ("Count", Maps.Count));
    }

    public async Task EagerLoadAllAsync(Action<int, int> onProgress, CancellationToken ct)
    {
        var unloaded = Maps.Where(m => !m.IsLoaded).ToList();
        int total = unloaded.Count;
        for (int i = 0; i < total; i++)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                var pkt = await _conn.RequestMapAsync(unloaded[i].Index);
                if (pkt is not null)
                    unloaded[i].LoadRecord(EditorDataService.MapRecordFromPacket(pkt));
            }
            catch (OperationCanceledException) { return; }
            catch { /* skip failed map */ }
            onProgress(i + 1, total);
        }
    }

    private async Task LoadMapAsync(MapRowViewModel vm)
    {
        IsLoading = true;
        StatusMessage = EditorStrings.Format(EditorStrings.MapEditorStatus_LoadingMap, ("Index", vm.Index));
        try
        {
            var pkt = await _conn.RequestMapAsync(vm.Index);
            if (pkt is not null)
            {
                var rec = EditorDataService.MapRecordFromPacket(pkt);
                vm.LoadRecord(rec);
                StatusMessage = EditorStrings.Format(EditorStrings.MapEditorStatus_LoadedMap,
                    ("Index", vm.Index));
                if (vm == SelectedMap)
                    _ = EagerLoadNeighborsAsync();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = EditorStrings.Format(EditorStrings.MapEditorStatus_LoadMapFailed,
                ("Index", vm.Index), ("Error", ex.Message));
        }
        finally
        {
            IsLoading = false;
        }
    }
}

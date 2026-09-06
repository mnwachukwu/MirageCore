using CommunityToolkit.Mvvm.ComponentModel;
using Mirage.Editor.Localization;
using Mirage.Editor.Services;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using System.Collections.ObjectModel;

namespace Mirage.Editor.ViewModels;

/// <summary>The quest editor — clones ShopEditorViewModel over the EditorViewModelBase
/// online/offline flow. A quest row needs live NPC (giver/turn-in/objective target), item (reward) and
/// quest (PrereqQuest) picker lists, all sourced from EditorDataService; the class gate is a multi-select
/// rather than a picker.</summary>
public sealed partial class QuestEditorViewModel : EditorViewModelBase<QuestRowViewModel>
{
    [ObservableProperty] private QuestRowViewModel? _selectedQuest;
    public override QuestRowViewModel? Selected => SelectedQuest;
    protected override void SetSelected(QuestRowViewModel? row) => SelectedQuest = row;
    public ObservableCollection<QuestRowViewModel> Quests { get; } = [];
    public override ObservableCollection<QuestRowViewModel> Items => Quests;
    protected override string GetFilterText(QuestRowViewModel row) => row.DisplayName;

    public QuestEditorViewModel(EditorDataService data, EditorConnection conn) : base(data, conn)
    {
        HookItems();
        _data.EntriesInvalidated += () =>
        {
            foreach (var q in Quests) q.NotifyEntriesChanged();
            RebuildClassSelection();   // a renamed or newly named class must re-label its checkbox
        };
        ClassSelection.SelectionChanged += ids =>
        {
            if (SelectedQuest is null) return;
            _applyingClassSelection = true;
            try { SelectedQuest.AllowedClasses = ids; }
            finally { _applyingClassSelection = false; }
        };
    }

    public Models.NamedEntry[] ClassEntries => _data.LiveClassEntries;

    /// <summary>The class gate: a checkbox per class, none ticked meaning every class. Unlike the level
    /// and stat requirements beside it this is a set, and a quest outside it is invisible rather than
    /// merely unacceptable.</summary>
    public ClassSelectionViewModel ClassSelection { get; } = new();

    // Set while a checkbox click is writing into the row, so the row's change notification doesn't bounce
    // back and rebuild the checkboxes mid-edit.
    private bool _applyingClassSelection;

    private void RebuildClassSelection()
    {
        if (SelectedQuest is null) ClassSelection.Clear();
        else ClassSelection.Rebuild(ClassEntries, SelectedQuest.AllowedClasses);
        ClassSelection.IsActive = SelectedQuest is not null;
    }

    private void OnQuestPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Re-tick when the row's list changed from anywhere but the checkboxes (a packet, a discard).
        if (e.PropertyName is nameof(QuestRowViewModel.AllowedClasses) && !_applyingClassSelection)
            RebuildClassSelection();
    }

    protected override string SectionId => "Quests";
    protected override string TypeName => EditorStrings.Get(EditorStrings.QuestEditor_TypeName);
    protected override string TypeNamePlural => EditorStrings.Get(EditorStrings.QuestEditor_TypeNamePlural);
    protected override int GetIndex(QuestRowViewModel vm) => vm.Index;
    protected override bool GetIsDirty(QuestRowViewModel vm) => vm.IsDirty;
    // ── Copy ──────────────────────────────────────────────────────────────────

    /// <summary>An unused slot, by the same rule the list already labels one: it has no name.</summary>
    protected override string GetName(QuestRowViewModel row) => row.Name;

    protected override bool GetIsLoaded(QuestRowViewModel row) => row.IsLoaded;

    protected override void CopyInto(QuestRowViewModel source, QuestRowViewModel target)
    {
        var rec = source.ToRecord();
        rec.Name += RecordCopy.Suffix;
        // Giver and turn-in are resolved by scanning for the NPC, so a duplicate claim would leave one of
        // the two quests unreachable. The copy arrives unattached.
        rec.GiverNpc = 0;
        rec.TurnInNpc = 0;
        target.CopyFromRecord(rec);
    }

    protected override void ClearDirtyState(QuestRowViewModel vm) => vm.ClearDirty();

    public async Task EagerLoadAllAsync(CancellationToken ct)
    {
        if (!_data.IsOnline) return;
        var bulk = await _conn.RequestAllQuestsAsync(ct);
        if (bulk is null) return;
        foreach (var pkt in bulk.Quests)
        {
            var vm = Items.FirstOrDefault(v => v.Index == pkt.QuestNum);
            if (vm is not null) ApplyServerResponse(vm, pkt);
        }
        OnPropertyChanged(nameof(FilteredItems));
    }

    partial void OnSelectedQuestChanged(QuestRowViewModel? oldValue, QuestRowViewModel? newValue)
    {
        NotifyInboundRefsChanged();
        if (oldValue is not null) oldValue.PropertyChanged -= OnQuestPropertyChanged;
        if (newValue is not null) newValue.PropertyChanged += OnQuestPropertyChanged;
        NotifyDirtyState();
        if (newValue is not null && !newValue.IsLoaded && _data.IsOnline)
            _ = LoadEntityAsync(newValue);
        RebuildClassSelection();
    }

    public void LoadOffline()
    {
        SelectedQuest = null;
        Quests.Clear();
        for (int i = 1; i < _data.OfflineQuests.Length; i++)
            Quests.Add(NewRow(i, _data.OfflineQuests[i]));
        StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_LoadedOffline,
            ("Count", Quests.Count), ("EntityType", TypeNamePlural));
    }

    public void LoadOnline()
    {
        if (_data.OnlineQuests is null) return;
        SelectedQuest = null;
        Quests.Clear();
        foreach (var entry in _data.OnlineQuests)
            Quests.Add(NewRow(entry.Num, new QuestRecord { Name = entry.Name }, isLoaded: false));
        StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_LoadedOnline,
            ("Count", Quests.Count), ("EntityType", TypeNamePlural));
    }

    // Bind every picker a quest row needs from the live editor caches.
    private QuestRowViewModel NewRow(int index, QuestRecord r, bool isLoaded = true) =>
        new(index, r,
            () => _data.LiveNpcEntries, () => _data.LiveItemEntries,
            () => _data.LiveClassEntries, () => _data.LiveQuestEntries,
            _data.IsCurrencyItem, isLoaded);

    protected override async Task<IPacket?> RequestFromServerAsync(QuestRowViewModel vm)
        => await _conn.RequestQuestAsync(vm.Index);

    protected override void ApplyServerResponse(QuestRowViewModel vm, IPacket pkt)
        => vm.ApplyPacket((UpdateQuestPacket)pkt);

    protected override IPacket BuildSavePacket(QuestRowViewModel vm) => vm.BuildSavePacket();

    protected override void AfterSave(QuestRowViewModel vm)
    {
        if (_data.IsOnline) _data.PatchOnlineQuestName(vm.Index, vm.Name);
    }

    protected override Task SaveOfflineAsync(QuestRowViewModel vm)
        => _data.SaveOfflineQuestAsync(vm.Index, vm.ToRecord());

    protected override void LoadFromOfflineRecord(QuestRowViewModel vm)
        => vm.LoadFromRecord(_data.OfflineQuests[vm.Index]);
}

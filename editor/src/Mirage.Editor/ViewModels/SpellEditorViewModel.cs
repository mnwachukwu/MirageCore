using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Editor.Models;
using Mirage.Editor.Services;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using System.Collections.ObjectModel;
namespace Mirage.Editor.ViewModels;

/// <summary>The spell-list editor. Adds two category filters on top of the inherited name filter —
/// spell type and class requirement — plus id-to-entry adapters so the GiveItem and class pickers can
/// bind to <see cref="Mirage.Editor.Models.NamedEntry"/> while the record stores a bare id.</summary>
public sealed partial class SpellEditorViewModel : EditorViewModelBase<SpellRowViewModel>
{
    [ObservableProperty] private SpellRowViewModel? _selectedSpell;
    public override SpellRowViewModel? Selected => SelectedSpell;
    protected override void SetSelected(SpellRowViewModel? row) => SelectedSpell = row;
    public ObservableCollection<SpellRowViewModel> Spells { get; } = [];
    public override ObservableCollection<SpellRowViewModel> Items => Spells;
    /// <inheritdoc/>
    protected override string GetFilterText(SpellRowViewModel row) => row.DisplayName;
    public IEnumerable<SpellType> SpellTypes { get; } = Enum.GetValues<SpellType>();
    private static readonly IReadOnlyList<EnumFilterOption<SpellType>> _spellTypeFilters =
        Enum.GetValues<SpellType>().Select(t => new EnumFilterOption<SpellType>(t)).ToArray();
    public IReadOnlyList<EnumFilterOption<SpellType>> SpellTypeFilters => _spellTypeFilters;
    [ObservableProperty] private EnumFilterOption<SpellType>? _typeFilter;
    partial void OnTypeFilterChanged(EnumFilterOption<SpellType>? value)
    {
        OnPropertyChanged(nameof(FilteredItems));
        OnPropertyChanged(nameof(FilterStatus));
        OnPropertyChanged(nameof(IsFilterActive));
    }
    [RelayCommand] private void ClearTypeFilter() => TypeFilter = null;

    public NamedEntry[] ItemEntries => _data.LiveItemEntries;
    /// <summary>Picker adapter for a GiveItem spell's target item: maps the row's numeric ItemNum to and
    /// from an entry. Null when nothing is selected or ItemNum is 0.</summary>
    public NamedEntry? SelectedGiveItem
    {
        get
        {
            if (SelectedSpell is null) return null;
            var id = SelectedSpell.ItemNum;
            return id > 0 && id < ItemEntries.Length ? ItemEntries[id] : null;
        }
        set
        {
            if (SelectedSpell is null) return;
            var id = (short)(value?.Id ?? 0);
            if (SelectedSpell.ItemNum == id) return;
            SelectedSpell.ItemNum = id;
            OnPropertyChanged(nameof(SelectedGiveItem));
        }
    }

    public NamedEntry[] ClassEntries => _data.LiveClassEntries;
    /// <summary>Class entries for the FILTER dropdown, dropping the id-0 "(none)" sentinel — filtering
    /// on "any class" would be the same as no filter at all.</summary>
    public NamedEntry[] ClassReqFilterEntries => _data.LiveClassEntries.Skip(1).ToArray();
    [ObservableProperty] private NamedEntry? _classReqFilter;
    partial void OnClassReqFilterChanged(NamedEntry? value)
    {
        OnPropertyChanged(nameof(FilteredItems));
        OnPropertyChanged(nameof(FilterStatus));
        OnPropertyChanged(nameof(IsFilterActive));
    }
    [RelayCommand] private void ClearClassReqFilter() => ClassReqFilter = null;

    /// <summary>Name filter (inherited) AND the type / class-requirement filters. While either category
    /// filter is active, unset slots are skipped — they default to AddHp with no magnitude and would
    /// otherwise flood every type-based result.</summary>
    protected override bool MatchesFilter(SpellRowViewModel row) =>
        base.MatchesFilter(row) &&
        // When a category filter is active, skip unset slots. "Set" means it carries a magnitude or,
        // for GiveItem (which has none), an item to hand over — checking VitalAmount alone would hide
        // every GiveItem spell from a filtered list.
        ((TypeFilter is null && ClassReqFilter is null)
            || (!string.IsNullOrEmpty(row.Name) && (row.VitalAmount != 0 || row.ItemNum != 0))) &&
        (TypeFilter is null || row.Type == TypeFilter.Value) &&
        // With a set rather than a single id, "filter by class" means "shows spells THIS class can learn".
        // An unrestricted spell matches every class filter, because it really is available to that class.
        (ClassReqFilter is null || ClassGate.Allows(row.AllowedClasses, ClassReqFilter.Id));
    public override bool IsFilterActive => base.IsFilterActive || TypeFilter is not null || ClassReqFilter is not null;

    /// <summary>The class gate: a checkbox per class, none ticked meaning every class. One instance for
    /// the whole list, re-pointed at whichever row is selected.</summary>
    public ClassSelectionViewModel ClassSelection { get; } = new();

    // Set while a checkbox click is writing into the row, so the row's change notification doesn't bounce
    // back and rebuild the checkboxes mid-edit.
    private bool _applyingClassSelection;

    private void RebuildClassSelection()
    {
        if (SelectedSpell is null) ClassSelection.Clear();
        else ClassSelection.Rebuild(ClassEntries, SelectedSpell.AllowedClasses);
        ClassSelection.IsActive = SelectedSpell is not null;
    }

    public SpellEditorViewModel(EditorDataService data, EditorConnection conn) : base(data, conn)
    {
        HookItems();
        _data.EntriesInvalidated += () =>
        {
            OnPropertyChanged(nameof(ItemEntries));
            OnPropertyChanged(nameof(ClassEntries));
            OnPropertyChanged(nameof(ClassReqFilterEntries));
            RebuildClassSelection();   // a renamed or newly named class must re-label its checkbox
        };
        ClassSelection.SelectionChanged += ids =>
        {
            if (SelectedSpell is null) return;
            _applyingClassSelection = true;
            try { SelectedSpell.AllowedClasses = ids; }
            finally { _applyingClassSelection = false; }
        };
    }

    /// <summary>Patch the cached online name index after a save, so the list caption reflects a renamed
    /// record without re-fetching the whole index.</summary>
    protected override void AfterSave(SpellRowViewModel vm)
    {
        if (_data.IsOnline) _data.PatchOnlineSpellName(vm.Index, vm.Name);
    }

    protected override string SectionId => "Spells";
    protected override string TypeName => EditorStrings.Get(EditorStrings.SpellEditor_TypeName);
    protected override string TypeNamePlural => EditorStrings.Get(EditorStrings.SpellEditor_TypeNamePlural);
    /// <inheritdoc/>
    protected override int GetIndex(SpellRowViewModel vm) => vm.Index;
    /// <inheritdoc/>
    protected override bool GetIsDirty(SpellRowViewModel vm) => vm.IsDirty;
    /// <inheritdoc/>
    // ── Copy ──────────────────────────────────────────────────────────────────

    /// <summary>An unused slot, by the same rule the list already labels one: it has no name.</summary>
    protected override string GetName(SpellRowViewModel row) => row.Name;

    protected override bool GetIsLoaded(SpellRowViewModel row) => row.IsLoaded;

    protected override void CopyInto(SpellRowViewModel source, SpellRowViewModel target)
    {
        var rec = source.ToRecord();
        rec.Name += RecordCopy.Suffix;
        target.CopyFromRecord(rec);
    }

    protected override void ClearDirtyState(SpellRowViewModel vm) => vm.ClearDirty();

    /// <summary>Pre-fill every placeholder row from one bulk server response, so browsing the list after
    /// connecting is instant instead of fetching per selection. No-op offline; canceled on disconnect.</summary>
    public async Task EagerLoadAllAsync(CancellationToken ct)
    {
        if (!_data.IsOnline) return;
        var bulk = await _conn.RequestAllSpellsAsync(ct);
        if (bulk is null) return;
        foreach (var pkt in bulk.Spells)
        {
            var vm = Items.FirstOrDefault(v => v.Index == pkt.SpellNum);
            if (vm is not null) ApplyServerResponse(vm, pkt);
        }
        OnPropertyChanged(nameof(FilteredItems));
    }

    partial void OnSelectedSpellChanged(SpellRowViewModel? oldValue, SpellRowViewModel? newValue)
    {
        NotifyInboundRefsChanged();
        if (oldValue is not null) oldValue.PropertyChanged -= OnSpellPropertyChanged;
        if (newValue is not null) newValue.PropertyChanged += OnSpellPropertyChanged;
        NotifyDirtyState();
        if (newValue is not null && !newValue.IsLoaded && _data.IsOnline)
            _ = LoadEntityAsync(newValue);
        RebuildClassSelection();
        OnPropertyChanged(nameof(SelectedGiveItem));
    }

    private void OnSpellPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Re-tick when the row's list changed from anywhere but the checkboxes (a packet, a discard).
        if (e.PropertyName is nameof(SpellRowViewModel.AllowedClasses) && !_applyingClassSelection)
            RebuildClassSelection();
        if (e.PropertyName is nameof(SpellRowViewModel.ItemNum))
            OnPropertyChanged(nameof(SelectedGiveItem));
    }

    /// <summary>Rebuild the list from the on-disk records, fully populated — offline editing has no
    /// server to lazy-load from.</summary>
    public void LoadOffline()
    {
        SelectedSpell = null;
        Spells.Clear();
        for (int i = 1; i < _data.OfflineSpells.Length; i++)
            Spells.Add(new SpellRowViewModel(i, _data.OfflineSpells[i]));
        StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_LoadedOffline,
            ("Count", Spells.Count), ("EntityType", TypeNamePlural));
    }

    /// <summary>Rebuild the list from the server's name index as NAME-ONLY placeholders
    /// (<c>isLoaded: false</c>). Each row's full definition arrives when it is selected, or sooner via
    /// <see cref="EagerLoadAllAsync"/>.</summary>
    public void LoadOnline()
    {
        if (_data.OnlineSpells is null) return;
        SelectedSpell = null;
        Spells.Clear();
        foreach (var entry in _data.OnlineSpells)
            Spells.Add(new SpellRowViewModel(entry.Num, new SpellRecord { Name = entry.Name }, isLoaded: false));
        StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_LoadedOnline,
            ("Count", Spells.Count), ("EntityType", TypeNamePlural));
    }

    /// <inheritdoc/>
    protected override async Task<IPacket?> RequestFromServerAsync(SpellRowViewModel vm)
        => await _conn.RequestSpellAsync(vm.Index);

    /// <inheritdoc/>
    protected override void ApplyServerResponse(SpellRowViewModel vm, IPacket pkt)
        => vm.ApplyPacket((UpdateSpellPacket)pkt);

    /// <inheritdoc/>
    protected override IPacket BuildSavePacket(SpellRowViewModel vm) => vm.BuildSavePacket();

    /// <inheritdoc/>
    protected override Task SaveOfflineAsync(SpellRowViewModel vm)
        => _data.SaveOfflineSpellAsync(vm.Index, vm.ToRecord());

    /// <inheritdoc/>
    protected override void LoadFromOfflineRecord(SpellRowViewModel vm)
        => vm.LoadFromRecord(_data.OfflineSpells[vm.Index]);
}

using Avalonia.Media.Imaging;
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

public sealed partial class ItemEditorViewModel : EditorViewModelBase<ItemRowViewModel>
{
    [ObservableProperty] private ItemRowViewModel? _selectedItem;
    public override ItemRowViewModel? Selected => SelectedItem;
    protected override void SetSelected(ItemRowViewModel? row) => SelectedItem = row;
    public override ObservableCollection<ItemRowViewModel> Items { get; } = [];
    protected override string GetFilterText(ItemRowViewModel row) => row.DisplayName;
    public IEnumerable<ItemType> ItemTypes { get; } = Enum.GetValues<ItemType>();
    private static readonly IReadOnlyList<EnumFilterOption<ItemType>> _itemTypeFilters =
        // None is INCLUDED, unlike most "skip the blank enum member" filters: it is the type treasure
        // carries, so excluding it would make the one item family that has to be authored by hand the
        // only one the author cannot filter to.
        Enum.GetValues<ItemType>().Select(t => new EnumFilterOption<ItemType>(t)).ToArray();
    public IReadOnlyList<EnumFilterOption<ItemType>> ItemTypeFilters => _itemTypeFilters;
    [ObservableProperty] private EnumFilterOption<ItemType>? _typeFilter;
    partial void OnTypeFilterChanged(EnumFilterOption<ItemType>? value)
    {
        OnPropertyChanged(nameof(FilteredItems));
        OnPropertyChanged(nameof(FilterStatus));
        OnPropertyChanged(nameof(IsFilterActive));
    }
    protected override bool MatchesFilter(ItemRowViewModel row) =>
        base.MatchesFilter(row) && (TypeFilter is null || row.Type == TypeFilter.Value);
    public override bool IsFilterActive => base.IsFilterActive || TypeFilter is not null;
    [RelayCommand] private void ClearTypeFilter() => TypeFilter = null;
    // The item sheets, indexed by sheet number. An item names one, and its pic number is a row inside
    // that sheet rather than a position across all of them.
    private IReadOnlyList<Bitmap?> _itemSheets = [];
    public NamedEntry[] ItemSheetEntries { get; private set; } = [];

    public void SetItemSheets(IReadOnlyList<Bitmap?> sheets, IReadOnlyList<string> names)
    {
        _itemSheets = sheets;
        ItemSheetEntries = SheetEntries.Build(sheets.Count, names);
        OnPropertyChanged(nameof(ItemSheetEntries));
        NotifyPicChanged();
    }

    /// <summary>The sheet the selected item draws from.</summary>
    public Bitmap? ItemBitmap
    {
        get
        {
            int sheet = SelectedItem?.ItemSheet ?? 0;
            return (uint)sheet < (uint)_itemSheets.Count ? _itemSheets[sheet] : null;
        }
    }

    public IReadOnlyList<int> ItemPicEntries { get; private set; } = [];

    // Two-way bridge for the sheet typeahead.
    public NamedEntry? SelectedItemSheetEntry
    {
        get
        {
            int sheet = SelectedItem?.ItemSheet ?? 0;
            return sheet >= 0 && sheet < ItemSheetEntries.Length ? ItemSheetEntries[sheet] : null;
        }
        set { if (SelectedItem is not null && value is not null) SelectedItem.ItemSheet = (short)value.Id; }
    }

    private void NotifyPicChanged()
    {
        var bmp = ItemBitmap;
        int count = bmp is null ? 0 : (int)(bmp.Size.Height / Constants.PicY);
        ItemPicEntries = Enumerable.Range(0, count).ToArray();
        OnPropertyChanged(nameof(ItemBitmap));
        OnPropertyChanged(nameof(ItemPicEntries));
        OnPropertyChanged(nameof(SelectedItemSheetEntry));
    }

    public ItemEditorViewModel(EditorDataService data, EditorConnection conn) : base(data, conn)
    {
        HookItems();
        _data.EntriesInvalidated += () =>
        {
            OnPropertyChanged(nameof(SpellEntries));
            OnPropertyChanged(nameof(ClassEntries));
            RebuildClassSelection();   // a renamed or newly named class must re-label its checkbox
        };
        ClassSelection.SelectionChanged += ids =>
        {
            if (SelectedItem is null) return;
            _applyingClassSelection = true;
            try { SelectedItem.AllowedClasses = ids; }
            finally { _applyingClassSelection = false; }
        };
    }

    // Set while a checkbox click is writing into the row, so the row's own change notification doesn't
    // bounce back and rebuild the checkboxes mid-edit.
    private bool _applyingClassSelection;

    public NamedEntry[] ClassEntries => _data.LiveClassEntries;

    /// <summary>The equipment class gate: a checkbox per class, none ticked meaning every class. One
    /// instance for the whole list, re-pointed at whichever row is selected.</summary>
    public ClassSelectionViewModel ClassSelection { get; } = new();

    private void RebuildClassSelection()
    {
        if (SelectedItem is null) ClassSelection.Clear();
        else ClassSelection.Rebuild(ClassEntries, SelectedItem.AllowedClasses);
        ClassSelection.IsActive = SelectedItem is not null;
    }

    protected override void AfterSave(ItemRowViewModel vm)
    {
        if (_data.IsOnline) _data.PatchOnlineItem(vm.Index, vm.Name, vm.Type);
    }

    protected override string SectionId => "Items";
    protected override string TypeName => EditorStrings.Get(EditorStrings.ItemEditor_TypeName);
    protected override string TypeNamePlural => EditorStrings.Get(EditorStrings.ItemEditor_TypeNamePlural);
    protected override int GetIndex(ItemRowViewModel vm) => vm.Index;
    protected override bool GetIsDirty(ItemRowViewModel vm) => vm.IsDirty;
    // ── Copy ──────────────────────────────────────────────────────────────────

    /// <summary>An unused slot, by the same rule the list already labels one: it has no name.</summary>
    protected override string GetName(ItemRowViewModel row) => row.Name;

    protected override bool GetIsLoaded(ItemRowViewModel row) => row.IsLoaded;

    protected override void CopyInto(ItemRowViewModel source, ItemRowViewModel target)
    {
        var rec = source.ToRecord();
        rec.Name += RecordCopy.Suffix;
        target.CopyFromRecord(rec);
    }

    protected override void ClearDirtyState(ItemRowViewModel vm) => vm.ClearDirty();

    public async Task EagerLoadAllAsync(CancellationToken ct)
    {
        if (!_data.IsOnline) return;
        var bulk = await _conn.RequestAllItemsAsync(ct);
        if (bulk is null) return;
        foreach (var pkt in bulk.Items)
        {
            var vm = Items.FirstOrDefault(v => v.Index == pkt.ItemNum);
            if (vm is not null) ApplyServerResponse(vm, pkt);
        }
        OnPropertyChanged(nameof(FilteredItems));
    }

    public NamedEntry[] SpellEntries => _data.LiveSpellEntries;

    public NamedEntry? SelectedSpellItem
    {
        get
        {
            if (SelectedItem is null) return null;
            var id = SelectedItem.SpellNum;
            return id > 0 && id < SpellEntries.Length ? SpellEntries[id] : null;
        }
        set
        {
            if (SelectedItem is null) return;
            var id = (short)(value?.Id ?? 0);
            if (SelectedItem.SpellNum == id) return;
            SelectedItem.SpellNum = id;
            OnPropertyChanged(nameof(SelectedSpellItem));
        }
    }

    partial void OnSelectedItemChanged(ItemRowViewModel? oldValue, ItemRowViewModel? newValue)
    {
        NotifyInboundRefsChanged();
        if (oldValue is not null) oldValue.PropertyChanged -= OnItemPropertyChanged;
        if (newValue is not null) newValue.PropertyChanged += OnItemPropertyChanged;
        NotifyDirtyState();
        if (newValue is not null && !newValue.IsLoaded && _data.IsOnline)
            _ = LoadEntityAsync(newValue);
        OnPropertyChanged(nameof(SelectedSpellItem));
        RebuildClassSelection();
        NotifyPicChanged();
    }

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ItemRowViewModel.SpellNum) or nameof(ItemRowViewModel.Type))
            OnPropertyChanged(nameof(SelectedSpellItem));
        // The sheet number chooses which bitmap the pic picker reads.
        if (e.PropertyName == nameof(ItemRowViewModel.ItemSheet)) NotifyPicChanged();
        // A row whose list changed from anywhere other than the checkboxes — a packet landing, a discard —
        // has to re-tick them. The guard skips the author's own clicks, which set the row FROM the
        // toggles; rebuilding there would clear and refill the list the click is still walking.
        if (e.PropertyName is nameof(ItemRowViewModel.AllowedClasses) && !_applyingClassSelection)
            RebuildClassSelection();
    }

    public void LoadOffline()
    {
        SelectedItem = null;
        Items.Clear();
        for (int i = 1; i < _data.OfflineItems.Length; i++)
            Items.Add(new ItemRowViewModel(i, _data.OfflineItems[i]));
        StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_LoadedOffline,
            ("Count", Items.Count), ("EntityType", TypeNamePlural));
    }

    public void LoadOnline()
    {
        if (_data.OnlineItems is null) return;
        SelectedItem = null;
        Items.Clear();
        foreach (var entry in _data.OnlineItems)
            Items.Add(new ItemRowViewModel(entry.Num, new ItemRecord { Name = entry.Name }, isLoaded: false));
        StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_LoadedOnline,
            ("Count", Items.Count), ("EntityType", TypeNamePlural));
    }

    protected override async Task<IPacket?> RequestFromServerAsync(ItemRowViewModel vm)
        => await _conn.RequestItemAsync(vm.Index);

    protected override void ApplyServerResponse(ItemRowViewModel vm, IPacket pkt)
        => vm.ApplyPacket((UpdateItemPacket)pkt);

    protected override IPacket BuildSavePacket(ItemRowViewModel vm) => vm.BuildSavePacket();

    protected override Task SaveOfflineAsync(ItemRowViewModel vm)
        => _data.SaveOfflineItemAsync(vm.Index, vm.ToRecord());

    protected override void LoadFromOfflineRecord(ItemRowViewModel vm)
        => vm.LoadFromRecord(_data.OfflineItems[vm.Index]);
}

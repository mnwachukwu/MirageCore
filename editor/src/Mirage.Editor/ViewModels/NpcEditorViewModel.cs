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

public sealed partial class NpcEditorViewModel : EditorViewModelBase<NpcRowViewModel>
{
    [ObservableProperty] private NpcRowViewModel? _selectedNpc;
    public override NpcRowViewModel? Selected => SelectedNpc;
    protected override void SetSelected(NpcRowViewModel? row) => SelectedNpc = row;
    public ObservableCollection<NpcRowViewModel> Npcs { get; } = [];
    public override ObservableCollection<NpcRowViewModel> Items => Npcs;
    protected override string GetFilterText(NpcRowViewModel row) => row.DisplayName;
    public IEnumerable<NpcBehavior> Behaviors { get; } = Enum.GetValues<NpcBehavior>();
    public IEnumerable<FlickerStyle> FlickerStyles { get; } = Enum.GetValues<FlickerStyle>();
    // NPC footprint sizes shown as pixel dimensions (32x32 / 64x64 / 96x96); the bound value is the 1..N class.
    public IReadOnlyList<NpcSizeOption> NpcSizes { get; } =
        Enumerable.Range(1, Constants.MaxNpcSize).Select(s => new NpcSizeOption(s)).ToArray();

    // Two-way bridge for the Size ComboBox: reads/writes SelectedNpc.Size as an NpcSizeOption (Avalonia's
    // ComboBox has no SelectedValuePath, so the int↔option mapping lives here). Refreshed on NPC change.
    public NpcSizeOption? SelectedNpcSize
    {
        get => SelectedNpc is null ? null : NpcSizes.FirstOrDefault(o => o.Value == SelectedNpc.Size);
        set { if (SelectedNpc is not null && value is not null) SelectedNpc.Size = value.Value; }
    }
    private static readonly IReadOnlyList<EnumFilterOption<NpcBehavior>> _behaviorFilters =
        Enum.GetValues<NpcBehavior>().Select(b => new EnumFilterOption<NpcBehavior>(b)).ToArray();
    public IReadOnlyList<EnumFilterOption<NpcBehavior>> BehaviorFilters => _behaviorFilters;
    [ObservableProperty] private EnumFilterOption<NpcBehavior>? _behaviorFilter;
    partial void OnBehaviorFilterChanged(EnumFilterOption<NpcBehavior>? value)
    {
        OnPropertyChanged(nameof(FilteredItems));
        OnPropertyChanged(nameof(FilterStatus));
        OnPropertyChanged(nameof(IsFilterActive));
    }
    protected override bool MatchesFilter(NpcRowViewModel row) =>
        base.MatchesFilter(row) && (BehaviorFilter is null || row.Behavior == BehaviorFilter.Value);
    public override bool IsFilterActive => base.IsFilterActive || BehaviorFilter is not null;
    [RelayCommand] private void ClearBehaviorFilter() => BehaviorFilter = null;

    public NamedEntry[] ItemEntries => _data.LiveItemEntries;

    // The sprite sheets, one set per footprint size. An NPC names a sheet number and a size, and those
    // are separate choices: the size picks which set to read, the number picks one sheet out of it.
    private IReadOnlyList<Bitmap?> _sprites = [];
    private IReadOnlyList<Bitmap?> _sprites64 = [];
    private IReadOnlyList<Bitmap?> _sprites96 = [];
    public NamedEntry[] SpriteSheetEntries { get; private set; } = [];

    public void SetSpriteSheets(IReadOnlyList<Bitmap?> s32, IReadOnlyList<Bitmap?> s64,
        IReadOnlyList<Bitmap?> s96, IReadOnlyList<string> names)
    {
        _sprites = s32;
        _sprites64 = s64;
        _sprites96 = s96;
        SpriteSheetEntries = SheetEntries.Build(s32.Count, names);
        OnPropertyChanged(nameof(SpriteSheetEntries));
        NotifySpriteChanged();
    }

    // The sheet the selected NPC actually draws from, at its own footprint size. Previewing a size-2
    // NPC from the 32x32 set showed whichever creature happened to share its row number there.
    public Bitmap? SpriteBitmap => SheetAt(SizedSprites(SelectedNpc?.Size ?? 1), SelectedNpc?.SpriteSheet ?? 0);
    /// <summary>Source cell size of the selected NPC's sheet, in pixels (32/64/96).</summary>
    public int SpriteCellSize => Math.Clamp(SelectedNpc?.Size ?? 1, 1, Constants.MaxNpcSize) * Constants.PicX;
    public IReadOnlyList<int> SpriteEntries { get; private set; } = [];

    // Two-way bridge for the sheet typeahead, the same shape the Size picker uses.
    public NamedEntry? SelectedSpriteSheetEntry
    {
        get
        {
            int sheet = SelectedNpc?.SpriteSheet ?? 0;
            return sheet >= 0 && sheet < SpriteSheetEntries.Length ? SpriteSheetEntries[sheet] : null;
        }
        set { if (SelectedNpc is not null && value is not null) SelectedNpc.SpriteSheet = value.Id; }
    }

    private IReadOnlyList<Bitmap?> SizedSprites(int size) =>
        size >= 3 ? _sprites96 : size == 2 ? _sprites64 : _sprites;

    private static Bitmap? SheetAt(IReadOnlyList<Bitmap?> sheets, int index) =>
        (uint)index < (uint)sheets.Count ? sheets[index] : null;

    // Every input to the preview at once: the sheet set, the selected NPC, its sheet number and its size
    // all move the same three properties, so they are re-raised together rather than one hook each.
    private void NotifySpriteChanged()
    {
        var bmp = SpriteBitmap;
        int cell = SpriteCellSize;
        int rows = bmp is null ? 0 : (int)(bmp.Size.Height / cell);
        SpriteEntries = Enumerable.Range(0, Math.Max(0, rows)).ToArray();
        OnPropertyChanged(nameof(SpriteBitmap));
        OnPropertyChanged(nameof(SpriteCellSize));
        OnPropertyChanged(nameof(SpriteEntries));
        OnPropertyChanged(nameof(SelectedSpriteSheetEntry));
    }

    // The drop picker moved ONTO the rows: a drop table has many item slots rather than one, so each
    // NpcDropRowViewModel owns its own picker and currency-aware quantity rule. This VM's job is now just
    // to hand every row the live item list, and to re-raise it when that list changes.
    private void AttachDropProviders(NpcRowViewModel? row) =>
        row?.AttachItemProviders(() => _data.LiveItemEntries, _data.IsCurrencyItem);

    public NpcEditorViewModel(EditorDataService data, EditorConnection conn) : base(data, conn)
    {
        HookItems();
        // Refresh the item dropdowns on every drop row too: an item's currency-ness can flip under a
        // selected NPC without its drop lines changing, which would otherwise leave a quantity rule stale.
        _data.EntriesInvalidated += () =>
        {
            OnPropertyChanged(nameof(ItemEntries));
            SelectedNpc?.NotifyDropEntriesChanged();
        };
    }

    protected override void AfterSave(NpcRowViewModel vm)
    {
        if (_data.IsOnline) _data.PatchOnlineNpcName(vm.Index, vm.Name);
    }

    protected override string SectionId => "NPCs";
    protected override string TypeName => EditorStrings.Get(EditorStrings.NpcEditor_TypeName);
    protected override string TypeNamePlural => EditorStrings.Get(EditorStrings.NpcEditor_TypeNamePlural);
    protected override int GetIndex(NpcRowViewModel vm) => vm.Index;
    protected override bool GetIsDirty(NpcRowViewModel vm) => vm.IsDirty;
    // ── Copy ──────────────────────────────────────────────────────────────────

    /// <summary>An unused slot, by the same rule the list already labels one: it has no name.</summary>
    protected override string GetName(NpcRowViewModel row) => row.Name;

    protected override bool GetIsLoaded(NpcRowViewModel row) => row.IsLoaded;

    protected override void CopyInto(NpcRowViewModel source, NpcRowViewModel target)
    {
        var rec = source.ToRecord();
        rec.Name += RecordCopy.Suffix;
        target.CopyFromRecord(rec);
    }

    protected override void ClearDirtyState(NpcRowViewModel vm) => vm.ClearDirty();

    public async Task EagerLoadAllAsync(CancellationToken ct)
    {
        if (!_data.IsOnline) return;
        var bulk = await _conn.RequestAllNpcsAsync(ct);
        if (bulk is null) return;
        foreach (var pkt in bulk.Npcs)
        {
            var vm = Items.FirstOrDefault(v => v.Index == pkt.NpcNum);
            if (vm is not null) ApplyServerResponse(vm, pkt);
        }
        OnPropertyChanged(nameof(FilteredItems));
    }

    partial void OnSelectedNpcChanged(NpcRowViewModel? oldValue, NpcRowViewModel? newValue)
    {
        NotifyInboundRefsChanged();
        if (oldValue is not null) oldValue.PropertyChanged -= OnNpcPropertyChanged;
        if (newValue is not null) newValue.PropertyChanged += OnNpcPropertyChanged;
        NotifyDirtyState();
        OnPropertyChanged(nameof(SelectedNpcSize));
        NotifySpriteChanged();
        // Wire the picker on selection rather than at construction: rows are built in bulk (and lazily for
        // online placeholders), and only the selected one is ever showing a drop table.
        AttachDropProviders(newValue);
        if (newValue is not null && !newValue.IsLoaded && _data.IsOnline)
            _ = LoadEntityAsync(newValue);
    }
    private void OnNpcPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Drop-table changes raise their own derived properties on the row (yield text, config warning),
        // so the only thing left for the editor VM to mirror is the art preview: both the sheet number
        // and the footprint size choose which bitmap the sprite picker reads.
        if (e.PropertyName is nameof(NpcRowViewModel.SpriteSheet) or nameof(NpcRowViewModel.Size))
            NotifySpriteChanged();
    }

    public void LoadOffline()
    {
        SelectedNpc = null;
        Npcs.Clear();
        for (int i = 1; i < _data.OfflineNpcs.Length; i++)
            Npcs.Add(new NpcRowViewModel(i, _data.OfflineNpcs[i]));
        StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_LoadedOffline,
            ("Count", Npcs.Count), ("EntityType", TypeNamePlural));
    }

    public void LoadOnline()
    {
        if (_data.OnlineNpcs is null) return;
        SelectedNpc = null;
        Npcs.Clear();
        foreach (var entry in _data.OnlineNpcs)
            Npcs.Add(new NpcRowViewModel(entry.Num, new NpcRecord { Name = entry.Name }, isLoaded: false));
        StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_LoadedOnline,
            ("Count", Npcs.Count), ("EntityType", TypeNamePlural));
    }

    protected override async Task<IPacket?> RequestFromServerAsync(NpcRowViewModel vm)
        => await _conn.RequestNpcAsync(vm.Index);

    protected override void ApplyServerResponse(NpcRowViewModel vm, IPacket pkt)
        => vm.ApplyPacket((UpdateNpcPacket)pkt);

    protected override IPacket BuildSavePacket(NpcRowViewModel vm) => vm.BuildSavePacket();

    protected override Task SaveOfflineAsync(NpcRowViewModel vm)
        => _data.SaveOfflineNpcAsync(vm.Index, vm.ToRecord());

    protected override void LoadFromOfflineRecord(NpcRowViewModel vm)
        => vm.LoadFromRecord(_data.OfflineNpcs[vm.Index]);
}

/// <summary>A selectable NPC footprint size shown as its pixel dimensions (e.g. "64x64"); <see cref="Value"/>
/// is the 1..N size class stored on the NPC (bound via the ComboBox's SelectedValuePath).</summary>
public sealed record NpcSizeOption(int Value)
{
    public override string ToString() => $"{Value * Constants.PicX}x{Value * Constants.PicX}";
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Editor.Models;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
namespace Mirage.Editor.ViewModels;

public sealed partial class ShopRowViewModel : ObservableObject, ILockableRow
{
    /// <inheritdoc/>
    [ObservableProperty] private bool _lockedByOther;
    /// <inheritdoc/>
    [ObservableProperty] private string _lockHolder = "";

    public int Index { get; }
    public bool IsLoaded { get; private set; }

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private bool _fixesItems;
    [ObservableProperty] private ShopType _shopType;
    [ObservableProperty] private bool _allowBanking;
    // Keeper: the NPC template this shop/inn is assigned to (0 = none). SelectedKeeper is the NamedEntry facade
    // the DropdownAutoCompleteBox binds to; KeeperEntries is the live NPC list.
    [ObservableProperty] private int _keeper;

    public bool IsStore
    {
        get => ShopType == ShopType.Store;
        set { if (value && ShopType != ShopType.Store) ShopType = ShopType.Store; }
    }
    public bool IsInn
    {
        get => ShopType == ShopType.Inn;
        set { if (value && ShopType != ShopType.Inn) ShopType = ShopType.Inn; }
    }

    public ObservableCollection<ShopBarterRowViewModel> Barters { get; } = [];

    /// <summary>The shop's SALES table — items sold for gold at <see cref="ItemRecord.Price"/>, in the order
    /// the player sees them. Order is authored, not derived: <c>ShopRecord.Normalize</c> deliberately keeps
    /// it, so the move-up/move-down commands are editing a real property rather than a cosmetic one.</summary>
    public ObservableCollection<ShopSalesRowViewModel> Sales { get; } = [];

    /// <summary>True when the barter table has no rows — drives the editor's empty-state hint.</summary>
    public bool HasNoBarters => Barters.Count == 0;

    /// <summary>True when the sales table has no rows.</summary>
    public bool HasNoSales => Sales.Count == 0;

    /// <summary>"N items, worth X gold" — the running total, because a storefront's job is a price list and
    /// the sum is the thing that is hard to eyeball down a column of forty.</summary>
    public string SalesSummary
    {
        get
        {
            int live = Sales.Count(s => !s.IsEmpty);
            if (live == 0) return string.Empty;
            long total = Sales.Where(s => !s.IsEmpty).Sum(s => (long)s.Price);
            return EditorStrings.Format(EditorStrings.ShopEditor_SalesSummary, ("Items", live), ("Gold", $"{total:n0}"));
        }
    }

    // Non-blocking authoring guards, mirroring the NPC drop table's. Neither is an error — the record's
    // Normalize drops both on load — but both are silent, and silent is what makes them worth surfacing.
    /// <summary>Warning text for a sales table that lists the same item twice, or lists an unpriced one.</summary>
    public string SalesWarning
    {
        get
        {
            var live = Sales.Where(s => !s.IsEmpty).ToList();
            if (live.Select(s => s.ItemNum).Distinct().Count() != live.Count)
                return EditorStrings.Get(EditorStrings.ShopEditor_SalesWarnDuplicate);
            return live.Any(s => s.HasNoPrice) ? EditorStrings.Get(EditorStrings.ShopEditor_SalesWarnNoPrice) : string.Empty;
        }
    }
    /// <summary>Whether to show the sales-configuration warning.</summary>
    public bool HasSalesWarning => SalesWarning.Length > 0;

    public bool IsDirty => _textDirty || _fixesDirty || _typeDirty || _structuralDirty
        || Barters.Any(t => t.IsDirty) || Sales.Any(s => s.IsDirty);
    private bool _textDirty;
    private bool _fixesDirty;
    private bool _typeDirty;
    private bool _structuralDirty;   // a barter/sales row was added, removed or REORDERED (not a field edit)
    private bool _loading;

    public string DisplayName => $"{Index}: {(string.IsNullOrEmpty(Name) ? EditorStrings.Get(EditorStrings.Common_EmptyName) : Name)}";

    private readonly Func<NamedEntry[]> _entriesProvider;
    private readonly Func<NamedEntry[]> _npcEntriesProvider;
    private readonly Func<int, bool> _isCurrency;
    // Defaulted rather than required so the plain `new ShopRowViewModel(...)` sites (and the tests) keep
    // working; a row built without it still round-trips its sales table, it just cannot show prices.
    private readonly Func<int, int?> _priceOf;

    public NamedEntry[] KeeperEntries => _npcEntriesProvider();

    public NamedEntry? SelectedKeeper
    {
        get => EntryFor(KeeperEntries, Keeper);
        set
        {
            var id = value?.Id ?? 0;
            if (Keeper == id) return;
            Keeper = id;   // OnKeeperChanged marks dirty + re-notifies SelectedKeeper
        }
    }

    private static NamedEntry? EntryFor(NamedEntry[] entries, int id) =>
        id > 0 && id < entries.Length ? entries[id] : null;

    public ShopRowViewModel(int index, ShopRecord r, Func<NamedEntry[]> entriesProvider, Func<NamedEntry[]> npcEntriesProvider, Func<int, bool> isCurrency, Func<int, int?>? priceOf = null, bool isLoaded = true)
    {
        Index = index;
        IsLoaded = isLoaded;
        _entriesProvider = entriesProvider;
        _npcEntriesProvider = npcEntriesProvider;
        _isCurrency = isCurrency;
        _priceOf = priceOf ?? (_ => null);
        _name = r.Name;
        _fixesItems = r.FixesItems;
        _shopType = r.ShopType;
        _allowBanking = r.AllowBanking;
        _keeper = r.Keeper;

        Barters.CollectionChanged += OnBartersCollectionChanged;
        Sales.CollectionChanged += OnSalesCollectionChanged;
        // Guarded exactly like the NPC drop table: building child rows subscribes handlers that land on the
        // dirty flag, and an unguarded load flags every stocked shop as edited the moment it is opened.
        _loading = true;
        try
        {
            LoadBarters(r);
            LoadSales(r.SalesItem);
        }
        finally { _loading = false; }
        ClearDirty();   // a row built straight from disk has not been edited
    }

    private void OnBartersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (ShopBarterRowViewModel t in e.OldItems)
                t.PropertyChanged -= OnBarterRowPropertyChanged;
        }

        if (e.NewItems is not null)
        {
            foreach (ShopBarterRowViewModel t in e.NewItems)
                t.PropertyChanged += OnBarterRowPropertyChanged;
        }

        OnPropertyChanged(nameof(HasNoBarters));
        if (_loading) return;
        _structuralDirty = true;
        OnPropertyChanged(nameof(IsDirty));
    }

    [RelayCommand]
    private void AddBarter() =>
        Barters.Add(new ShopBarterRowViewModel(Barters.Count + 1, new BarterItemRecord(), _entriesProvider, _isCurrency));

    [RelayCommand]
    private void RemoveBarter(ShopBarterRowViewModel row) => Barters.Remove(row);

    private void OnBarterRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_loading) return;
        OnPropertyChanged(nameof(IsDirty));
    }

    // ── Sales table ───────────────────────────────────────────────────────────

    private void OnSalesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (ShopSalesRowViewModel s in e.OldItems) s.PropertyChanged -= OnSalesRowPropertyChanged;
        if (e.NewItems is not null)
            foreach (ShopSalesRowViewModel s in e.NewItems) s.PropertyChanged += OnSalesRowPropertyChanged;

        RenumberSales();
        NotifySalesDerived();
        MoveSaleUpCommand.NotifyCanExecuteChanged();
        MoveSaleDownCommand.NotifyCanExecuteChanged();
        if (_loading) return;
        _structuralDirty = true;
        OnPropertyChanged(nameof(IsDirty));
    }

    private void OnSalesRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Only a row reporting its OWN edit counts — the same rule the drop table uses. NotifySalesDerived
        // re-raises across every row and runs on selection, so treating any raise as an edit would mark a
        // shop modified merely by opening it.
        NotifySalesDerived();
        if (_loading || sender is not ShopSalesRowViewModel { IsDirty: true }) return;
        OnPropertyChanged(nameof(IsDirty));
    }

    // SlotIndex IS the player-visible position, so it has to follow every insert, remove and reorder.
    private void RenumberSales()
    {
        for (int i = 0; i < Sales.Count; i++) Sales[i].SlotIndex = i + 1;
    }

    private void NotifySalesDerived()
    {
        OnPropertyChanged(nameof(HasNoSales));
        OnPropertyChanged(nameof(SalesSummary));
        OnPropertyChanged(nameof(SalesWarning));
        OnPropertyChanged(nameof(HasSalesWarning));
    }

    /// <summary>Add an empty sales row. Unbounded — a storefront is "these items", and the seeded ones
    /// already run to dozens; the only ceiling is the item table itself.</summary>
    [RelayCommand]
    private void AddSale() =>
        Sales.Add(new ShopSalesRowViewModel(Sales.Count + 1, 0, _entriesProvider, _priceOf));

    [RelayCommand]
    private void RemoveSale(ShopSalesRowViewModel row) => Sales.Remove(row);

    [RelayCommand(CanExecute = nameof(CanMoveSaleUp))]
    private void MoveSaleUp(ShopSalesRowViewModel row)
    {
        int i = Sales.IndexOf(row);
        if (i > 0) Sales.Move(i, i - 1);
    }
    private bool CanMoveSaleUp(ShopSalesRowViewModel? row) => row is not null && Sales.IndexOf(row) > 0;

    [RelayCommand(CanExecute = nameof(CanMoveSaleDown))]
    private void MoveSaleDown(ShopSalesRowViewModel row)
    {
        int i = Sales.IndexOf(row);
        if (i >= 0 && i < Sales.Count - 1) Sales.Move(i, i + 1);
    }
    private bool CanMoveSaleDown(ShopSalesRowViewModel? row) =>
        row is not null && Sales.IndexOf(row) is >= 0 and var i && i < Sales.Count - 1;

    // Build sales rows from a list of item numbers. Callers set _loading around this.
    private void LoadSales(List<int> nums)
    {
        Sales.Clear();
        int slot = 1;
        foreach (int num in nums)
        {
            if (num <= 0) continue;   // the record's Normalize drops these too; don't show a dead row
            Sales.Add(new ShopSalesRowViewModel(slot++, num, _entriesProvider, _priceOf));
        }
    }

    partial void OnNameChanged(string value)
    {
        if (_loading) return;
        _textDirty = true;
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(IsDirty));
    }
    partial void OnFixesItemsChanged(bool value)
    {
        if (_loading) return;
        _fixesDirty = true;
        OnPropertyChanged(nameof(IsDirty));
    }
    partial void OnShopTypeChanged(ShopType value)
    {
        if (_loading) return;
        _typeDirty = true;
        OnPropertyChanged(nameof(IsStore));
        OnPropertyChanged(nameof(IsInn));
        OnPropertyChanged(nameof(IsDirty));
    }
    partial void OnAllowBankingChanged(bool value)
    {
        if (_loading) return;
        _typeDirty = true;
        OnPropertyChanged(nameof(IsDirty));
    }
    partial void OnKeeperChanged(int value)
    {
        // Refresh the dropdown selection even while loading a record; only an actual edit dirties the row.
        OnPropertyChanged(nameof(SelectedKeeper));
        if (_loading) return;
        _typeDirty = true;
        OnPropertyChanged(nameof(IsDirty));
    }

    public void ClearDirty()
    {
        _textDirty = _fixesDirty = _typeDirty = _structuralDirty = false;
        // Child rows too: one left dirty re-marks the shop on its next derived re-raise, and the dot comes
        // straight back with nobody having touched anything.
        foreach (var t in Barters) t.ClearDirty();
        foreach (var s in Sales) s.ClearDirty();
        OnPropertyChanged(nameof(IsDirty));
    }

    public void NotifyEntriesChanged()
    {
        OnPropertyChanged(nameof(KeeperEntries));
        OnPropertyChanged(nameof(SelectedKeeper));
        foreach (var t in Barters) t.NotifyEntriesChanged();
        foreach (var s in Sales) s.NotifyEntriesChanged();
        NotifySalesDerived();   // prices may have moved under us — the summary and warnings follow
    }

    // Build barter rows from a record, skipping empties and the legacy null-at-index-0, so the table shows
    // only real barters (dense). Callers set _loading around this so the rebuild doesn't dirty the row.
    private void LoadBarters(ShopRecord r)
    {
        Barters.Clear();
        int slot = 1;
        foreach (var t in r.BarterItem)
        {
            if (t is null || (t.GiveItem <= 0 && t.GetItem <= 0)) continue;
            Barters.Add(new ShopBarterRowViewModel(slot++, t, _entriesProvider, _isCurrency));
        }
    }

    /// <summary>Fill from a record and leave the row DIRTY and loaded — the copy path, where the new
    /// record exists only in memory until a save persists it.
    /// <para>Marking it LOADED matters online: an unloaded row lazy-fetches when selected, and that fetch
    /// would land after the copy and overwrite it with the empty slot the server still holds.</para></summary>
    public void CopyFromRecord(ShopRecord r)
    {
        LoadFromRecord(r);
        IsLoaded = true;
        _structuralDirty = true;
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(DisplayName));
    }

    public void LoadFromRecord(ShopRecord r)
    {
        _loading = true;
        try
        {
            Name = r.Name;
            FixesItems = r.FixesItems;
            ShopType = r.ShopType;
            AllowBanking = r.AllowBanking;
            Keeper = r.Keeper;
            LoadBarters(r);
            LoadSales(r.SalesItem);
        }
        finally
        {
            _loading = false;
        }
        ClearDirty();
        OnPropertyChanged(nameof(DisplayName));
    }

    public void ApplyPacket(UpdateShopPacket pkt)
    {
        _loading = true;
        try
        {
            Name = pkt.Name;
            FixesItems = pkt.FixesItems;
            ShopType = pkt.ShopType;
            AllowBanking = pkt.AllowBanking;
            Keeper = pkt.Keeper;
            LoadSales([.. pkt.Sales]);
            Barters.Clear();
            int slot = 1;
            foreach (var t in pkt.Barters)
            {
                if (t.GiveItem <= 0 && t.GetItem <= 0) continue;   // skip empty rows — table stays dense
                Barters.Add(new ShopBarterRowViewModel(slot++, new BarterItemRecord
                {
                    GiveItem = t.GiveItem,
                    GiveQuantity = t.GiveQuantity,
                    GetItem = t.GetItem,
                    GetQuantity = t.GetQuantity,
                }, _entriesProvider, _isCurrency));
            }
        }
        finally
        {
            _loading = false;
        }

        IsLoaded = true;
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(IsDirty));
    }

    public ShopRecord ToRecord()
    {
        var r = new ShopRecord
        {
            Name = Name,
            FixesItems = FixesItems,
            ShopType = ShopType,
            AllowBanking = AllowBanking,
            Keeper = Keeper,
            // Dense and in authored order — the order IS the shopfront. Half-authored rows are dropped here
            // the same way empty barters are, so the file says what it means.
            SalesItem = [.. Sales.Where(s => !s.IsEmpty).Select(s => s.ItemNum)],
        };
        // Persist a dense list of real barters only — empty rows (either side itemless) are dropped, so a
        // blank row anywhere leaves no gap in the saved data.
        r.BarterItem = Barters.Where(t => !t.IsEmpty).Select(t => t.ToRecord()).ToList();
        return r;
    }

    /// <summary>Project the row into the online save packet. The single source of that mapping — both the
    /// editor's own save and the push-changes prompt route through here, so neither can drift from the other.</summary>
    public EditorSaveShopPacket BuildSavePacket() => new()
    {
        ShopNum = Index,
        Name = Name,
        FixesItems = FixesItems,
        ShopType = ShopType,
        AllowBanking = AllowBanking,
        Keeper = Keeper,
        Barters = Barters.Select(t => new EditorSaveShopPacket.BarterEntry(
            t.GiveItem, t.GiveQuantity, t.GetItem, t.GetQuantity)).ToArray(),
        Sales = [.. Sales.Where(s => !s.IsEmpty).Select(s => s.ItemNum)],
    };
}

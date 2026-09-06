using CommunityToolkit.Mvvm.ComponentModel;
using Mirage.Editor.Localization;
using Mirage.Editor.Services;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using System.Collections.ObjectModel;

namespace Mirage.Editor.ViewModels;

/// <summary>The shop/inn editor. Rows are handed live item and NPC entry lookups so their trade and
/// keeper pickers stay current, and are refreshed when those entry lists are invalidated.</summary>
public sealed partial class ShopEditorViewModel : EditorViewModelBase<ShopRowViewModel>
{
    [ObservableProperty] private ShopRowViewModel? _selectedShop;
    public override ShopRowViewModel? Selected => SelectedShop;
    protected override void SetSelected(ShopRowViewModel? row) => SelectedShop = row;
    public ObservableCollection<ShopRowViewModel> Shops { get; } = [];
    public override ObservableCollection<ShopRowViewModel> Items => Shops;
    /// <inheritdoc/>
    protected override string GetFilterText(ShopRowViewModel row) => row.DisplayName;

    public ShopEditorViewModel(EditorDataService data, EditorConnection conn) : base(data, conn)
    {
        HookItems();
        _data.EntriesInvalidated += () => { foreach (var s in Shops) s.NotifyEntriesChanged(); };
    }

    protected override string SectionId => "Shops";
    protected override string TypeName => EditorStrings.Get(EditorStrings.ShopEditor_TypeName);
    protected override string TypeNamePlural => EditorStrings.Get(EditorStrings.ShopEditor_TypeNamePlural);
    /// <inheritdoc/>
    protected override int GetIndex(ShopRowViewModel vm) => vm.Index;
    /// <inheritdoc/>
    protected override bool GetIsDirty(ShopRowViewModel vm) => vm.IsDirty;
    /// <inheritdoc/>
    // ── Copy ──────────────────────────────────────────────────────────────────

    /// <summary>An unused slot, by the same rule the list already labels one: it has no name.</summary>
    protected override string GetName(ShopRowViewModel row) => row.Name;

    protected override bool GetIsLoaded(ShopRowViewModel row) => row.IsLoaded;

    protected override void CopyInto(ShopRowViewModel source, ShopRowViewModel target)
    {
        var rec = source.ToRecord();
        rec.Name += RecordCopy.Suffix;
        // The keeper is a side-mapping the server resolves by SCANNING for the NPC, so two shops naming
        // the same keeper means one of them silently never opens. The copy arrives unattached.
        rec.Keeper = 0;
        target.CopyFromRecord(rec);
    }

    protected override void ClearDirtyState(ShopRowViewModel vm) => vm.ClearDirty();

    /// <summary>Pre-fill every placeholder row from one bulk server response, so browsing the list after
    /// connecting is instant instead of fetching per selection. No-op offline; canceled on disconnect.</summary>
    public async Task EagerLoadAllAsync(CancellationToken ct)
    {
        if (!_data.IsOnline) return;
        var bulk = await _conn.RequestAllShopsAsync(ct);
        if (bulk is null) return;
        foreach (var pkt in bulk.Shops)
        {
            var vm = Items.FirstOrDefault(v => v.Index == pkt.ShopNum);
            if (vm is not null) ApplyServerResponse(vm, pkt);
        }
        OnPropertyChanged(nameof(FilteredItems));
    }

    partial void OnSelectedShopChanged(ShopRowViewModel? value)
    {
        NotifyInboundRefsChanged();
        NotifyDirtyState();
        if (value is not null && !value.IsLoaded && _data.IsOnline)
            _ = LoadEntityAsync(value);
    }

    /// <summary>Rebuild the list from the on-disk records, fully populated — offline editing has no
    /// server to lazy-load from.</summary>
    public void LoadOffline()
    {
        SelectedShop = null;
        Shops.Clear();
        for (int i = 1; i < _data.OfflineShops.Length; i++)
            Shops.Add(new ShopRowViewModel(i, _data.OfflineShops[i], () => _data.LiveItemEntries, () => _data.LiveNpcEntries, _data.IsCurrencyItem, _data.ItemPrice));
        StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_LoadedOffline,
            ("Count", Shops.Count), ("EntityType", TypeNamePlural));
    }

    /// <summary>Rebuild the list from the server's name index as NAME-ONLY placeholders
    /// (<c>isLoaded: false</c>). Each row's full definition arrives when it is selected, or sooner via
    /// <see cref="EagerLoadAllAsync"/>.</summary>
    public void LoadOnline()
    {
        if (_data.OnlineShops is null) return;
        SelectedShop = null;
        Shops.Clear();
        foreach (var entry in _data.OnlineShops)
            Shops.Add(new ShopRowViewModel(entry.Num, new ShopRecord { Name = entry.Name }, () => _data.LiveItemEntries, () => _data.LiveNpcEntries, _data.IsCurrencyItem, _data.ItemPrice, isLoaded: false));
        StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_LoadedOnline,
            ("Count", Shops.Count), ("EntityType", TypeNamePlural));
    }

    /// <inheritdoc/>
    protected override async Task<IPacket?> RequestFromServerAsync(ShopRowViewModel vm)
        => await _conn.RequestShopAsync(vm.Index);

    /// <inheritdoc/>
    protected override void ApplyServerResponse(ShopRowViewModel vm, IPacket pkt)
        => vm.ApplyPacket((UpdateShopPacket)pkt);

    /// <inheritdoc/>
    protected override IPacket BuildSavePacket(ShopRowViewModel vm) => vm.BuildSavePacket();

    /// <summary>Patch the cached online name index after a save, so the list caption reflects a renamed
    /// record without re-fetching the whole index.</summary>
    protected override void AfterSave(ShopRowViewModel vm)
    {
        if (_data.IsOnline) _data.PatchOnlineShopName(vm.Index, vm.Name);
    }

    /// <inheritdoc/>
    protected override Task SaveOfflineAsync(ShopRowViewModel vm)
        => _data.SaveOfflineShopAsync(vm.Index, vm.ToRecord());

    /// <inheritdoc/>
    protected override void LoadFromOfflineRecord(ShopRowViewModel vm)
        => vm.LoadFromRecord(_data.OfflineShops[vm.Index]);
}

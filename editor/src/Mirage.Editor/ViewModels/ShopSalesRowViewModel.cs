using CommunityToolkit.Mvvm.ComponentModel;
using Mirage.Editor.Localization;
using Mirage.Editor.Models;
namespace Mirage.Editor.ViewModels;

/// <summary>One line of a shop's SALES table: an item, and nothing else.
///
/// <para>Deliberately thinner than <see cref="ShopBarterRowViewModel"/>, and that thinness is the whole reason
/// the sales table exists. A barter row has to say what is given AND what is got, with a quantity on each
/// side; a sales row is just "this shop stocks item N", because price lives on the item record. Authoring a
/// 40-item storefront as barter rows would mean writing 40 give→get pairs that all say "some gold".</para>
///
/// <para>The price shown here is READ-ONLY and comes from the item, not from this row — editing it belongs
/// in the item editor, where changing it moves the price at every vendor at once. That is the accepted
/// trade-off of pricing from the item; the barter table is the escape hatch when a vendor must differ.</para>
/// </summary>
public sealed partial class ShopSalesRowViewModel : ObservableObject
{
    private readonly Func<NamedEntry[]> _itemEntriesProvider;
    private readonly Func<int, int?> _priceOf;

    /// <summary>1-based position in the shopfront. This IS the order the player sees, so it is authored
    /// rather than derived — see the move-up/move-down commands on the shop row.</summary>
    [ObservableProperty] private int _slotIndex;

    public string ItemPlaceholder => EditorStrings.Get(EditorStrings.ShopEditor_SalesItemPlaceholder);

    [ObservableProperty] private int _itemNum;

    public bool IsDirty { get; private set; }

    public NamedEntry[] ItemEntries => _itemEntriesProvider();

    public NamedEntry? SelectedItem
    {
        get => EntryFor(ItemEntries, ItemNum);
        set
        {
            var id = value?.Id ?? 0;
            if (ItemNum == id) return;
            ItemNum = id;   // OnItemNumChanged marks dirty + re-notifies the price readout
        }
    }

    /// <summary>Gold the player pays for this line, straight off the item record.</summary>
    public int Price => _priceOf(ItemNum) ?? 0;

    /// <summary>The price as display text — blank on an empty row, and an explicit warning at zero.
    /// A zero-priced item on a shopfront is given away for nothing, which is almost never intended and is
    /// invisible unless the table says so.</summary>
    public string PriceText =>
        ItemNum <= 0 ? string.Empty
      : Price <= 0 ? EditorStrings.Get(EditorStrings.ShopEditor_SalesNoPrice)
      : $"{Price:n0}";

    /// <summary>Whether to render the price in the warning color (an unpriced item).</summary>
    public bool HasNoPrice => ItemNum > 0 && Price <= 0;

    /// <summary>Exact complement of <see cref="HasNoPrice"/>, so the two price readouts in the row template
    /// are mutually exclusive and cannot render on top of each other. An empty row shows neither.</summary>
    public bool HasPrice => ItemNum > 0 && Price > 0;

    /// <summary>An unused row — no item. Dropped when the shop is saved.</summary>
    public bool IsEmpty => ItemNum <= 0;

    public ShopSalesRowViewModel(int slotIndex, int itemNum, Func<NamedEntry[]> itemEntriesProvider, Func<int, int?> priceOf)
    {
        _slotIndex = slotIndex;
        _itemNum = itemNum;
        _itemEntriesProvider = itemEntriesProvider;
        _priceOf = priceOf;
    }

    partial void OnItemNumChanged(int value)
    {
        IsDirty = true;
        OnPropertyChanged(nameof(SelectedItem));
        NotifyPriceChanged();
    }

    public void ClearDirty() => IsDirty = false;

    /// <summary>Re-raise everything that depends on the item list. Unlike the quantity-bearing rows this
    /// writes NO state, so there is no refresh-marks-dirty trap here — see NpcDropRowViewModel.</summary>
    public void NotifyEntriesChanged()
    {
        OnPropertyChanged(nameof(ItemEntries));
        OnPropertyChanged(nameof(SelectedItem));
        NotifyPriceChanged();
    }

    private void NotifyPriceChanged()
    {
        OnPropertyChanged(nameof(Price));
        OnPropertyChanged(nameof(PriceText));
        OnPropertyChanged(nameof(HasPrice));
        OnPropertyChanged(nameof(HasNoPrice));
    }

    private static NamedEntry? EntryFor(NamedEntry[] entries, int id) =>
        id > 0 && id < entries.Length ? entries[id] : null;
}

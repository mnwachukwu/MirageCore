using CommunityToolkit.Mvvm.ComponentModel;
using Mirage.Editor.Localization;
using Mirage.Editor.Models;
using Mirage.Shared.Records;
namespace Mirage.Editor.ViewModels;

/// <summary>One authored quest reward: an item + a quantity (gold is item #1). Mirrors the
/// give/get side of ShopBarterRowViewModel — an item picker facade + currency-aware qty min/max + dirty tracking.
/// An empty row (no item) is dropped on save.</summary>
public sealed partial class QuestRewardRowViewModel : ObservableObject
{
    private readonly Func<NamedEntry[]> _itemEntriesProvider;
    private readonly Func<int, bool> _isCurrency;

    public int SlotIndex { get; }

    public string ItemPlaceholder => EditorStrings.Get(EditorStrings.QuestEditor_ItemPlaceholder);

    [ObservableProperty] private int _itemNum;
    [ObservableProperty] private int _value;

    public bool IsDirty { get; private set; }

    public NamedEntry[] ItemEntries => _itemEntriesProvider();

    // Quantity rule (mirrors BarterRow): no item → 0; a non-currency item → exactly 1 (never stacks); a currency
    // item → 1..9999. Bound to the NumericUpDown Min/Max; CoerceValue is the authoritative backstop.
    public int ValueMin => ItemNum > 0 ? 1 : 0;
    public int ValueMax => ItemNum <= 0 ? 0 : (_isCurrency(ItemNum) ? 9999 : 1);

    public NamedEntry? SelectedItem
    {
        get => EntryFor(ItemEntries, ItemNum);
        set
        {
            var id = value?.Id ?? 0;
            if (ItemNum == id) return;
            ItemNum = id;   // OnItemNumChanged marks dirty + re-notifies
        }
    }

    public QuestRewardRowViewModel(int slotIndex, QuestReward r, Func<NamedEntry[]> itemEntriesProvider, Func<int, bool> isCurrency)
    {
        SlotIndex = slotIndex;
        _itemEntriesProvider = itemEntriesProvider;
        _isCurrency = isCurrency;
        _itemNum = r.ItemNum;
        _value = r.Quantity;
    }

    partial void OnItemNumChanged(int value)
    {
        IsDirty = true;
        OnPropertyChanged(nameof(SelectedItem));
        OnPropertyChanged(nameof(ValueMin));
        OnPropertyChanged(nameof(ValueMax));
        CoerceValue();
    }
    partial void OnValueChanged(int value)
    {
        if (!_refreshing) IsDirty = true;
        CoerceValue();
    }

    private void CoerceValue()
    {
        int c = ItemNum <= 0 ? 0 : (!_isCurrency(ItemNum) ? 1 : (Value < 1 ? 1 : Value));
        if (Value != c) Value = c;
    }

    /// <summary>An unused reward row — no item. Dropped when the quest is saved.</summary>
    public bool IsEmpty => ItemNum <= 0 || Value <= 0;

    public void ClearDirty() => IsDirty = false;

    // Set while a REFRESH re-normalizes the row — see NpcDropRowViewModel for the failure this prevents.
    private bool _refreshing;

    public void NotifyEntriesChanged()
    {
        OnPropertyChanged(nameof(ItemEntries));
        OnPropertyChanged(nameof(SelectedItem));
        OnPropertyChanged(nameof(ValueMin));
        OnPropertyChanged(nameof(ValueMax));
        // Normalization, not an author edit: the arriving item list is what makes currency-ness knowable,
        // so this can rewrite a loaded value on the very first selection. Marking dirty for it would flag a
        // quest as modified merely by being opened.
        _refreshing = true;
        try { CoerceValue(); }
        finally { _refreshing = false; }
    }

    public QuestReward ToRecord() => new() { ItemNum = ItemNum, Quantity = Value };

    private static NamedEntry? EntryFor(NamedEntry[] entries, int id) =>
        id > 0 && id < entries.Length ? entries[id] : null;
}

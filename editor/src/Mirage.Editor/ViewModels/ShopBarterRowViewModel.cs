using CommunityToolkit.Mvvm.ComponentModel;
using Mirage.Editor.Localization;
using Mirage.Editor.Models;
using Mirage.Shared.Records;
namespace Mirage.Editor.ViewModels;

public sealed partial class ShopBarterRowViewModel : ObservableObject
{
    private readonly Func<NamedEntry[]> _entriesProvider;
    private readonly Func<int, bool> _isCurrency;

    public int SlotIndex { get; }

    public string GiveItemPlaceholder => EditorStrings.Get(EditorStrings.ShopEditor_GiveItemPlaceholder);
    public string GetItemPlaceholder => EditorStrings.Get(EditorStrings.ShopEditor_GetItemPlaceholder);

    [ObservableProperty] private int _giveItem;
    [ObservableProperty] private int _giveQuantity;
    [ObservableProperty] private int _getItem;
    [ObservableProperty] private int _getQuantity;

    public bool IsDirty { get; private set; }

    public NamedEntry[] ItemEntries => _entriesProvider();

    // Per-side quantity limits, bound to the NumericUpDown Min/Max so the spinner can't leave the valid
    // range: an empty side (no item) pins to 0; a non-currency item pins to exactly 1 (it never stacks);
    // a currency item allows 1..9999. CoerceGive/GetQuantity is the authoritative backstop for typed input
    // and item swaps; the server also re-normalizes on save.
    public int GiveQuantityMin => GiveItem > 0 ? 1 : 0;
    public int GiveQuantityMax => GiveItem <= 0 ? 0 : (_isCurrency(GiveItem) ? 9999 : 1);
    public int GetQuantityMin => GetItem > 0 ? 1 : 0;
    public int GetQuantityMax => GetItem <= 0 ? 0 : (_isCurrency(GetItem) ? 9999 : 1);

    public NamedEntry? SelectedGiveItem
    {
        get => EntryFor(ItemEntries, GiveItem);
        set
        {
            var id = value?.Id ?? 0;
            if (GiveItem == id) return;
            GiveItem = id;
            IsDirty = true;
            OnPropertyChanged(nameof(SelectedGiveItem));
        }
    }

    public NamedEntry? SelectedGetItem
    {
        get => EntryFor(ItemEntries, GetItem);
        set
        {
            var id = value?.Id ?? 0;
            if (GetItem == id) return;
            GetItem = id;
            IsDirty = true;
            OnPropertyChanged(nameof(SelectedGetItem));
        }
    }

    public ShopBarterRowViewModel(int slotIndex, BarterItemRecord r, Func<NamedEntry[]> entriesProvider, Func<int, bool> isCurrency)
    {
        SlotIndex = slotIndex;
        _entriesProvider = entriesProvider;
        _isCurrency = isCurrency;
        _giveItem = r.GiveItem;
        _giveQuantity = r.GiveQuantity;
        _getItem = r.GetItem;
        _getQuantity = r.GetQuantity;
    }

    partial void OnGiveItemChanged(int value)
    {
        IsDirty = true;
        OnPropertyChanged(nameof(SelectedGiveItem));
        // Refresh the spinner range BEFORE coercing the value, so setting the value can't get clamped to
        // the previous item's (now stale) maximum.
        OnPropertyChanged(nameof(GiveQuantityMin));
        OnPropertyChanged(nameof(GiveQuantityMax));
        CoerceGiveQuantity();
    }
    partial void OnGiveQuantityChanged(int value)
    {
        if (!_refreshing) IsDirty = true;
        CoerceGiveQuantity();
    }
    partial void OnGetItemChanged(int value)
    {
        IsDirty = true;
        OnPropertyChanged(nameof(SelectedGetItem));
        OnPropertyChanged(nameof(GetQuantityMin));
        OnPropertyChanged(nameof(GetQuantityMax));
        CoerceGetQuantity();
    }
    partial void OnGetQuantityChanged(int value)
    {
        if (!_refreshing) IsDirty = true;
        CoerceGetQuantity();
    }

    // Snap the stored quantity into the side's valid range (see GiveQuantityMin/Max). Setter-guarded so a
    // no-op coercion doesn't re-enter or spuriously re-notify.
    private void CoerceGiveQuantity()
    {
        int coerced = CoerceQuantity(GiveItem, GiveQuantity);
        if (GiveQuantity != coerced) GiveQuantity = coerced;
    }
    private void CoerceGetQuantity()
    {
        int coerced = CoerceQuantity(GetItem, GetQuantity);
        if (GetQuantity != coerced) GetQuantity = coerced;
    }
    private int CoerceQuantity(int itemId, int value)
    {
        if (itemId <= 0) return 0;              // no item on this side → unused slot
        if (!_isCurrency(itemId)) return 1;     // non-currency → exactly one item
        return value < 1 ? 1 : value;           // currency → at least one
    }

    /// <summary>An unused barter row — neither side carries an item. Dropped when the shop is saved.</summary>
    public bool IsEmpty => GiveItem <= 0 && GetItem <= 0;

    public void ClearDirty() => IsDirty = false;

    // Set while a REFRESH re-normalizes the row — see NpcDropRowViewModel for the failure this prevents.
    private bool _refreshing;

    public void NotifyEntriesChanged()
    {
        OnPropertyChanged(nameof(ItemEntries));
        OnPropertyChanged(nameof(SelectedGiveItem));
        OnPropertyChanged(nameof(SelectedGetItem));
        // An item's currency-ness may have changed under us; refresh the spinner limits and snap the stored
        // quantities back into range, so a currency→normal flip caps a >1 qty at 1 immediately (not on next
        // edit). This is NORMALIZATION, not an author edit — and it is not the no-op it first appears: the
        // arriving item list is what makes currency-ness knowable at all, so an authored value outside the
        // rule gets rewritten on the very FIRST selection. Marking dirty for it flags a shop as modified
        // merely by being opened.
        OnPropertyChanged(nameof(GiveQuantityMin));
        OnPropertyChanged(nameof(GiveQuantityMax));
        OnPropertyChanged(nameof(GetQuantityMin));
        OnPropertyChanged(nameof(GetQuantityMax));
        _refreshing = true;
        try
        {
            CoerceGiveQuantity();
            CoerceGetQuantity();
        }
        finally { _refreshing = false; }
    }

    public BarterItemRecord ToRecord() => new()
    {
        GiveItem = GiveItem,
        GiveQuantity = GiveQuantity,
        GetItem = GetItem,
        GetQuantity = GetQuantity,
    };

    private static NamedEntry? EntryFor(NamedEntry[] entries, int id) =>
        id > 0 && id < entries.Length ? entries[id] : null;
}

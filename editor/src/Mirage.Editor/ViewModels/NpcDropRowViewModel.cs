using CommunityToolkit.Mvvm.ComponentModel;
using Mirage.Editor.Localization;
using Mirage.Editor.Models;
using Mirage.Shared.Records;
namespace Mirage.Editor.ViewModels;

/// <summary>One line of an NPC's drop table: an item, how many, and how often. Mirrors
/// <see cref="QuestRewardRowViewModel"/> — item-picker facade, currency-aware quantity bounds, dirty
/// tracking — because it is the same authoring shape and should feel identical to use.
///
/// <para>The one thing it adds is <see cref="Chance"/>, and the one thing worth knowing while authoring
/// is that every line rolls INDEPENDENTLY: two 50% lines are not "one drop, evenly split", they are two
/// coins flipped, so a kill can yield both, either, or nothing. <see cref="ChanceText"/> spells that out
/// in the row so the sum is visible without arithmetic.</para></summary>
public sealed partial class NpcDropRowViewModel : ObservableObject
{
    private readonly Func<NamedEntry[]> _itemEntriesProvider;
    private readonly Func<int, bool> _isCurrency;

    public int SlotIndex { get; }

    public string ItemPlaceholder => EditorStrings.Get(EditorStrings.NpcEditor_DropItemPlaceholder);

    [ObservableProperty] private int _itemNum;
    [ObservableProperty] private int _value;
    [ObservableProperty] private int _chance;

    public bool IsDirty { get; private set; }

    public NamedEntry[] ItemEntries => _itemEntriesProvider();

    // Quantity rule, identical to a quest reward: no item → 0; a non-currency item → exactly 1 (one
    // drop of a sword is one sword); a currency item → 1..9999.
    public int ValueMin => ItemNum > 0 && _isCurrency(ItemNum) ? 1 : 0;
    public int ValueMax => ItemNum <= 0 ? 0 : (_isCurrency(ItemNum) ? 9999 : 0);

    /// <summary>Whether the quantity spinner means anything for this row — only currency stacks.</summary>
    public bool ValueApplies => ItemNum > 0 && _isCurrency(ItemNum);

    /// <summary>The chance as display text: "never" / "always" / "N%".</summary>
    public string ChanceText => Chance <= 0 ? EditorStrings.Get(EditorStrings.NpcEditor_DropChanceNever)
                             : Chance >= 100 ? EditorStrings.Get(EditorStrings.NpcEditor_DropChanceAlways)
                             : $"{Chance}%";

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

    public NpcDropRowViewModel(int slotIndex, NpcDrop d, Func<NamedEntry[]> itemEntriesProvider, Func<int, bool> isCurrency)
    {
        SlotIndex = slotIndex;
        _itemEntriesProvider = itemEntriesProvider;
        _isCurrency = isCurrency;
        _itemNum = d.ItemNum;
        _value = d.Quantity;
        _chance = d.Chance;
    }

    partial void OnItemNumChanged(int value)
    {
        IsDirty = true;
        OnPropertyChanged(nameof(SelectedItem));
        OnPropertyChanged(nameof(ValueMin));
        OnPropertyChanged(nameof(ValueMax));
        OnPropertyChanged(nameof(ValueApplies));
        CoerceValue();
    }
    partial void OnValueChanged(int value)
    {
        if (!_refreshing) IsDirty = true;
        CoerceValue();
    }
    partial void OnChanceChanged(int value)
    {
        IsDirty = true;
        OnPropertyChanged(nameof(ChanceText));
    }

    private void CoerceValue()
    {
        // Non-currency carries no quantity at all — the server normalizes it to 0 on save, so showing
        // anything else here would be a number the game throws away.
        int c = ItemNum > 0 && _isCurrency(ItemNum) ? (Value < 1 ? 1 : Value) : 0;
        if (Value != c) Value = c;
    }

    /// <summary>An unused row — no item, or a chance that can never land. Dropped when the NPC is saved
    /// (server-side <c>NpcRecord.Normalize</c> does the same, so the two agree).</summary>
    public bool IsEmpty => ItemNum <= 0 || Chance <= 0;

    public void ClearDirty() => IsDirty = false;

    // Set while a REFRESH re-normalizes the row, so the resulting write is not counted as an author edit.
    private bool _refreshing;

    public void NotifyEntriesChanged()
    {
        OnPropertyChanged(nameof(ItemEntries));
        OnPropertyChanged(nameof(SelectedItem));
        OnPropertyChanged(nameof(ValueMin));
        OnPropertyChanged(nameof(ValueMax));
        OnPropertyChanged(nameof(ValueApplies));
        // The coercion here is NORMALIZATION, not an edit — and it is NOT the no-op it looks like: the item
        // list arriving is what first makes currency-ness knowable, and authored data may legitimately carry
        // a quantity on a non-currency drop, so this write fires on the very first selection. Marking dirty
        // for it flagged every NPC with such a drop as modified merely by being opened.
        _refreshing = true;
        try { CoerceValue(); }
        finally { _refreshing = false; }
    }

    public NpcDrop ToRecord() => new() { ItemNum = ItemNum, Quantity = (short)Value, Chance = (short)Chance };

    private static NamedEntry? EntryFor(NamedEntry[] entries, int id) =>
        id > 0 && id < entries.Length ? entries[id] : null;
}

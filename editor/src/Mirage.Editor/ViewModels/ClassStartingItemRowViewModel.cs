using CommunityToolkit.Mvvm.ComponentModel;
using Mirage.Editor.Localization;
using Mirage.Editor.Models;
using Mirage.Shared;
using Mirage.Shared.Records;
namespace Mirage.Editor.ViewModels;

/// <summary>One item a class starts with. Same authoring shape as a quest reward or an NPC drop line —
/// item picker, currency-aware quantity — plus the one thing those do not have: a USABILITY check.
///
/// <para>Character creation SKIPS a starting line the class cannot use, so an unusable row here does not
/// produce a bad character, it produces a MISSING item and no explanation. That makes the warning the
/// whole point of this row rather than a nicety: it is the only place the mistake is visible.</para></summary>
public sealed partial class ClassStartingItemRowViewModel : ObservableObject
{
    // The gate facts, NOT a full record: served from the LIVE world when the editor is connected, so an
    // outcome here is what the SERVER will actually do rather than what the local data folder implies.
    public delegate (ItemType Type, int Power, short LevelReq, List<short>? AllowedClasses)? ItemGateLookup(int num);

    private readonly Func<NamedEntry[]> _itemEntriesProvider;
    private readonly ItemGateLookup _itemLookup;
    private readonly Func<(int Num, int Str, int Def)> _classInfo;

    public int SlotIndex { get; }

    public string ItemPlaceholder => EditorStrings.Get(EditorStrings.ClassEditor_StartItemPlaceholder);

    [ObservableProperty] private int _itemNum;
    [ObservableProperty] private int _value;

    public bool IsDirty { get; private set; }

    public NamedEntry[] ItemEntries => _itemEntriesProvider();

    private (ItemType Type, int Power, short LevelReq, List<short>? AllowedClasses)? Item => _itemLookup(ItemNum);
    private bool IsCurrency => Item?.Type == ItemType.Currency;

    // Quantity: currency stacks 1..9999; everything else is exactly one and the engine zeroes the field.
    public bool ValueApplies => IsCurrency;
    public int ValueMin => IsCurrency ? 1 : 0;
    public int ValueMax => IsCurrency ? 9999 : 0;

    /// <summary>What actually happens to this line at character creation: worn, carried, or skipped.
    /// Computed against the class's BASE stats, which is exactly what a new character has.</summary>
    public string OutcomeText
    {
        get
        {
            if (Item is not { } item) return string.Empty;
            var (classNum, str, def) = _classInfo();

            if (ItemRecord.UsesLevelReq(item.Type) && item.LevelReq > 1)
                return EditorStrings.Format(EditorStrings.ClassEditor_StartSkippedLevel, ("Level", item.LevelReq));

            if (!ItemRecord.IsEquipment(item.Type))
                return EditorStrings.Get(EditorStrings.ClassEditor_StartCarried);

            if (!ClassGate.Allows(item.AllowedClasses, classNum))
                return EditorStrings.Get(EditorStrings.ClassEditor_StartSkippedClass);

            int classStat = item.Type == ItemType.Weapon ? str : def;
            int need = CombatFormulas.GearStatRequirement(item.Power, classStat);
            if (need > classStat)
                return EditorStrings.Format(EditorStrings.ClassEditor_StartSkippedStat,
                    ("Need", need), ("Have", classStat));

            return EditorStrings.Get(EditorStrings.ClassEditor_StartWorn);
        }
    }

    /// <summary>True when this line will be silently dropped at creation — the state worth shouting about.</summary>
    public bool IsSkipped => OutcomeText.Length > 0
        && OutcomeText != EditorStrings.Get(EditorStrings.ClassEditor_StartWorn)
        && OutcomeText != EditorStrings.Get(EditorStrings.ClassEditor_StartCarried);

    public NamedEntry? SelectedItem
    {
        get => EntryFor(ItemEntries, ItemNum);
        set
        {
            var id = value?.Id ?? 0;
            if (ItemNum == id) return;
            ItemNum = id;
        }
    }

    public ClassStartingItemRowViewModel(int slotIndex, ClassStartingItem s, Func<NamedEntry[]> itemEntriesProvider,
        ItemGateLookup itemLookup, Func<(int, int, int)> classInfo)
    {
        SlotIndex = slotIndex;
        _itemEntriesProvider = itemEntriesProvider;
        _itemLookup = itemLookup;
        _classInfo = classInfo;
        _itemNum = s.ItemNum;
        _value = s.Quantity;
    }

    partial void OnItemNumChanged(int value)
    {
        IsDirty = true;
        OnPropertyChanged(nameof(SelectedItem));
        NotifyDerived();
        CoerceValue();
    }
    partial void OnValueChanged(int value)
    {
        IsDirty = true;
        CoerceValue();
    }

    private void CoerceValue()
    {
        int c = IsCurrency ? (Value < 1 ? 1 : Value) : 0;
        if (Value != c) Value = c;
    }

    /// <summary>An unused row — no item. Dropped on save, matching ClassRecord.Normalize.</summary>
    public bool IsEmpty => ItemNum <= 0;

    public void ClearDirty() => IsDirty = false;

    /// <summary>Re-raise everything derived from the item list OR the class's stats — editing STR or DEF
    /// changes what the class can wear, so the outcome column has to follow.</summary>
    public void NotifyDerived()
    {
        OnPropertyChanged(nameof(ItemEntries));
        OnPropertyChanged(nameof(SelectedItem));
        OnPropertyChanged(nameof(ValueApplies));
        OnPropertyChanged(nameof(ValueMin));
        OnPropertyChanged(nameof(ValueMax));
        OnPropertyChanged(nameof(OutcomeText));
        OnPropertyChanged(nameof(IsSkipped));
    }

    public ClassStartingItem ToRecord() => new() { ItemNum = ItemNum, Quantity = (short)Value };

    private static NamedEntry? EntryFor(NamedEntry[] entries, int id) =>
        id > 0 && id < entries.Length ? entries[id] : null;
}

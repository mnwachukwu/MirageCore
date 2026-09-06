using CommunityToolkit.Mvvm.ComponentModel;
using Mirage.Editor.Localization;
using Mirage.Editor.Models;
using Mirage.Shared;
using Mirage.Shared.Records;
namespace Mirage.Editor.ViewModels;

/// <summary>One spell a class starts already knowing.
///
/// <para>RESTRICTIVE BY CONSTRUCTION, unlike the item row beside it: the picker offers only spells this
/// class could actually learn at level 1, so there is no illegal state to warn about. Items need the
/// permissive-with-a-warning treatment because potions, currency and keys carry no class gate at all and
/// a designer may legitimately want any of them; a spell the class cannot cast is never legitimate.</para></summary>
public sealed partial class ClassStartingSpellRowViewModel : ObservableObject
{
    /// <summary>Gate facts, NOT a full record — served from the LIVE world when connected.</summary>
    public delegate (SpellType Type, short VitalAmount, short LevelReq, List<short>? AllowedClasses)? SpellGateLookup(int num);

    private readonly Func<NamedEntry[]> _learnableProvider;
    private readonly SpellGateLookup _spellLookup;

    public int SlotIndex { get; }

    public string SpellPlaceholder => EditorStrings.Get(EditorStrings.ClassEditor_StartSpellPlaceholder);

    [ObservableProperty] private int _spellNum;

    public bool IsDirty { get; private set; }

    /// <summary>Only what this class can learn at level 1 — the list is the validation.</summary>
    public NamedEntry[] SpellEntries => _learnableProvider();

    /// <summary>What the spell actually does, so a book can be judged without opening the spell editor:
    /// its type and magnitude. Both come off the record rather than the name, which is only a label.</summary>
    public string DetailText
    {
        get
        {
            if (_spellLookup(SpellNum) is not { } s) return string.Empty;
            return EditorStrings.Format(EditorStrings.ClassEditor_StartSpellDetail,
                ("Type", s.Type.ToString()), ("Amount", s.VitalAmount));
        }
    }

    public NamedEntry? SelectedSpell
    {
        get => SpellEntries.FirstOrDefault(e => e.Id == SpellNum);
        set
        {
            var id = value?.Id ?? 0;
            if (SpellNum == id) return;
            SpellNum = id;
        }
    }

    public ClassStartingSpellRowViewModel(int slotIndex, int spellNum, Func<NamedEntry[]> learnableProvider,
        SpellGateLookup spellLookup)
    {
        SlotIndex = slotIndex;
        _learnableProvider = learnableProvider;
        _spellLookup = spellLookup;
        _spellNum = spellNum;
    }

    partial void OnSpellNumChanged(int value)
    {
        IsDirty = true;
        OnPropertyChanged(nameof(SelectedSpell));
        OnPropertyChanged(nameof(DetailText));
    }

    public bool IsEmpty => SpellNum <= 0;

    public void ClearDirty() => IsDirty = false;

    /// <summary>Re-raise the picker — the learnable set depends on the class's INT, so editing INT can
    /// invalidate a row that was legal a moment ago.</summary>
    public void NotifyDerived()
    {
        OnPropertyChanged(nameof(SpellEntries));
        OnPropertyChanged(nameof(SelectedSpell));
        OnPropertyChanged(nameof(DetailText));
    }
}

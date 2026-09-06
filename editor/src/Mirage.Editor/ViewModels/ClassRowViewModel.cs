using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Editor.Models;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using System.Collections.ObjectModel;

namespace Mirage.Editor.ViewModels;

/// <summary>
/// One class slot in the class editor's list — the editable mirror of a <see cref="ClassRecord"/>.
/// <para>Alongside the usual row duties it previews what a freshly-created character of this class
/// would have at level 1, so a designer can see the consequences of a stat spread while typing it.
/// Editing a stat re-raises only the previews that stat actually feeds.</para>
/// </summary>
public sealed partial class ClassRowViewModel : ObservableObject, ILockableRow
{
    /// <inheritdoc/>
    [ObservableProperty] private bool _lockedByOther;
    /// <inheritdoc/>
    [ObservableProperty] private string _lockHolder = "";

    /// <summary>1-based class slot number.</summary>
    public int Index { get; }
    /// <summary>Whether the full definition has been fetched; false for a placeholder row awaiting load.</summary>
    public bool IsLoaded { get; private set; }

    [ObservableProperty] private string _name = "";
    /// <summary>The short pitch the character-create screen shows under the class list — what this class
    /// is FOR, in the player's terms. Empty is fine; the screen simply shows nothing.</summary>
    [ObservableProperty] private string _description = "";
    /// <summary>Sprite a character of this class starts with, one per sex. Fixed onto the character at
    /// creation, so re-arting a class here changes who is CREATED next, never who already exists.</summary>
    [ObservableProperty] private int _spriteMale;
    [ObservableProperty] private int _spriteFemale;
    /// <summary>Which sprite sheet each of the above is a row of.</summary>
    [ObservableProperty] private int _spriteSheetMale;
    [ObservableProperty] private int _spriteSheetFemale;
    [ObservableProperty] private int _str;
    [ObservableProperty] private int _def;
    [ObservableProperty] private int _spd;
    [ObservableProperty] private int _int;

    /// <summary>Whether the row holds edits not yet saved.</summary>
    public bool IsDirty { get; private set; }

    // ── Starting loadout ──────────────────────────────────────────────────────
    /// <summary>What a new character of this class is created holding. Equipment that passes its gates
    /// arrives WORN; anything the class cannot use is SKIPPED at creation, which is why each row carries
    /// an outcome column rather than trusting the author to have got it right.</summary>
    public ObservableCollection<ClassStartingItemRowViewModel> StartingItems { get; } = [];
    /// <summary>Spells a new character already knows. The picker offers only what this class can learn at
    /// level 1, so unlike the item table there is no illegal state to warn about.</summary>
    public ObservableCollection<ClassStartingSpellRowViewModel> StartingSpells { get; } = [];

    // Supplied by the editor VM (which owns the live item and spell tables). Defaulted so the many plain
    // `new ClassRowViewModel(i, record)` sites — and the tests — keep working without a picker.
    private Func<NamedEntry[]> _itemEntries = static () => [];
    private ClassStartingItemRowViewModel.ItemGateLookup _itemLookup = static _ => null;
    private ClassStartingSpellRowViewModel.SpellGateLookup _spellLookup = static _ => null;
    private Func<ClassRowViewModel, NamedEntry[]> _learnableSpells = static _ => [];

    /// <summary>Wire the pickers after construction. Rebuilds existing rows so any already loaded pick
    /// them up.</summary>
    public void AttachProviders(Func<NamedEntry[]> itemEntries, ClassStartingItemRowViewModel.ItemGateLookup itemLookup,
        ClassStartingSpellRowViewModel.SpellGateLookup spellLookup, Func<ClassRowViewModel, NamedEntry[]> learnableSpells)
    {
        _itemEntries = itemEntries;
        _itemLookup = itemLookup;
        _spellLookup = spellLookup;
        _learnableSpells = learnableSpells;
        NotifyLoadoutDerived();
    }

    /// <summary>Re-raise every loadout-derived binding. Called when the item/spell tables change AND when
    /// a STAT changes — editing STR, DEF or INT moves what this class can wear and learn, so a row that
    /// was legal a moment ago may not be.</summary>
    public void NotifyLoadoutDerived()
    {
        foreach (var r in StartingItems) r.NotifyDerived();
        foreach (var r in StartingSpells) r.NotifyDerived();
        OnPropertyChanged(nameof(LoadoutSummary));
    }

    /// <summary>How the loadout will actually land — worn, carried, and the count that will be silently
    /// dropped, which is the number worth seeing without reading every row.</summary>
    public string LoadoutSummary
    {
        get
        {
            int skipped = StartingItems.Count(r => !r.IsEmpty && r.IsSkipped);
            int kept = StartingItems.Count(r => !r.IsEmpty) - skipped;
            return EditorStrings.Format(EditorStrings.ClassEditor_LoadoutSummary,
                ("Kept", kept), ("Spells", StartingSpells.Count(r => !r.IsEmpty)), ("Skipped", skipped));
        }
    }

    public bool HasSkippedStartingItems => StartingItems.Any(r => !r.IsEmpty && r.IsSkipped);

    private void LoadStartingItems(List<ClassStartingItem>? items)
    {
        foreach (var r in StartingItems) r.PropertyChanged -= OnLoadoutRowChanged;
        StartingItems.Clear();
        foreach (var s in items ?? [])
            AddStartingItemRow(s);
        NotifyLoadoutDerived();
    }

    private void LoadStartingSpells(List<int>? spells)
    {
        foreach (var r in StartingSpells) r.PropertyChanged -= OnLoadoutRowChanged;
        StartingSpells.Clear();
        foreach (int n in spells ?? [])
            AddStartingSpellRow(n);
        NotifyLoadoutDerived();
    }

    private void AddStartingItemRow(ClassStartingItem s)
    {
        var row = new ClassStartingItemRowViewModel(StartingItems.Count + 1, s,
            () => _itemEntries(), n => _itemLookup(n), () => (Index, Str, Def));
        row.PropertyChanged += OnLoadoutRowChanged;
        StartingItems.Add(row);
    }

    private void AddStartingSpellRow(int spellNum)
    {
        var row = new ClassStartingSpellRowViewModel(StartingSpells.Count + 1, spellNum,
            () => _learnableSpells(this), n => _spellLookup(n));
        row.PropertyChanged += OnLoadoutRowChanged;
        StartingSpells.Add(row);
    }

    private void OnLoadoutRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Only a row reporting its OWN edit marks the class dirty.
        //
        // A child row re-raises derived state constantly — NotifyDerived alone raises seven properties,
        // and it runs whenever the loadout previews are refreshed, which includes simply selecting the
        // class. Treating every one of those as an edit meant OPENING a class marked it modified, which
        // is how the unsaved-changes dot became noise. The rows already track a correct IsDirty (set only
        // in their ItemNum/Value setters); this just asks them instead of assuming.
        bool realEdit = sender switch
        {
            ClassStartingItemRowViewModel item => item.IsDirty,
            ClassStartingSpellRowViewModel spell => spell.IsDirty,
            _ => true,
        };
        if (realEdit) MarkDirty();
        OnPropertyChanged(nameof(LoadoutSummary));
        OnPropertyChanged(nameof(HasSkippedStartingItems));
    }

    [RelayCommand]
    private void AddStartingItem()
    {
        if (StartingItems.Count >= Constants.MaxInv) return;
        AddStartingItemRow(new ClassStartingItem());
        MarkDirty();
        NotifyLoadoutDerived();
    }

    [RelayCommand]
    private void RemoveStartingItem(ClassStartingItemRowViewModel row)
    {
        row.PropertyChanged -= OnLoadoutRowChanged;
        if (StartingItems.Remove(row)) MarkDirty();
        NotifyLoadoutDerived();
    }

    [RelayCommand]
    private void AddStartingSpell()
    {
        if (StartingSpells.Count >= Constants.MaxPlayerSpells) return;
        AddStartingSpellRow(0);
        MarkDirty();
        NotifyLoadoutDerived();
    }

    [RelayCommand]
    private void RemoveStartingSpell(ClassStartingSpellRowViewModel row)
    {
        row.PropertyChanged -= OnLoadoutRowChanged;
        if (StartingSpells.Remove(row)) MarkDirty();
        NotifyLoadoutDerived();
    }

    /// <summary>The authored loadout, empty rows stripped — the same shape ClassRecord.Normalize keeps,
    /// so an offline save and an online save produce the same file.</summary>
    public List<ClassStartingItem>? StartingItemsToRecord() =>
        StartingItems.Any(r => !r.IsEmpty) ? [.. StartingItems.Where(r => !r.IsEmpty).Select(r => r.ToRecord())] : null;
    public List<int>? StartingSpellsToRecord() =>
        StartingSpells.Any(r => !r.IsEmpty) ? [.. StartingSpells.Where(r => !r.IsEmpty).Select(r => r.SpellNum)] : null;

    /// <summary>List caption: "index: name", with a placeholder when the slot is unnamed.</summary>
    public string DisplayName => $"{Index}: {(string.IsNullOrEmpty(Name) ? EditorStrings.Get(EditorStrings.Common_EmptyName) : Name)}";

    // ── Theoretical fresh Lv.1 character of this class ──────────────────────
    // At character creation each player stat is copied from the class stat (one-time copy in
    // CreateCharacter), so a fresh L=1 character has playerStat = classStat for every stat.
    // The previews below evaluate every formula with that assumption — what a player gets the
    // instant they finish character creation.  All formulas route through the same shared
    // classes the live game uses, so the editor stays in lockstep with combat.

    public int LevelOneMaxHp => StatFormulas.GetPlayerMaxHp(1, Def, Def);
    public int LevelOneMaxMp => StatFormulas.GetPlayerMaxMp(1, Int, Int);
    public int LevelOneMaxSp => StatFormulas.GetPlayerMaxSp(1, Spd, Spd);

    public int LevelOneHpRegen => StatFormulas.GetPlayerHpRegen(Def);
    public int LevelOneMpRegen => StatFormulas.GetPlayerMpRegen(Int);
    public int LevelOneSpRegen => StatFormulas.GetPlayerSpRegen(Spd);

    // Base damage and mitigation with NO equipment — what the class deals/absorbs bare-handed.
    public int LevelOnePhysDamage => CombatFormulas.UnarmedDamage(Str);
    public int LevelOneMagicDamage => CombatFormulas.SpellPower(Int);
    public int LevelOneMit => CombatFormulas.PlayerProtection(1, Def);   // one universal MIT at level 1 (level baseline + DEF bonus; defends P-DMG and M-DMG alike)

    // Set while filling from a record or packet, so those writes don't count as author edits.
    private bool _loading;

    public ClassRowViewModel(int index, ClassRecord r, bool isLoaded = true)
    {
        Index = index;
        IsLoaded = isLoaded;
        // Backing fields, not properties: an assignment through the property would fire OnXChanged and
        // mark a freshly-loaded row dirty before anyone has touched it.
        _name = r.Name;
        _description = r.Description;
        _spriteMale = r.SpriteMale;
        _spriteFemale = r.SpriteFemale;
        _spriteSheetMale = r.SpriteSheetMale;
        _spriteSheetFemale = r.SpriteSheetFemale;
        _str = r.Str;
        _def = r.Def;
        _spd = r.Spd;
        _int = r.Int;

        // The loadout has no backing-field shortcut — building it subscribes OnLoadoutRowChanged to each
        // child row, and a child re-raising anything derived (NotifyDerived alone raises seven) lands on
        // MarkDirty. So it is loaded under the same guard LoadFromRecord uses, and the row ends CLEAN.
        //
        // Without this every class with a starting loadout showed the unsaved-changes dot the moment the
        // offline list was built, which is worse than cosmetic: it makes the dot meaningless, so a real
        // unsaved edit is indistinguishable from ten rows that were merely read off disk.
        _loading = true;
        try
        {
            LoadStartingItems(r.StartingItems);
            LoadStartingSpells(r.StartingSpells);
        }
        finally
        {
            _loading = false;
        }
        ClearDirty();
    }

    partial void OnNameChanged(string value) => MarkDirty();
    partial void OnDescriptionChanged(string value) => MarkDirty();
    partial void OnSpriteMaleChanged(int value) => MarkDirty();
    partial void OnSpriteFemaleChanged(int value) => MarkDirty();
    partial void OnSpriteSheetMaleChanged(int value) => MarkDirty();
    partial void OnSpriteSheetFemaleChanged(int value) => MarkDirty();
    // Each stat re-raises only the previews it feeds: STR is offense-only, DEF drives HP + regen +
    // mitigation, SPD the stamina pool, INT the mana pool and spell power.
    // STR and DEF also gate EQUIPMENT, and INT gates spells, so each of the three re-raises the loadout:
    // dropping a class's DEF can turn a worn piece into a skipped one, and the outcome column has to say
    // so while the designer is still looking at it.
    partial void OnStrChanged(int value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(LevelOnePhysDamage));
        NotifyLoadoutDerived();
    }
    partial void OnDefChanged(int value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(LevelOneMaxHp));
        OnPropertyChanged(nameof(LevelOneHpRegen));
        OnPropertyChanged(nameof(LevelOneMit));
        NotifyLoadoutDerived();
    }
    partial void OnSpdChanged(int value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(LevelOneMaxSp));
        OnPropertyChanged(nameof(LevelOneSpRegen));
    }
    partial void OnIntChanged(int value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(LevelOneMaxMp));
        OnPropertyChanged(nameof(LevelOneMpRegen));
        OnPropertyChanged(nameof(LevelOneMagicDamage));
        NotifyLoadoutDerived();
    }

    private void MarkDirty()
    {
        if (_loading) return;
        IsDirty = true;
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(IsDirty));
    }

    /// <summary>Mark the row clean after a successful save or discard. Clears the CHILD rows too — a row
    /// left dirty would re-mark the class on its next derived re-raise, so the dot would come straight
    /// back without anyone touching anything.</summary>
    public void ClearDirty()
    {
        foreach (var item in StartingItems) item.ClearDirty();
        foreach (var spell in StartingSpells) spell.ClearDirty();
        IsDirty = false;
        OnPropertyChanged(nameof(IsDirty));
    }

    /// <summary>Refill from an on-disk record (offline load or discard). Leaves the row clean.</summary>
    /// <summary>Fill from a record and leave the row DIRTY and loaded — the copy path, where the new
    /// record exists only in memory until a save persists it.
    /// <para>Marking it LOADED matters online: an unloaded row lazy-fetches when selected, and that fetch
    /// would land after the copy and overwrite it with the empty slot the server still holds.</para></summary>
    public void CopyFromRecord(ClassRecord r)
    {
        LoadFromRecord(r);
        IsLoaded = true;
        MarkDirty();
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(DisplayName));
    }

    public void LoadFromRecord(ClassRecord r)
    {
        _loading = true;
        try
        {
            Name = r.Name;
            Description = r.Description;
            SpriteMale = r.SpriteMale;
            SpriteFemale = r.SpriteFemale;
            SpriteSheetMale = r.SpriteSheetMale;
            SpriteSheetFemale = r.SpriteSheetFemale;
            Str = r.Str;
            Def = r.Def;
            Spd = r.Spd;
            Int = r.Int;
            LoadStartingItems(r.StartingItems);
            LoadStartingSpells(r.StartingSpells);
        }
        finally
        {
            _loading = false;
        }
        ClearDirty();
        OnPropertyChanged(nameof(DisplayName));
    }

    /// <summary>Refill from a server response and mark the row loaded; does not clear the dirty flag.</summary>
    public void ApplyPacket(UpdateClassPacket pkt)
    {
        _loading = true;
        try
        {
            Name = pkt.Name;
            Description = pkt.Description;
            SpriteMale = pkt.SpriteMale;
            SpriteFemale = pkt.SpriteFemale;
            SpriteSheetMale = pkt.SpriteSheetMale;
            SpriteSheetFemale = pkt.SpriteSheetFemale;
            Str = pkt.Str;
            Def = pkt.Def;
            Spd = pkt.Spd;
            Int = pkt.Int;
            LoadStartingItems(pkt.StartingItems);
            LoadStartingSpells(pkt.StartingSpells);
        }
        finally
        {
            _loading = false;
        }
        IsLoaded = true;
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(DisplayName));
    }

    /// <summary>Project the row back into a record for saving.</summary>
    public ClassRecord ToRecord() => new()
    {
        Name = Name,
        Description = Description,
        SpriteMale = SpriteMale,
        SpriteFemale = SpriteFemale,
        SpriteSheetMale = SpriteSheetMale,
        SpriteSheetFemale = SpriteSheetFemale,
        Str = Str,
        Def = Def,
        Spd = Spd,
        Int = this.Int,
        StartingItems = StartingItemsToRecord(),
        StartingSpells = StartingSpellsToRecord(),
    };

    /// <summary>Project the row into the online save packet. The single source of that mapping — both the
    /// editor's own save and the push-changes prompt route through here, so neither can drift from the other.</summary>
    public EditorSaveClassPacket BuildSavePacket() => new()
    {
        ClassNum = Index,
        Name = Name,
        Description = Description,
        SpriteMale = SpriteMale,
        SpriteFemale = SpriteFemale,
        SpriteSheetMale = SpriteSheetMale,
        SpriteSheetFemale = SpriteSheetFemale,
        Str = Str,
        Def = Def,
        Spd = Spd,
        Int = this.Int,
        StartingItems = StartingItemsToRecord(),
        StartingSpells = StartingSpellsToRecord(),
    };
}

using CommunityToolkit.Mvvm.ComponentModel;
using Mirage.Editor.Localization;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Editor.ViewModels;

/// <summary>
/// One spell slot in the spell editor's list — the editable mirror of a <see cref="SpellRecord"/>.
/// <para>Dirty tracking works as in the other row view-models: setters mark dirty unless
/// <c>_loading</c> is set while filling from a record or packet.</para>
/// <para>Also drives the editor's live cost preview, so editing <see cref="VitalAmount"/>,
/// <see cref="IntReq"/> or the type re-raises <see cref="BaseMpCost"/> and friends. For most types MP
/// cost is a pure function of the spell's own metadata, making the preview the exact in-game cost; SubHp
/// and AddMp are caster-dependent and are quoted against a stand-in instead — see
/// <see cref="BaseMpCost"/>.</para>
/// </summary>
public sealed partial class SpellRowViewModel : ObservableObject, ILockableRow
{
    /// <inheritdoc/>
    [ObservableProperty] private bool _lockedByOther;
    /// <inheritdoc/>
    [ObservableProperty] private string _lockHolder = "";

    /// <summary>1-based spell slot number.</summary>
    public int Index { get; }
    /// <summary>Whether the full definition has been fetched; false for a placeholder row awaiting load.</summary>
    public bool IsLoaded { get; private set; }

    [ObservableProperty] private string _name = "";
    /// <summary>Classes allowed to learn it; null or empty = every class. Replaced wholesale by the
    /// class multi-select rather than mutated, so the change notification actually fires.</summary>
    [ObservableProperty] private List<short>? _allowedClasses;
    [ObservableProperty] private SpellType _type;

    // Type-specific fields — GiveItem uses the bottom three and no VitalAmount; every other type is
    // the other way round. See SpellRecord.
    /// <summary>The spell's magnitude, which also gates learning it. Unused by GiveItem.</summary>
    [ObservableProperty] private short _vitalAmount;
    /// <summary>GiveItem: the item handed over; unused by every other spell type.</summary>
    [ObservableProperty] private short _itemNum;
    /// <summary>GiveItem: how many; unused by every other spell type.</summary>
    [ObservableProperty] private short _itemQuantity;
    /// <summary>GiveItem: its INT requirement, and hence its MP cost; unused by every other type.</summary>
    [ObservableProperty] private short _intReq;
    /// <summary>Minimum character level to learn it; 0 = no level gate. Applies to every spell type,
    /// unlike the fields above. INT decides who may learn a spell, this decides when — and it is the only
    /// one of the two that can pace a ladder, since a specialist starts with enough INT to meet a
    /// mid-ladder spell at level 1. Enforced on learn AND on every cast.</summary>
    [ObservableProperty] private short _levelReq;

    /// <summary>Whether the row holds edits not yet saved.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>List caption: "index: name", with a placeholder when the slot is unnamed.</summary>
    public string DisplayName => $"{Index}: {(string.IsNullOrEmpty(Name) ? EditorStrings.Get(EditorStrings.Common_EmptyName) : Name)}";

    // Set while filling from a record or packet, so those writes don't count as author edits.
    private bool _loading;

    public SpellRowViewModel(int index, SpellRecord r, bool isLoaded = true)
    {
        Index = index;
        IsLoaded = isLoaded;
        _name = r.Name;
        _allowedClasses = r.AllowedClasses is null ? null : new List<short>(r.AllowedClasses);
        _type = r.Type;
        _vitalAmount = r.VitalAmount;
        _itemNum = r.ItemNum;
        _itemQuantity = r.ItemQuantity;
        _intReq = r.IntReq;
        _levelReq = r.LevelReq;
    }

    partial void OnNameChanged(string value) => MarkDirty();
    partial void OnAllowedClassesChanged(List<short>? value) => MarkDirty();

    // VitalAmount and IntReq are the two gate values, so both re-raise the cost preview; ItemNum and
    // ItemQuantity say what GiveItem hands over and have no bearing on cost.
    partial void OnVitalAmountChanged(short value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(BaseMpCost));
        OnPropertyChanged(nameof(MpCostDisplay));
        OnPropertyChanged(nameof(ReagentCost));
    }
    partial void OnItemNumChanged(short value) => MarkDirty();
    partial void OnItemQuantityChanged(short value) => MarkDirty();
    partial void OnLevelReqChanged(short value) => MarkDirty();
    partial void OnIntReqChanged(short value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(BaseMpCost));
        OnPropertyChanged(nameof(MpCostDisplay));
    }

    // The type decides the one varying caption, which fields show, AND which cost model applies, so
    // the whole derived set re-raises together.
    partial void OnTypeChanged(SpellType value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(VitalAmountLabel));
        OnPropertyChanged(nameof(VitalAmountVisible));
        OnPropertyChanged(nameof(IsGiveItem));
        OnPropertyChanged(nameof(BaseMpCost));
        OnPropertyChanged(nameof(MpCostDisplay));
        OnPropertyChanged(nameof(ShowReagentCost));
        OnPropertyChanged(nameof(ReagentCost));
    }

    private void MarkDirty()
    {
        if (_loading) return;
        IsDirty = true;
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(IsDirty));
    }

    /// <summary>Mark the row clean after a successful save or discard.</summary>
    public void ClearDirty()
    {
        IsDirty = false;
        OnPropertyChanged(nameof(IsDirty));
    }

    /// <summary>Refill from an on-disk record (offline load or discard). Leaves the row clean.</summary>
    /// <summary>Fill from a record and leave the row DIRTY and loaded — the copy path, where the new
    /// record exists only in memory until a save persists it.
    /// <para>Marking it LOADED matters online: an unloaded row lazy-fetches when selected, and that fetch
    /// would land after the copy and overwrite it with the empty slot the server still holds.</para></summary>
    public void CopyFromRecord(SpellRecord r)
    {
        LoadFromRecord(r);
        IsLoaded = true;
        MarkDirty();
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(DisplayName));
    }

    public void LoadFromRecord(SpellRecord r)
    {
        _loading = true;
        try
        {
            Name = r.Name;
            AllowedClasses = r.AllowedClasses is null ? null : new List<short>(r.AllowedClasses);
            Type = r.Type;
            VitalAmount = r.VitalAmount;
            ItemNum = r.ItemNum;
            ItemQuantity = r.ItemQuantity;
            IntReq = r.IntReq;
            LevelReq = r.LevelReq;
        }
        finally
        {
            _loading = false;
        }
        ClearDirty();
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(BaseMpCost));
    }

    /// <summary>Refill from a server response and mark the row loaded; does not clear the dirty flag.</summary>
    public void ApplyPacket(UpdateSpellPacket pkt)
    {
        _loading = true;
        try
        {
            Name = pkt.Name;
            AllowedClasses = pkt.AllowedClasses is null ? null : new List<short>(pkt.AllowedClasses);
            Type = pkt.Type;
            VitalAmount = pkt.VitalAmount;
            ItemNum = pkt.ItemNum;
            ItemQuantity = pkt.ItemQuantity;
            IntReq = pkt.IntReq;
            LevelReq = pkt.LevelReq;
        }
        finally
        {
            _loading = false;
        }
        IsLoaded = true;
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(BaseMpCost));
    }

    /// <summary>Project the row back into a record for saving. <see cref="SpellRecord.Normalize"/>d, so a
    /// field the current type does not use is written as 0 rather than carrying a previous type's value —
    /// which matters here because a stale IntReq would silently re-gate the spell. The row itself is left
    /// alone, so retyping and back within one session keeps the numbers.</summary>
    public SpellRecord ToRecord()
    {
        var r = new SpellRecord
        {
            Name = Name,
            AllowedClasses = AllowedClasses is null ? null : new List<short>(AllowedClasses),
            Type = Type,
            VitalAmount = VitalAmount,
            ItemNum = ItemNum,
            ItemQuantity = ItemQuantity,
            IntReq = IntReq,
            LevelReq = LevelReq,
        };
        r.Normalize();
        return r;
    }

    /// <summary>Project the row into the online save packet. The single source of that mapping — both the
    /// editor's own save and the push-changes prompt route through here, so neither can drift from the other.
    /// Normalized through <see cref="ToRecord"/> so the online and offline saves store the same thing.</summary>
    public EditorSaveSpellPacket BuildSavePacket()
    {
        var r = ToRecord();
        return new EditorSaveSpellPacket
        {
            SpellNum = Index,
            Name = r.Name,
            AllowedClasses = r.AllowedClasses,
            Type = r.Type,
            VitalAmount = r.VitalAmount,
            ItemNum = r.ItemNum,
            ItemQuantity = r.ItemQuantity,
            IntReq = r.IntReq,
            LevelReq = r.LevelReq,
        };
    }

    // ── Captions and visibility ───────────────────────────────────────────────
    // The split is total: GiveItem shows the item picker, quantity and INT requirement; every other type
    // shows VitalAmount alone. Both flags defer to SpellRecord — the same rules Normalize clears by, so
    // a hidden field is exactly a zeroed one.

    /// <summary>Whether this is a GiveItem spell, which shows the item picker, quantity and INT
    /// requirement in place of the magnitude field.</summary>
    public bool IsGiveItem => SpellRecord.UsesItemFields(Type);

    /// <summary>Whether the magnitude field applies (everything except GiveItem).</summary>
    public bool VitalAmountVisible => SpellRecord.UsesVitalAmount(Type);

    /// <summary>Form caption for <see cref="VitalAmount"/> — which vital it moves, and in which
    /// direction, depends on the type.</summary>
    public string VitalAmountLabel => Type switch
    {
        SpellType.AddHp => EditorStrings.Get(EditorStrings.DataLabel_HpAmount),
        SpellType.AddMp => EditorStrings.Get(EditorStrings.DataLabel_MpAmount),
        SpellType.AddSp => EditorStrings.Get(EditorStrings.DataLabel_SpAmount),
        SpellType.SubHp => EditorStrings.Get(EditorStrings.DataLabel_Damage),
        SpellType.SubMp => EditorStrings.Get(EditorStrings.DataLabel_MpDrain),
        SpellType.SubSp => EditorStrings.Get(EditorStrings.DataLabel_SpDrain),
        _ => EditorStrings.Get(EditorStrings.DataLabel_VitalAmount),
    };

    // MP cost preview.  Most utility spells (AddHp, AddSp, the drains, GiveItem) pay a pure function of spell
    // metadata — no caster Int enters, so this preview is the exact in-game cost.  Two exceptions, both
    // caster-dependent and so quoted against a stand-in here:
    //   SubHp   — the caster's sustainable "weapon", MP is a trivial pool fraction (MaxMP / 20) and the real
    //             per-cast cost is the REAGENT below, so its MP line shows the formula rather than a number.
    //   AddMp   — priced off what it restores, to stop a self-cast printing mana.  With no caster to hand,
    //             quote it at Int == VitalAmount: the spell's own raw gate, hence the lowest Int that can
    //             learn it before any class head-start.  That makes the preview the cost for the weakest
    //             legal caster, and every stronger one pays MORE (both terms of the restore rise with Int),
    //             so an author reading this number is reading a floor, not a typical case.
    /// <summary>The spell's MP cost as the game will charge it — for AddMp, quoted at the reference Int
    /// described above rather than for any particular caster.</summary>
    public int BaseMpCost => CombatFormulas.GetSpellMpCost(ToRecord(), VitalAmount);
    /// <summary>MP cost as shown in the form — a bare number for the types that charge one, the
    /// pool-fraction formula for SubHp, and for AddMp the number tagged with the Int it is quoted at, so
    /// nobody reads a caster-dependent figure as fixed.</summary>
    public string MpCostDisplay => Type switch
    {
        SpellType.SubHp => EditorStrings.Get(EditorStrings.SpellEditor_SubHpMpCostValue),
        SpellType.AddMp => EditorStrings.Format(EditorStrings.SpellEditor_AddMpCostValue,
            ("Cost", BaseMpCost), ("Int", VitalAmount)),
        _ => BaseMpCost.ToString(),
    };

    // Reagent-per-cast preview (SubHp only) — the magic mirror of weapon-repair upkeep:
    // round(VitalAmount/10 × ~0.48 durability-lost-per-swing).  Shown instead of a fixed MP number since
    // that's the cost an author actually tunes.
    /// <summary>Whether to show the reagent-cost row (SubHp only).</summary>
    public bool ShowReagentCost => Type == SpellType.SubHp;
    /// <summary>Reagents one cast takes when it takes any — a flat count. Empty for a type that consumes
    /// none.</summary>
    public string ReagentCost =>
        Type == SpellType.SubHp
            ? CombatFormulas.ReagentCostPerCast(CombatFormulas.SubHpReagentCostExact(LevelReq)).ToString()
            : "";

    /// <summary>How often a cast takes its reagents at all, as a percent. Empty at 100%, where every cast
    /// pays and there are no odds to state.</summary>
    public string ReagentChance
    {
        get
        {
            if (Type != SpellType.SubHp) return "";
            double chance = CombatFormulas.ReagentDepleteChancePercent(CombatFormulas.SubHpReagentCostExact(LevelReq));
            return chance is > 0 and < 100
                ? EditorStrings.Format(EditorStrings.SpellEditor_ReagentChancePercent, ("Percent", chance.ToString("0.#")))
                : "";
        }
    }
}

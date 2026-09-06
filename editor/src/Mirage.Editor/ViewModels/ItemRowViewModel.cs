using CommunityToolkit.Mvvm.ComponentModel;
using Mirage.Editor.Localization;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Editor.ViewModels;

/// <summary>
/// One item slot in the item editor's list — the editable mirror of an <see cref="ItemRecord"/>.
/// <para>Tracks its own dirty flag: every setter routes through <see cref="MarkDirty"/>, which is
/// suppressed while <c>_loading</c> is set so filling the row from a record or packet doesn't mark
/// it as an author edit.</para>
/// <para>Each type-specific field carries its own caption and its own <c>…Visible</c> flag, both
/// derived from <see cref="Type"/> via the rules on <see cref="ItemRecord"/> — so the form shows a
/// weapon its durability, power and class requirement, and a potion nothing but its amount.</para>
/// </summary>
public sealed partial class ItemRowViewModel : ObservableObject, ILockableRow
{
    /// <inheritdoc/>
    [ObservableProperty] private bool _lockedByOther;
    /// <inheritdoc/>
    [ObservableProperty] private string _lockHolder = "";

    /// <summary>1-based item slot number.</summary>
    public int Index { get; }
    /// <summary>Whether the full definition has been fetched; false for a placeholder row awaiting load.</summary>
    public bool IsLoaded { get; private set; }

    [ObservableProperty] private string _name = "";
    /// <summary>Index into the item graphics strip.</summary>
    [ObservableProperty] private short _pic;
    /// <summary>Which item sheet <see cref="Pic"/> is a row of.</summary>
    [ObservableProperty] private short _itemSheet;
    [ObservableProperty] private ItemType _type;

    // Type-specific fields — see ItemRecord for which apply to which type.
    [ObservableProperty] private short _durability;
    [ObservableProperty] private short _vitalAmount;
    [ObservableProperty] private short _spellNum;
    [ObservableProperty] private short _power;
    /// <summary>Minimum character level to equip or use it; 0 = no level gate. This is what paces the tier
    /// ladder: the stat requirement derived from <see cref="Power"/> gates WHO may wear a piece, and a
    /// class's base stat is high enough at level 1 to meet a mid-ladder item on the day it is rolled — so
    /// only a level gates WHEN.</summary>
    [ObservableProperty] private short _levelReq;
    /// <summary>Classes allowed to equip it; null or empty = every class. Replaced wholesale by the
    /// class multi-select rather than mutated, so the change notification actually fires.</summary>
    [ObservableProperty] private List<short>? _allowedClasses;

    // Item restriction flags; each blocks exactly one action, enforced server-side.
    [ObservableProperty] private bool _nonTradeable;
    [ObservableProperty] private bool _nonListable;
    [ObservableProperty] private bool _nonMailable;
    [ObservableProperty] private bool _destroyOnDrop;
    /// <summary>Blocks the generic shop sell path, which is a junk dump rather than a market. Set it on
    /// currency (dumping gold for a fraction of itself is nonsense) and on treasure, whose worth is the
    /// whole point of visiting a fence — left junkable it would just be dumped at the generic rate.</summary>
    [ObservableProperty] private bool _nonJunkable;

    /// <summary>Gold worth. Seeded from the economy formula for the whole armory, so authoring one by hand
    /// is an OVERRIDE — which is exactly what treasure needs and what nothing else should want.</summary>
    [ObservableProperty] private int _price;

    /// <summary>Whether the row holds edits not yet saved.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>List caption: "index: name", with a placeholder when the slot is unnamed.</summary>
    public string DisplayName => $"{Index}: {(string.IsNullOrEmpty(Name) ? EditorStrings.Get(EditorStrings.Common_EmptyName) : Name)}";

    // Set while filling from a record or packet, so those writes don't count as author edits.
    private bool _loading;

    public ItemRowViewModel(int index, ItemRecord r, bool isLoaded = true)
    {
        Index = index;
        IsLoaded = isLoaded;
        _name = r.Name;
        _pic = r.Pic;
        _itemSheet = r.ItemSheet;
        _type = r.Type;
        _durability = r.Durability;
        _vitalAmount = r.VitalAmount;
        _spellNum = r.SpellNum;
        _power = r.Power;
        _levelReq = r.LevelReq;
        _allowedClasses = r.AllowedClasses is null ? null : new List<short>(r.AllowedClasses);
        _nonTradeable = r.NonTradeable;
        _nonListable = r.NonListable;
        _nonMailable = r.NonMailable;
        _destroyOnDrop = r.DestroyOnDrop;
        _nonJunkable = r.NonJunkable;
        _price = r.Price;
    }

    /// <summary>int view of <see cref="Pic"/> for the numeric spinner, which does not bind to short.</summary>
    public int PicAsInt
    {
        get => Pic;
        set => Pic = (short)value;
    }
    partial void OnNameChanged(string value) => MarkDirty();
    partial void OnPicChanged(short value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(PicAsInt));
    }
    partial void OnDurabilityChanged(short value) => MarkDirty();
    partial void OnVitalAmountChanged(short value) => MarkDirty();
    partial void OnSpellNumChanged(short value) => MarkDirty();
    partial void OnPowerChanged(short value) => MarkDirty();
    partial void OnLevelReqChanged(short value) => MarkDirty();
    partial void OnAllowedClassesChanged(List<short>? value) => MarkDirty();
    partial void OnNonTradeableChanged(bool value) => MarkDirty();
    partial void OnNonListableChanged(bool value) => MarkDirty();
    partial void OnNonMailableChanged(bool value) => MarkDirty();
    partial void OnDestroyOnDropChanged(bool value) => MarkDirty();
    partial void OnNonJunkableChanged(bool value) => MarkDirty();
    partial void OnPriceChanged(int value) => MarkDirty();

    // Changing the type re-labels and re-shows the fields, so every derived caption and visibility
    // flag has to re-raise alongside the dirty mark.
    partial void OnTypeChanged(ItemType value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(VitalAmountLabel));
        OnPropertyChanged(nameof(PowerLabel));
        OnPropertyChanged(nameof(DurabilityVisible));
        OnPropertyChanged(nameof(VitalAmountVisible));
        OnPropertyChanged(nameof(SpellNumVisible));
        OnPropertyChanged(nameof(PowerVisible));
        OnPropertyChanged(nameof(LevelReqVisible));
        OnPropertyChanged(nameof(AllowedClassesVisible));
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
    public void CopyFromRecord(ItemRecord r)
    {
        LoadFromRecord(r);
        IsLoaded = true;
        MarkDirty();
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(DisplayName));
    }

    public void LoadFromRecord(ItemRecord r)
    {
        _loading = true;
        try
        {
            Name = r.Name;
            Pic = r.Pic;
            ItemSheet = r.ItemSheet;
            Type = r.Type;
            Durability = r.Durability;
            VitalAmount = r.VitalAmount;
            SpellNum = r.SpellNum;
            Power = r.Power;
            LevelReq = r.LevelReq;
            AllowedClasses = r.AllowedClasses is null ? null : new List<short>(r.AllowedClasses);
            NonTradeable = r.NonTradeable;
            NonListable = r.NonListable;
            NonMailable = r.NonMailable;
            DestroyOnDrop = r.DestroyOnDrop;
            NonJunkable = r.NonJunkable;
            Price = r.Price;
        }
        finally
        {
            _loading = false;
        }
        ClearDirty();
        OnPropertyChanged(nameof(DisplayName));
    }

    /// <summary>Refill from a server response and mark the row loaded. Unlike
    /// <see cref="LoadFromRecord"/> this does not clear the dirty flag, so the push-changes flow can
    /// still see edits made before the packet arrived.</summary>
    public void ApplyPacket(UpdateItemPacket pkt)
    {
        _loading = true;
        try
        {
            Name = pkt.Name;
            Pic = pkt.Pic;
            ItemSheet = pkt.ItemSheet;
            Type = pkt.Type;
            Durability = pkt.Durability;
            VitalAmount = pkt.VitalAmount;
            SpellNum = pkt.SpellNum;
            Power = pkt.Power;
            LevelReq = pkt.LevelReq;
            AllowedClasses = pkt.AllowedClasses is null ? null : new List<short>(pkt.AllowedClasses);
            NonTradeable = pkt.NonTradeable;
            NonListable = pkt.NonListable;
            NonMailable = pkt.NonMailable;
            DestroyOnDrop = pkt.DestroyOnDrop;
            NonJunkable = pkt.NonJunkable;
            Price = pkt.Price;
        }
        finally
        {
            _loading = false;
        }
        IsLoaded = true;
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(DisplayName));
    }

    /// <summary>Project the row back into a record for saving.
    /// <para>The result is <see cref="ItemRecord.Normalize"/>d, so a field the current type does not use
    /// is written as 0 rather than carrying whatever the row held when it was a different type. The row
    /// itself is left alone — retyping a weapon to a potion and back inside one editing session keeps
    /// the original numbers, since nothing has been saved yet.</para></summary>
    public ItemRecord ToRecord()
    {
        var r = new ItemRecord
        {
            Name = Name,
            Pic = Pic,
            ItemSheet = ItemSheet,
            Type = Type,
            Durability = Durability,
            VitalAmount = VitalAmount,
            SpellNum = SpellNum,
            Power = Power,
            LevelReq = LevelReq,
            AllowedClasses = AllowedClasses is null ? null : new List<short>(AllowedClasses),
            NonTradeable = NonTradeable,
            NonListable = NonListable,
            NonMailable = NonMailable,
            DestroyOnDrop = DestroyOnDrop,
            NonJunkable = NonJunkable,
            Price = Price,
        };
        r.Normalize();
        return r;
    }

    /// <summary>Project the row into the online save packet. The single source of that mapping — both the
    /// editor's own save and the push-changes prompt route through here, so neither can drift from the other.
    /// Normalized through <see cref="ToRecord"/> so the online and offline saves store the same thing.</summary>
    public EditorSaveItemPacket BuildSavePacket()
    {
        var r = ToRecord();
        return new EditorSaveItemPacket
        {
            ItemNum = Index,
            Name = r.Name,
            Pic = r.Pic,
            ItemSheet = r.ItemSheet,
            Type = r.Type,
            Durability = r.Durability,
            VitalAmount = r.VitalAmount,
            SpellNum = r.SpellNum,
            Power = r.Power,
            LevelReq = r.LevelReq,
            AllowedClasses = r.AllowedClasses,
            NonTradeable = r.NonTradeable,
            NonListable = r.NonListable,
            NonMailable = r.NonMailable,
            DestroyOnDrop = r.DestroyOnDrop,
            NonJunkable = r.NonJunkable,
            Price = r.Price,
        };
    }

    // ── Captions ──────────────────────────────────────────────────────────────
    // Durability, SpellNum and the class gate mean one thing wherever they apply, so their captions are
    // set from the view's code-behind. Only these two vary by type.

    /// <summary>Form caption for the potion amount — which vital it moves depends on the type.</summary>
    public string VitalAmountLabel => Type switch
    {
        ItemType.PotionAddHp or ItemType.PotionSubHp => EditorStrings.Get(EditorStrings.DataLabel_HpAmount),
        ItemType.PotionAddMp or ItemType.PotionSubMp => EditorStrings.Get(EditorStrings.DataLabel_MpAmount),
        ItemType.PotionAddSp or ItemType.PotionSubSp => EditorStrings.Get(EditorStrings.DataLabel_SpAmount),
        _ => EditorStrings.Get(EditorStrings.DataLabel_VitalAmount),
    };

    /// <summary>Form caption for <see cref="Power"/> — the one field whose name understates it. It is
    /// damage on a weapon and defense on the three defensive pieces, so the form says which, even though
    /// the same number also gates equipping and prices repairs in every case.</summary>
    public string PowerLabel => Type switch
    {
        ItemType.Weapon => EditorStrings.Get(EditorStrings.DataLabel_Damage),
        ItemType.Armor or ItemType.Helmet or ItemType.Shield => EditorStrings.Get(EditorStrings.DataLabel_Defense),
        _ => EditorStrings.Get(EditorStrings.DataLabel_Power),
    };

    // ── Visibility ────────────────────────────────────────────────────────────
    // All five defer to ItemRecord, the same rules Normalize clears by — so a field the form hides is
    // exactly a field the save zeroes, and the two can't disagree.
    //
    // Key and Currency show none of them: a door matches its key on the item's own id, so a key's
    // numbers are unused.

    public bool DurabilityVisible => ItemRecord.UsesDurability(Type);
    public bool VitalAmountVisible => ItemRecord.UsesVitalAmount(Type);
    public bool SpellNumVisible => ItemRecord.UsesSpellNum(Type);
    public bool PowerVisible => ItemRecord.UsesPower(Type);
    public bool LevelReqVisible => ItemRecord.UsesLevelReq(Type);
    public bool AllowedClassesVisible => ItemRecord.UsesAllowedClasses(Type);
}

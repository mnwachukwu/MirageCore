using CommunityToolkit.Mvvm.ComponentModel;
using Mirage.Editor.Localization;
using Mirage.Editor.Services;
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
    /// <summary>Which equipment slot this is worn in, by the key the loaded game declared. Blank means it
    /// cannot be worn — the right answer for anything that is not equipment, and for a piece nobody has
    /// said where to put yet.</summary>
    [ObservableProperty] private string _equipSlot = "";

    // What the engine itself reads — see ItemRecord. Everything else a game keeps about an item is
    // authored on the record's own attributes.
    [ObservableProperty] private short _durability;
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
        _equipSlot = r.EquipSlot;
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
        OnPropertyChanged(nameof(DurabilityVisible));
        OnPropertyChanged(nameof(EquipSlotVisible));
        OnPropertyChanged(nameof(SelectedEquipSlot));
    }

    partial void OnEquipSlotChanged(string value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(SelectedEquipSlot));
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
            EquipSlot = r.EquipSlot;
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
            EquipSlot = pkt.EquipSlot;
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
            EquipSlot = EquipSlot,
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
            EquipSlot = r.EquipSlot,
            NonTradeable = r.NonTradeable,
            NonListable = r.NonListable,
            NonMailable = r.NonMailable,
            DestroyOnDrop = r.DestroyOnDrop,
            NonJunkable = r.NonJunkable,
            Price = r.Price,
        };
    }

    /// <summary>The slots this world offers, as the picker shows them. Read from the world rather than a
    /// compile-time list, so an editor opened against another game offers that game's slots. The blank
    /// first entry is how an author says a piece is not worn anywhere.</summary>
    public IReadOnlyList<EquipSlotOption> EquipSlotOptions =>
        [new EquipSlotOption("", EditorStrings.Get(EditorStrings.DataLabel_EquipSlotNone)),
         .. WorldEquipSlots.All.Select(s => new EquipSlotOption(s.Key, EditorStrings.GetOrFallback(s.LabelKey, s.Key)))];

    /// <summary>One row of <see cref="EquipSlotOptions"/>: the key that is saved, and the caption shown.</summary>
    public readonly record struct EquipSlotOption(string Key, string Label);

    /// <summary>The picker's selection, as the option object rather than the key.
    ///
    /// <para><b>Bound by item, not by value.</b> A ComboBox handed a value before its list has resolved
    /// finds no match, shows blank, and writes that blank back — which would quietly clear the slot of
    /// every piece an author merely looked at.</para></summary>
    public EquipSlotOption SelectedEquipSlot
    {
        get
        {
            var options = EquipSlotOptions;
            foreach (var option in options)
            {
                if (string.Equals(option.Key, EquipSlot, StringComparison.Ordinal)) return option;
            }
            return options[0];
        }
        set => EquipSlot = value.Key;
    }

    // ── Visibility ────────────────────────────────────────────────────────────
    // All five defer to ItemRecord, the same rules Normalize clears by — so a field the form hides is
    // exactly a field the save zeroes, and the two can't disagree.
    //
    // Key and Currency show none of them: a door matches its key on the item's own id, so a key's
    // numbers are unused.

    public bool DurabilityVisible => ItemRecord.UsesDurability(Type);
    public bool EquipSlotVisible => ItemRecord.IsEquipment(Type);
}

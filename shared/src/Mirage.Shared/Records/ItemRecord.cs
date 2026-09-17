using Mirage.Shared.Extensibility;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Records;

public sealed class ItemRecord
{
    private string _name = string.Empty;
    private string? _trimmedName;
    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            _trimmedName = null;
        }
    }
    /// <summary>Cached <see cref="Name"/>.TrimEnd() — record names are stored fixed-width and
    /// every item message string TrimEnds them.</summary>
    [JsonIgnore]
    public string TrimmedName => _trimmedName ??= _name.TrimEnd();

    public short Pic { get; set; }
    /// <summary>Which item sheet <see cref="Pic"/> is a row of.
    ///
    /// <para>Always written, including when it is 0: a record states which sheet it draws from rather than
    /// leaving a reader to infer it. Absent from an older file it still reads as 0.</para></summary>
    public short ItemSheet { get; set; }
    public ItemType Type { get; set; }

    // ── Type-specific fields ──────────────────────────────────────────────────
    // Each applies to some item types and is meaningless on the rest; the editor shows only the ones
    // that apply. (These replaced the VB6-era Data1/Data2/Data3 positional slots, where the same
    // number meant durability on a sword and healing on a potion.)
    //
    // All five are WhenWritingDefault, so a zero is left out of the file entirely and an item row
    // lists exactly the properties it has — a consumable shows VitalAmount and nothing else. That is the
    // whole point of the expansion: the JSON has to read as a domain object, not as five slots of
    // which three happen to be blank. It is set per-property rather than on the serializer because
    // the global option is shared with map, player and guild persistence, where an explicit 0 is
    // worth keeping. Round-trips cleanly either way: absent deserializes back to 0.

    /// <summary>Weapon/Armor/Helmet/Shield: maximum durability. A worn piece breaks at 0 and stays in
    /// the bag, unequipped, until repaired. 0 = carries no durability budget, so it never breaks.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public short Durability { get; set; }

    /// <summary>How much of whatever it moves, for an item that moves something. The engine carries the
    /// number and has no opinion about what it counts — a game reads it and decides.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public short VitalAmount { get; set; }


    /// <summary>How good the piece is, as one number.
    ///
    /// <para>The engine reads it for exactly one thing: what repairing it costs, through
    /// <see cref="EconomyFormulas.RepairCost"/>. What it means BESIDES that is a game's — damage, a
    /// requirement to wear it, a tier, or nothing at all — so it is one number rather than a
    /// field per purpose.</para></summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public short Power { get; set; }

    /// <summary>Where this item sits on the progression a game defines. 0 = ungated.
    ///
    /// <para>Core reads it for ONE thing: pricing. <c>EconomyFormulas</c> quotes an item's worth against the
    /// income expected at its tier, so without a tier a derived price means nothing. A game decides
    /// what a tier IS — a level, a badge, a chapter, an hour played — and what, if anything, it gates.</para>
    ///
    /// <para>Applies to anything equipped or consumed; currency and keys carry none.</para></summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public short Tier { get; set; }

    /// <summary>Which equipment slot this is worn in, by the key a game declared.
    ///
    /// <para>Blank on anything that is not <see cref="ItemType.Equipment"/>, and blank is also what an
    /// equippable item looks like before an author has said where it goes — it simply cannot be worn
    /// until they do. A key the loaded game does not declare behaves the same way, so a world authored
    /// against another game's slots still opens.</para></summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string EquipSlot { get; set; } = string.Empty;

    /// <summary>Item restriction flags. Each blocks exactly one action; banking is always allowed.
    /// Absent = false, so existing item data is unaffected. All five are enforced server-side:
    /// <see cref="NonTradeable"/> in TradeSystem, <see cref="NonListable"/> in MarketSystem,
    /// <see cref="NonMailable"/> on the mail-attach path, <see cref="DestroyOnDrop"/> in
    /// ItemSystem's drop paths, and <see cref="NonJunkable"/> on the shop sell path.
    ///
    /// <para><b><see cref="NonJunkable"/> is named for what the generic shop path really is:</b> a junk
    /// dump. A vendor takes anything and pays a poor rate on purpose: it is a floor under every
    /// drop, not a market. Blocking an item there says "this is not junk", and it covers two quite
    /// different cases. GOLD and VALOR are barred because dumping currency for a fraction of itself is
    /// nonsense. TREASURE is barred because its full worth sits in <see cref="Price"/>: left junkable it
    /// would be dumpable at the generic rate and the fence would be pointless. Blocked, it can only move
    /// through an authored trade row — which is also what lets two vendors pay differently for the same
    /// trinket.</para></summary>
    public bool NonTradeable { get; set; }   // can't be staged in a player trade
    public bool NonListable { get; set; }    // can't be sold on the marketplace
    public bool NonMailable { get; set; }    // can't be attached to / sent by mail
    public bool DestroyOnDrop { get; set; }  // dropping it (voluntary or on death) destroys it
    public bool NonJunkable { get; set; }    // can't be dumped on a shop through the generic sell path

    /// <summary>What the item is worth in gold: what a shop's SALES list charges for it, and the basis for
    /// what one pays when buying it back (<see cref="EconomyFormulas.ItemSellValue"/>, a deliberately poor
    /// fraction so player-to-player trade still wins).
    ///
    /// <para><b>int, not short.</b> Every other type-specific field here is a <c>short</c>, which makes
    /// <c>short</c> the reflex — and a top-tier weapon prices at 1,369,194, which wraps silently at 32,767.
    /// Most of the ladder would be corrupt and nothing would report it.</para>
    ///
    /// <para>SEEDED, NOT AUTHORED. A generator pass writes <see cref="EconomyFormulas.ItemValue"/> into
    /// every item, so 471 prices stay consistent with each other and with measured income without anyone
    /// typing them. The field exists so a price CAN be overridden — which is the only way to express a
    /// treasure item, whose worth is authored rather than derived from its power and tier.
    /// <see cref="Normalize"/> leaves it standing on every type and never recomputes one: an authored price
    /// is data, and re-seeding must not silently overwrite a deliberate override. Gold needs no type rule
    /// to stay unsellable — <see cref="EconomyFormulas.ItemValue"/> derives nothing for it, so a re-seed
    /// leaves it at zero and the shop's purchase path refuses any zero-price row.</para>
    ///
    /// <para>0 means "no derived worth". Combined with <see cref="NonJunkable"/> that is unambiguous:
    /// a 0-price junkable item can still be dumped for nothing, purely to clear a bag, so not every
    /// item has to be priced for the sell path to work on it.</para></summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Price { get; set; }

    // ── Which fields apply to which type ──────────────────────────────────────
    // The single statement of that rule. The editor asks it what to show, and <see cref="Normalize"/>
    // asks it what to clear, so the form and the file can't drift apart.
    //
    // This is the half of the expansion that actually removes the old format's hazard. Naming the
    // fields makes a row readable; only clearing the inapplicable ones makes it TRUE. Without it,
    // retyping a Weapon as a Consumable leaves Power sitting on the record at its old
    // values — invisible in the editor (which hides them) but live in the file and in every packet.

    /// <summary>The four wearable types, which alone carry durability, power and a class requirement.</summary>
    public static bool IsEquipment(ItemType type) => type is ItemType.Equipment;

    /// <summary>Whether this type is used up rather than worn or carried.</summary>
    public static bool IsConsumable(ItemType type) => type is ItemType.Consumable;

    public static bool UsesDurability(ItemType type) => IsEquipment(type);
    public static bool UsesPower(ItemType type) => IsEquipment(type);
    public static bool UsesVitalAmount(ItemType type) => IsConsumable(type);

    /// <summary>What a character wears or consumes carries a tier; currency and keys carry none. Gold is
    /// not something you qualify for, and a key should not refuse its own door.</summary>
    public static bool UsesTier(ItemType type) => IsEquipment(type) || IsConsumable(type);

    /// <summary>Everything a game hangs on this item that the engine has no name for — a class gate, a
    /// spell written on a scroll, an element, a rarity.
    ///
    /// <para>🔴 <b>What lets a game extend a record the engine owns.</b> An item's own properties are
    /// the ones Core acts on and they are a closed set, because Core cannot act on a property it has
    /// never heard of. A game's are open, and they travel with the item: authored in the editor, saved
    /// to the world file, and read back by name.</para>
    ///
    /// <para>A game declares which keys belong here by writing a model and describing it into this
    /// family; see <c>Records.Extend</c>.</para></summary>
    public AttributeBag Attributes { get; set; } = new();

    /// <summary>Zero every field that does not apply to the current <see cref="Type"/>, so the record
    /// carries only properties it actually has. Call on any path that writes an item — the editor's save
    /// and the server's handler for an editor save packet both do, the latter because the server is
    /// authoritative and will not store stale values a client sent it.
    /// <para>Key and Currency keep none of these: a door matches its key on the item's own id, so a key's
    /// numbers are unused.</para></summary>
    public void Normalize()
    {
        if (!UsesDurability(Type)) Durability = 0;
        if (!UsesVitalAmount(Type)) VitalAmount = 0;
        if (!IsEquipment(Type)) EquipSlot = string.Empty;
        if (!UsesPower(Type)) Power = 0;
        if (!UsesTier(Type)) Tier = 0;
    }
}

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
    // lists exactly the properties it has — a potion shows VitalAmount and nothing else. That is the
    // whole point of the expansion: the JSON has to read as a domain object, not as five slots of
    // which three happen to be blank. It is set per-property rather than on the serializer because
    // the global option is shared with map, player and guild persistence, where an explicit 0 is
    // worth keeping. Round-trips cleanly either way: absent deserializes back to 0.

    /// <summary>Weapon/Armor/Helmet/Shield: maximum durability. A worn piece breaks at 0 and stays in
    /// the bag, unequipped, until repaired. 0 = carries no durability budget, so it never breaks.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public short Durability { get; set; }

    /// <summary>The six potion types: how much of the vital the potion moves. Add* restores it; Sub*
    /// drains that much and restores half as much of each of the other two.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public short VitalAmount { get; set; }

    /// <summary>Spell scroll: the spell taught on use (1-based index into the spell table).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public short SpellNum { get; set; }

    /// <summary>Weapon/Armor/Helmet/Shield: how good the piece is — one number driving three things.
    /// On a weapon it is damage (via <c>WeaponContribution</c>); on armor/helmet it is mitigation (via
    /// <c>GearMitigation</c>); on a shield, mitigation at a quarter weight (<c>ShieldMitigation</c>).
    /// It doubles as the stat needed to equip the piece — STR for a weapon, DEF for the rest, both
    /// through <c>GearStatRequirement</c> — and as the repair rate (<c>EconomyFormulas.RepairCost</c>).
    /// One field rather than separate damage/defense ones precisely because the repair and wear paths
    /// treat all four types alike.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public short Power { get; set; }

    /// <summary>Minimum character level to equip or use this item. 0 = no level gate.
    /// <para>This is what actually paces the tier ladder. The stat requirement derived from
    /// <see cref="Power"/> cannot do it: a class's base stat is high enough at level 1 that a Sage already
    /// meets a mid-ladder piece the day it rolls a character, so the stat gate is a floor that stops the
    /// wrong CLASS wearing something, not a clock. A level is the clock.</para>
    /// <para>Both gates apply — an item can be out of reach for either reason, and the tooltip says
    /// which. Applies to anything equipped or consumed; currency and keys carry no level.</para></summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public short LevelReq { get; set; }

    /// <summary>Weapon/Armor/Helmet/Shield: the classes allowed to equip it (1-based ids). Empty or absent
    /// = every class, which is the usual case. Enforced server-side in ItemSystem's equip path; ask
    /// <see cref="ClassGate"/> rather than testing the list directly.
    /// <para>Nullable so an unrestricted item carries no key at all — the serializer's global
    /// WhenWritingNull does the work, and <see cref="Normalize"/> collapses an empty list to null so
    /// there is only ever one stored spelling of "anyone".</para></summary>
    public List<short>? AllowedClasses { get; set; }

    /// <summary>Item restriction flags. Each blocks exactly one action; banking is always allowed.
    /// Absent = false, so existing item data is unaffected. All five are enforced server-side:
    /// <see cref="NonTradeable"/> in TradeSystem, <see cref="NonListable"/> in MarketSystem,
    /// <see cref="NonMailable"/> on the mail-attach path, <see cref="DestroyOnDrop"/> in
    /// ItemSystem's drop paths, and <see cref="NonJunkable"/> on the shop sell path.
    ///
    /// <para><b><see cref="NonJunkable"/> is named for what the generic shop path really is:</b> a junk
    /// dump. A vendor takes anything and pays a poor rate, which is the point — it is a floor under every
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
    /// <c>short</c> the reflex — and the top-tier weapon prices at 1,369,194, which wraps silently at
    /// 32,767. The whole ladder above about level 60 would be corrupt and nothing would report it.</para>
    ///
    /// <para>SEEDED, NOT AUTHORED. A generator pass writes <see cref="EconomyFormulas.ItemValue"/> into
    /// every item, so 471 prices stay consistent with each other and with measured income without anyone
    /// typing them. The field exists so a price CAN be overridden — which is the only way to express a
    /// treasure item, whose worth is the point rather than a function of its power and tier.
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
    // retyping a Weapon as a Potion leaves Power and ClassReq sitting on the record at their old
    // values — invisible in the editor (which hides them) but live in the file and in every packet.

    /// <summary>The four wearable types, which alone carry durability, power and a class requirement.</summary>
    public static bool IsEquipment(ItemType type) =>
        type is ItemType.Weapon or ItemType.Armor or ItemType.Helmet or ItemType.Shield;

    /// <summary>The six potion types, which alone carry <see cref="VitalAmount"/>.</summary>
    public static bool IsPotion(ItemType type) =>
        type is ItemType.PotionAddHp or ItemType.PotionAddMp or ItemType.PotionAddSp
             or ItemType.PotionSubHp or ItemType.PotionSubMp or ItemType.PotionSubSp;

    public static bool UsesDurability(ItemType type) => IsEquipment(type);
    public static bool UsesPower(ItemType type) => IsEquipment(type);
    public static bool UsesAllowedClasses(ItemType type) => IsEquipment(type);
    public static bool UsesVitalAmount(ItemType type) => IsPotion(type);
    public static bool UsesSpellNum(ItemType type) => type is ItemType.Spell;

    /// <summary>What a character wears or drinks carries a level gate: the wearables and the potions.
    /// <para>A SCROLL does not. Its gate lives on the SPELL it teaches, which is where learning is actually
    /// refused, so a level on the paper would be a second number nothing reads. Currency and keys carry none
    /// either: gold is not something you qualify for, and a door that refuses its own key because the holder
    /// is level 4 is a puzzle nobody asked for.</para></summary>
    public static bool UsesLevelReq(ItemType type) => IsEquipment(type) || IsPotion(type);

    /// <summary>The item's level gate; for a spell scroll, the gate on the spell it teaches.</summary>
    public static int EffectiveLevelReq(ItemRecord? item, SpellRecord? taughtSpell)
    {
        if (item is { LevelReq: > 0 }) return item.LevelReq;
        return item?.Type == ItemType.Spell ? taughtSpell?.LevelReq ?? 0 : 0;
    }

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
        if (!UsesSpellNum(Type)) SpellNum = 0;
        if (!UsesPower(Type)) Power = 0;
        if (!UsesLevelReq(Type)) LevelReq = 0;
        AllowedClasses = UsesAllowedClasses(Type) ? ClassGate.Normalize(AllowedClasses) : null;
    }
}

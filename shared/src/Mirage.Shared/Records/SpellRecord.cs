using System.Text.Json.Serialization;

namespace Mirage.Shared.Records;

public sealed class SpellRecord
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
    /// <summary>Cached <see cref="Name"/>.TrimEnd() — cast-announcement messages TrimEnd
    /// the spell name on every successful cast.</summary>
    [JsonIgnore]
    public string TrimmedName => _trimmedName ??= _name.TrimEnd();

    /// <summary>The classes allowed to learn it (1-based ids). Empty or absent = every class. Ask
    /// <see cref="ClassGate"/> rather than testing the list directly; <see cref="Normalize"/> collapses
    /// an empty list to null so an unrestricted spell carries no key.</summary>
    public List<short>? AllowedClasses { get; set; }
    public SpellType Type { get; set; }

    // ── Type-specific fields ──────────────────────────────────────────────────
    // As on ItemRecord, each applies to some spell types and is meaningless on the rest, and each is
    // WhenWritingDefault so a spell row lists only what it actually carries. The split is clean here:
    // the Add*/Sub* spells use VitalAmount alone, and GiveItem uses the other three and no VitalAmount.

    /// <summary>Add*/Sub*: the spell's magnitude — how much of the vital it restores or drains, before
    /// the caster's INT contribution, the crit roll and variance. Doubles as the spell's INT
    /// requirement to learn (via <c>GetSpellIntRequirement</c>) and therefore sets its MP cost, exactly
    /// as a weapon's <see cref="ItemRecord.Power"/> doubles as its STR gate. Unused by GiveItem.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public short VitalAmount { get; set; }

    /// <summary>GiveItem: the item handed to the target (1-based index into the item table).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public short ItemNum { get; set; }

    /// <summary>GiveItem: how many of <see cref="ItemNum"/> to hand over.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public short ItemQuantity { get; set; }

    /// <summary>GiveItem: the INT requirement to learn it, and hence its MP cost. Carried separately
    /// because GiveItem's <see cref="ItemNum"/> is an id rather than a magnitude, so unlike the
    /// Add*/Sub* spells it has no power value to gate off. Unused by every other spell type.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public short IntReq { get; set; }

    /// <summary>Minimum character level to learn this spell. 0 = no level gate. Applies to every spell
    /// type, unlike the fields above.
    /// <para>The INT requirement above cannot pace the tier ladder on its own — a specialist starts with
    /// enough INT to meet a mid-ladder spell at level 1 — so INT decides <em>who</em> may learn a spell
    /// and this decides <em>when</em>. Both are checked, and the message names whichever failed.</para>
    /// <para>Not to be confused with the VB6 field of the same name, which was inert: it was saved, sent
    /// and bound to a slider that nothing ever read (see docs/changes-from-vb6.md). This one is
    /// enforced on the learn path.</para></summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public short LevelReq { get; set; }

    // ── Which fields apply to which type ──────────────────────────────────────
    // As on ItemRecord: one statement of the rule, consulted by the editor to decide what to show and
    // by Normalize to decide what to clear. The split is total here — GiveItem uses three fields and
    // no VitalAmount, every other type uses VitalAmount and none of the three.

    public static bool UsesVitalAmount(SpellType type) => type is not SpellType.GiveItem;
    public static bool UsesItemFields(SpellType type) => type is SpellType.GiveItem;

    /// <summary>Zero every field that does not apply to the current <see cref="Type"/>. Matters more here
    /// than on an item: a spell retyped away from GiveItem would otherwise keep its old IntReq, and
    /// <c>RawSpellRequirement</c> reads IntReq for GiveItem and VitalAmount for everything else — so a
    /// stale value silently sets the wrong gate and MP cost the moment the type changes back.</summary>
    public void Normalize()
    {
        if (!UsesVitalAmount(Type)) VitalAmount = 0;
        if (!UsesItemFields(Type))
        {
            ItemNum = 0;
            ItemQuantity = 0;
            IntReq = 0;
        }
        // The class gate applies to every spell type, so unlike the fields above it is never cleared —
        // only canonicalized (deduped, sorted, empty collapsed to null).
        AllowedClasses = ClassGate.Normalize(AllowedClasses);
    }
}

using System.Text.Json.Serialization;

namespace Mirage.Shared.Records;

/// <summary>One line of an NPC's drop table: an item, how many, and how often.
///
/// <para>Every entry rolls INDEPENDENTLY on a kill, so one death yields nothing, one thing, or several —
/// "almost always a little gold, sometimes a potion, very rarely the sword" is three lines at three
/// chances. The cost is that expected yield is the SUM of the chances, so ten 50% lines drop five things
/// a kill; only authoring restraint keeps that sane. There is deliberately NO length cap on
/// <see cref="NpcRecord.Drops"/>: repeated lines are the only way to author a multi-item payout (see
/// <see cref="Quantity"/>), so a length limit would be a limit on payout.</para>
///
/// <para>A hoard of twelve is twelve lines at one chance each, not one line of twelve, and behaves
/// better for it — ~21% to yield at least one and occasionally two, rather than a flat 2% for the whole
/// pile.</para></summary>
public sealed class NpcDrop
{
    /// <summary>1-based index into the item table. 0 or out of range = an inert line, skipped at roll time
    /// rather than treated as an error; an editor can hold a half-authored row without breaking a kill.</summary>
    public int ItemNum { get; set; }

    /// <summary>How many. Meaningful ONLY for a CURRENCY item, where it is the stack size, and clamped to
    /// at least 1 at roll time.
    ///
    /// <para> For anything else this field is DEAD — not "usually one", but never read at all.
    /// <c>ItemSystem.FindOpenInvSlot</c> merges slots for Currency alone; everything else takes a fresh
    /// slot and <c>HasItem</c> reports a flat 1 no matter what is written here. A line of
    /// <c>{sword, quantity 12}</c> yields ONE sword, silently. Author twelve lines instead.</para></summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public short Quantity { get; set; }

    /// <summary>Chance as a direct percent: 1 = 1%, 50 = 50%, 100 or more = always. 0 or less never drops,
    /// which is the way to park a line without deleting it. Rolled against
    /// <see cref="RandomSourceExtensions.Percent"/>, which returns [0..99], so the drop lands when the roll is
    /// BELOW this.</summary>
    public short Chance { get; set; }

    /// <summary>A line worth rolling: it names a real item and can actually land.</summary>
    [JsonIgnore]
    public bool IsLive => ItemNum > 0 && Chance > 0;
}

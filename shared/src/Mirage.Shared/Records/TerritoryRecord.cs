using System.Text.Json.Serialization;

namespace Mirage.Shared.Records;

/// <summary>
/// Who holds a contestable territory, what it has earned them, and who is coming for it.
///
/// <para>A territory and a map group are two different things. The GROUP is authored: it says which maps
/// make up the place and whether the place is contestable at all. Everything here is what one running
/// server accumulated on top of that — an owner, a vault's worth of income, a war-night queue — and it
/// means nothing beside a different world. So it lives in the DATA folder
/// (<c>territories/territory{MapGroup}.json</c>), never in the world one, and the editor never sees it.</para>
///
/// <para>The map group index is the key, which is the whole of the link between them: a territory is the
/// maps of its group, and the group says nothing further about it.</para>
///
/// <para>A territory group with no file yet is simply unclaimed — <see cref="World"/> makes the record on
/// first ask and it reaches disk when something changes it, so declaring a territory in the editor needs
/// nothing on the data side.</para>
/// </summary>
public sealed class TerritoryRecord
{
    /// <summary>Filename stem inside <c>territories/</c>; the trailing number is the <see cref="MapGroup"/>.</summary>
    public const string FileStem = "territory";

    /// <summary>The map group whose maps this territory is (>= 1).
    ///
    /// <para>NOT serialized. The number lives in the filename, the loader fills this in from it, and every
    /// other record keys off its filename the same way. Writing it as well would let a copied file claim a
    /// group that is not its own, and a territory believed to be one it is not scores for the wrong
    /// guild.</para></summary>
    [JsonIgnore]
    public int MapGroup { get; set; }

    /// <summary>The guild that currently controls this territory (0 = unclaimed).</summary>
    public int ControllingGuild { get; set; }

    // ── Income ───────────────────────────────────────────────────────────────────────────────────────────
    /// <summary>Gold accrued from PvE kills here since the last daily settlement (capped per day). Credited
    /// to <see cref="ControllingGuild"/>'s vault at the 00:00 settlement, then zeroed.</summary>
    public long PendingTerritoryIncome { get; set; }
    /// <summary>Running total of income CREDITED this territory-week; snapshotted into
    /// <see cref="PreviousWeekIncome"/> and zeroed at the weekly reset.</summary>
    public long IncomeThisWeek { get; set; }
    /// <summary>The gold this territory generated for its owner over the previous week (the Territories-tab
    /// column). 0 for unclaimed/untaxed.</summary>
    public long PreviousWeekIncome { get; set; }
    /// <summary>Settlement idempotency: the last date the weekly PreviousWeekIncome roll ran here (default
    /// MinValue = never). Keeps the daily settlement + /guildreset from double-rolling.</summary>
    public DateOnly LastWeekRollDate { get; set; }
    /// <summary>Consecutive weeks the current owner has held this territory (0 = fresh). Drives the income
    /// multiplier (cap <see cref="Constants.TerritoryWeeksHeldCap"/>). Incremented on a successful weekly
    /// defense at war night; reset to 0 on capture.</summary>
    public int WeeksHeld { get; set; }

    // ── War-night challenge registration ─────────────────────────────────────────────────────────────────
    /// <summary>Guild indices registered to contest this territory at the next war night (up to
    /// <see cref="Constants.TerritoryMaxChallengers"/>). Registrations are made ahead of war night and must
    /// survive a restart. Cleared at resolution.</summary>
    public List<int> Challengers { get; set; } = new();
    /// <summary>True when the current owner has abandoned this territory by challenging a different one (the
    /// one-territory cap): it resolves as an unclaimed contest (no defender) at the next war night. The owner
    /// keeps its income until then. Reset at resolution.</summary>
    public bool DefenderAbandoned { get; set; }

    /// <summary>Deep copy for an off-thread save snapshot. All fields are value types EXCEPT
    /// <see cref="Challengers"/>, which is copied so the snapshot can't be mutated by a live edit.</summary>
    public TerritoryRecord Clone()
    {
        var copy = (TerritoryRecord)MemberwiseClone();
        copy.Challengers = new List<int>(Challengers);
        return copy;
    }
}

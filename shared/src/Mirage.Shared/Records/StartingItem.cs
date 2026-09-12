namespace Mirage.Shared.Records;

/// <summary>One item a new character is created holding, authored on the world's manifest beside the
/// appearances it offers.
///
/// <para>NOTHING REQUIRES OPENING THE BAG. Equipment arrives already WORN and everything else is carried,
/// so there is no "equipped" flag to author and no way to author the wrong one. Asking a first-time player
/// to open a bag and work out what goes where is a worse opening than any gear is worth.</para></summary>
public sealed record StartingItem
{
    /// <summary>1-based index into the item table. 0 or out of range = an inert line, skipped at grant
    /// time rather than treated as an error, so a half-authored row cannot break character creation.</summary>
    public int ItemNum { get; init; }

    /// <summary>How many. Meaningful for currency (the stack size); every other type gets exactly one,
    /// since a character cannot start with two of the same sword in one slot.</summary>
    public short Quantity { get; init; }
}

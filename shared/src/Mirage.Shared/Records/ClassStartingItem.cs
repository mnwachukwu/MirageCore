namespace Mirage.Shared.Records;

/// <summary>One item a class starts with. Equipment arrives WORN, everything else carried — see
/// <see cref="ClassRecord.StartingItems"/> for why there is no "equipped" flag to author.</summary>
public sealed class ClassStartingItem
{
    /// <summary>1-based index into the item table. 0 or out of range = an inert line, skipped at grant
    /// time rather than treated as an error, so a half-authored row cannot break character creation.</summary>
    public int ItemNum { get; set; }

    /// <summary>How many. Meaningful for currency (the stack size); every other type gets exactly one,
    /// since a character cannot start with two of the same sword in one slot.</summary>
    public short Quantity { get; set; }
}

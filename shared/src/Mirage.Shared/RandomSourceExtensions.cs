namespace Mirage.Shared;

/// <summary>
/// The chance rolls, taken from an injected <see cref="IRandomSource"/> rather than ambient randomness.
///
/// <para>Every gate of the form <c>SomethingChance(...) > roll</c> draws from here, so a test that supplies a
/// scripted source decides whether a block, dodge, crit, drop or weather miss fires. Reaching those branches by
/// sampling is not an option: the chances are single-digit percent and capped, so "cast until it dodges" either
/// runs long or fails on the run where it does not come up.</para>
/// </summary>
public static class RandomSourceExtensions
{
    /// <summary>A roll to compare against a <c>*ChancePerMille</c> value, on whatever scale
    /// <see cref="Constants.ChanceScaleFactor"/> currently sets.</summary>
    public static int PerMille(this IRandomSource rng) => rng.Next(Constants.ChancePercentRollSides);

    /// <summary>A percentile roll in [0..99], for the fixed 1-percent granularity of drops and durability.</summary>
    public static int Percent(this IRandomSource rng) => rng.Next(Constants.PercentRollSides);
}

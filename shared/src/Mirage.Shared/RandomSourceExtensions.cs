namespace Mirage.Shared;

/// <summary>
/// The chance rolls, taken from an injected <see cref="IRandomSource"/> rather than ambient randomness.
///
/// <para>A gate of the form <c>chance > roll</c> draws from here, so a test that supplies a scripted source
/// decides whether a drop or a weather change fires rather than waiting for one. Reaching those branches by
/// sampling is not an option: the chances are single-digit percent, so "roll until it happens" either runs
/// long or fails on the run where it does not come up.</para>
/// </summary>
public static class RandomSourceExtensions
{
    /// <summary>A percentile roll in [0..99], for the fixed 1-percent granularity of drops and durability.</summary>
    public static int Percent(this IRandomSource rng) => rng.Next(Constants.PercentRollSides);
}

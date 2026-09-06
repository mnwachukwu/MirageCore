using Mirage.Shared;

namespace Mirage.Server.Tests.Platform;

/// <summary>
/// Chance sources that make a gate fire, or not fire, on demand.
///
/// <para>Every combat gate reads <c>SomeChance(...) > rng.PerMille()</c>, so the branch taken is decided by
/// the roll and nothing else. Sampling for a branch — swinging until something is dodged — leaves a test whose
/// result depends on the run, which is not a test. These pick the branch instead.</para>
/// </summary>
public static class PinnedRolls
{
    /// <summary>Rolls 0, which is below any chance of 1 or more: every gate with a non-zero chance fires.
    /// The bounded <c>Next(min, max)</c> overloads return their minimum for the same reason.</summary>
    public static IRandomSource Always { get; } = new Fixed(_ => 0, (min, _) => min, 0.0);

    /// <summary>Rolls the top of the space, which no chance can exceed: no gate fires. Chances are capped well
    /// below the roll ceiling, so this holds however they are tuned.</summary>
    public static IRandomSource Never { get; } = new Fixed(max => max - 1, (_, max) => max - 1, Math.BitDecrement(1.0));

    private sealed class Fixed(Func<int, int> bounded, Func<int, int, int> ranged, double unit) : IRandomSource
    {
        public int Next(int maxExclusive) => bounded(maxExclusive);
        public int Next(int minInclusive, int maxExclusive) => ranged(minInclusive, maxExclusive);
        public long NextInt64(long minInclusive, long maxExclusive) => minInclusive;

        // 🔴 The unit roll has to follow the SAME direction as the bounded ones. A shared 0.0 made Never behave
        // as Always for every `NextDouble() < chance` gate — including the AI's cast/melee weave — so a test
        // reaching for Never to pin melee would silently have pinned casting instead.
        public double NextDouble() => unit;
    }
}

namespace Mirage.Shared;

/// <summary>
/// The source of chance, injectable so that rolled outcomes can be asserted exactly.
///
/// <para>The rolls behind this interface decide the things players argue about: whether a hit is
/// blocked or dodged, whether it crits, which stat a death drains, how much of a stack drops, who
/// wins a loot roll, whether an NPC casts or closes, which way it wanders or kites, where it spawns,
/// and how long mail spends in transit. Every one of those read <c>Random.Shared</c> directly at the
/// point of use, so a test could only sample the distribution — which is why the kite-bias suite is
/// a statistical test rather than a behavioral one. With the roll injected, a test can pin the
/// sequence and assert the outcome: this loot table with this roll yields this drop.</para>
///
/// <para>Only the members the server actually uses are exposed, deliberately: a narrow surface is
/// trivial to implement in a test double, whereas mirroring <see cref="Random"/> would not be.</para>
/// </summary>
public interface IRandomSource
{
    /// <summary>Non-negative int below <paramref name="maxExclusive"/>.</summary>
    int Next(int maxExclusive);

    /// <summary>Int in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).</summary>
    int Next(int minInclusive, int maxExclusive);

    /// <summary>Long in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).</summary>
    long NextInt64(long minInclusive, long maxExclusive);

    /// <summary>Double in [0.0, 1.0).</summary>
    double NextDouble();
}

/// <summary>The production source: forwards to <see cref="Random.Shared"/>.</summary>
public sealed class SharedRandom : IRandomSource
{
    /// <summary>Shared instance — stateless forwarder, so one is enough. Used as the default when a
    /// system is constructed without an explicit source, which keeps behavior identical to the
    /// direct <c>Random.Shared</c> calls this replaced.</summary>
    public static readonly SharedRandom Instance = new();

    public int Next(int maxExclusive) => Random.Shared.Next(maxExclusive);

    public int Next(int minInclusive, int maxExclusive) => Random.Shared.Next(minInclusive, maxExclusive);

    public long NextInt64(long minInclusive, long maxExclusive) => Random.Shared.NextInt64(minInclusive, maxExclusive);

    public double NextDouble() => Random.Shared.NextDouble();
}

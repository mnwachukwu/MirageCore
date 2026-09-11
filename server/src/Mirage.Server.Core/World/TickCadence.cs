using Mirage.Shared;

namespace Mirage.Server.Core.World;

/// <summary>
/// Whether a cooldown has elapsed, asked from a tick rather than from the clock.
///
/// <para>Work driven by the tick can only ever satisfy a cooldown at a multiple of
/// <see cref="Constants.AiTickIntervalMs"/>. A cooldown that is a whole multiple of the interval
/// therefore lands EXACTLY on a tick boundary, where a plain <c>now &gt; then + cooldown</c> is decided
/// by microseconds — and a tick arriving a hair early waits a whole extra interval.</para>
///
/// <para>So a tick within half an interval of the deadline counts. That resolves to the nearest tick
/// rather than the first one strictly past the deadline, which is both what the cooldown means and
/// stable against a tick landing either side of it. Two entities on the same cooldown act on the same
/// beat however much work the tick did before reaching either of them.</para>
///
/// <para>Read the tick's own timestamp, not the clock. Sampling the clock partway through a tick makes
/// the answer depend on how much work preceded it, so two callers on one cooldown disagree by however
/// long the tick has been running.</para>
/// </summary>
public static class TickCadence
{
    /// <summary>How far before a deadline a tick still counts as having reached it.</summary>
    public const long TickToleranceMs = Constants.AiTickIntervalMs / 2;

    /// <summary>True when <paramref name="cooldownMs"/> has elapsed since <paramref name="since"/>, as of
    /// the tick timestamp <paramref name="now"/>.</summary>
    public static bool Elapsed(long now, long since, long cooldownMs) =>
        now + TickToleranceMs > since + cooldownMs;
}

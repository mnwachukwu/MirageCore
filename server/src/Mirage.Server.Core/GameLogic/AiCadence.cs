using Mirage.Shared;

namespace Mirage.Server.Core.GameLogic;

/// <summary>
/// Whether a cooldown an NPC is waiting on has elapsed, asked from the AI tick.
///
/// <para>An NPC only acts on an AI tick, so a cooldown can only ever be satisfied at a multiple of
/// <see cref="Constants.AiTickIntervalMs"/>. Every NPC cooldown in the game is 1000 ms and the tick is
/// 500, so the deadline lands EXACTLY on a tick boundary — and a plain <c>now &gt; then + cooldown</c>
/// there is decided by microseconds. A tick that arrives a hair early waits a whole extra 500 ms.</para>
///
/// <para>So a tick within half an interval of the deadline counts. That resolves to the nearest tick
/// rather than the first one strictly past the deadline, which is both what the cooldown means and stable
/// against a tick landing either side of it. Two NPCs on the same cooldown then swing on the same beat
/// however much work the tick did before reaching either of them.</para>
///
/// <para>Without it the two melee paths disagreed with each other: the player gate read the tick's own
/// timestamp and settled at a steady 1500 ms, while the NPC-vs-NPC gate read the clock afresh partway
/// through the tick and flipped between 1000 and 1500 depending on the tick's workload.</para>
/// </summary>
public static class AiCadence
{
    /// <summary>How far before a deadline a tick still counts as having reached it.</summary>
    public const long TickToleranceMs = Constants.AiTickIntervalMs / 2;

    /// <summary>True when <paramref name="cooldownMs"/> has elapsed since <paramref name="since"/>, as of
    /// the tick timestamp <paramref name="now"/>.</summary>
    public static bool Elapsed(long now, long since, long cooldownMs) =>
        now + TickToleranceMs > since + cooldownMs;
}

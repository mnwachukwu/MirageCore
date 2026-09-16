namespace Mirage.Shared;

/// <summary>
/// Whether a cooldown a creature is waiting on has run out, asked on the AI beat.
///
/// <para><b>A creature acts on the beat and at no other moment.</b> So a cooldown that is a whole
/// number of beats long ends exactly ON one — and a plain "has the time passed" there is decided by
/// microseconds. A beat arriving a hair early refuses the creature, and it waits another whole beat.</para>
///
/// <para>So a beat within half an interval of the deadline counts. That resolves to the NEAREST beat
/// rather than the first one strictly past the deadline, which is both what a cooldown means and stable
/// against a beat landing either side of it. Two creatures on the same cooldown then swing together
/// however much work the beat did before reaching either of them.</para>
///
/// <para>⚠ For creatures only. A player's input is read every frame rather than on the beat, so there is
/// no boundary to round to and the same slack would simply let them act early.</para>
/// </summary>
public static class AiCadence
{
    /// <summary>Half a beat. A deadline nearer than this to the current beat belongs to it.</summary>
    public const long ToleranceMs = Constants.AiTickIntervalMs / 2;

    /// <summary>Whether <paramref name="cooldownMs"/> has run out, counting from
    /// <paramref name="since"/>, for something asked on the beat.</summary>
    public static bool Elapsed(long now, long since, long cooldownMs) =>
        now + ToleranceMs > since + cooldownMs;
}

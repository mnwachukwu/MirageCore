namespace Mirage.Shared.Extensibility;

/// <summary>
/// What this game says about moving under your own power.
///
/// <para><b>Core has no idea what a step costs.</b> It knows where a body may stand, how often a step
/// may be taken, and the difference between a walk and a run, because all three are about the map. It
/// has no stamina, no encumbrance, no fatigue, and no opinion about whether a body that has been
/// sprinting for a minute should still be able to. That is a game's, and this is where it says so.</para>
///
/// <para><b>A refusal slows, it does not stop.</b> <see cref="MayRun"/> answers whether a body can
/// still manage a run; a refusal turns that step into a WALK rather than cancelling it. A body out of
/// breath keeps moving, and a rule that could halt a player mid-corridor is a different kind of rule
/// from a rule about pace.</para>
///
/// <para><b>Only a step under their own power reaches this.</b> A warp, a shove, a teleport, and a
/// walk across a map edge are all moves a body did not run to make, so none of them is charged.</para>
/// </summary>
public interface IMovePolicy
{
    /// <summary>Whether they can still manage a run. Yield a reason to bring them down to a walk, or
    /// nothing to let the run stand. Every policy is asked and the first refusal is the answer.
    ///
    /// <para>Asked before the step is paid for, so a body brought down to a walk is also charged the
    /// walking pace rather than the running one.</para></summary>
    Refusal MayRun(EntityHandle who) => Refusal.Allow;

    /// <summary>They took a step, at a run, under their own power onto a tile of the same map. What a
    /// run costs is charged here.
    ///
    /// <para>Not called for a walk, for a refused step, or for a body carried somewhere by something
    /// other than its own legs.</para></summary>
    void OnRan(EntityHandle who) { }
}

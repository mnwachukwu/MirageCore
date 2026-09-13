using Mirage.Shared.Extensibility;

namespace Mirage.Modules.Survey;

/// <summary>
/// Resting gives it back.
///
/// <para>Recovery is the half of stamina that nothing in the world triggers — no step, no arrival, no
/// item. It simply happens as time passes, which is what tick work is for. Slower than walking spends
/// it, so a survey has a shape: range out, find things, come back.</para>
///
/// <para><b>It runs on the game thread, in the loop's own order, and it has no roster of its own.</b>
/// There is no list of players here because the module has no business keeping one — it asks the world
/// about the handles it already knows and stops.</para>
/// </summary>
public sealed class SurveyTick : ITickWork
{
    private IWorld? _world;

    public string Name => "Survey recovery";

    /// <summary>Once every twenty ticks rather than every tick, because a point of stamina a second is
    /// the rate this game wants and the loop should not be asked more often than that.</summary>
    public int EveryTicks => Survey.RecoveryEveryTicks;

    public void Begin(IWorld world) => _world = world;

    public void Tick(long tick)
    {
        if (_world is not { } world) return;

        // Every slot the protocol allows: the world answers null for the ones holding nobody, which is
        // cheaper than this module keeping a roster in step with joins and drops.
        for (int i = 1; i <= Shared.Constants.MaxPlayers; i++)
        {
            var who = EntityHandle.ForPlayer(i);
            if (!world.IsInWorld(who)) continue;

            var bag = world.AttributesOf(who);
            if (bag is null) continue;
            if (!bag.TryGet(Survey.Stamina, out var stamina)) continue;
            if (!bag.TryGet(Survey.StaminaMax, out var max)) continue;

            long now = stamina.AsLong(), ceiling = max.AsLong();
            if (now >= ceiling) continue;

            world.SetAttribute(who, Survey.Stamina, Math.Min(ceiling, now + 1));
        }
    }
}

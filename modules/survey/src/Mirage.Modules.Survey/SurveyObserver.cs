using Mirage.Shared.Extensibility;

namespace Mirage.Modules.Survey;

/// <summary>
/// The rules of the survey: walking is tiring, and sometimes you find something.
///
/// <para><b>The engine tells it what happened; it decides what that means.</b> Core raises a step
/// because a step happened, and has no opinion about whether a step costs anything. Everything below is
/// this game's answer, and a game with a different one overrides nothing — it declares its own
/// observer.</para>
///
/// <para>Runs on the game thread, inside the movement that raised it, so it does small arithmetic and
/// returns.</para>
/// </summary>
public sealed class SurveyObserver : IWorldObserver
{
    private IWorld? _world;
    private string[] _catalogue = [];
    private int _steps;

    public string Name => "Survey rules";

    /// <summary>Handed the world and the authored species once the engine is built.</summary>
    public void Begin(IWorld world, string[] catalog)
    {
        _world = world;
        _catalogue = catalog;
    }

    /// <summary>A surveyor arrives. One that has never been here before is enrolled; one coming back
    /// keeps what they had, because their attributes were persisted with their character.</summary>
    public void OnPlayerJoined(EntityHandle who)
    {
        if (_world is not { } world) return;

        var bag = world.AttributesOf(who);
        if (bag is null || bag.Has(Survey.StaminaMax)) return;

        world.SetAttributes(who,
        [
            new(Survey.StaminaMax, Survey.FullStamina),
            new(Survey.Stamina, Survey.FullStamina),
            new(Survey.Specimens, 0L),
            new(Survey.Rank, Survey.RankFor(0)),
        ]);
    }

    public void OnPlayerMoved(EntityHandle who, in WorldPlace from, in WorldPlace to)
    {
        if (_world is not { } world) return;

        Spend(world, who);

        // Deterministic rather than random: a survey that pays out on a coin flip cannot be tested, and
        // a player cannot tell a dry spell from a broken game. Every twelfth step turns something up.
        if (++_steps % Survey.FindOneStepIn == 0) Find(world, who);
    }

    private static void Spend(IWorld world, EntityHandle who)
    {
        var bag = world.AttributesOf(who);
        if (bag is null || !bag.TryGet(Survey.Stamina, out var stamina)) return;

        long left = Math.Max(0, stamina.AsLong() - Survey.StepCost);
        if (left != stamina.AsLong()) world.SetAttribute(who, Survey.Stamina, left);
    }

    /// <summary>Something worth writing down. A tired surveyor finds nothing — which is the only thing
    /// stamina is FOR, and the reason it is worth spending.</summary>
    private void Find(IWorld world, EntityHandle who)
    {
        var bag = world.AttributesOf(who);
        if (bag is null) return;
        if (!bag.TryGet(Survey.Stamina, out var stamina) || stamina.AsLong() <= 0) return;
        if (_catalogue.Length == 0) return;   // a world with no species authored has nothing to find

        long found = (bag.TryGet(Survey.Specimens, out var seen) ? seen.AsLong() : 0) + 1;
        world.SetAttributes(who,
        [
            new(Survey.Specimens, found),
            new(Survey.Rank, Survey.RankFor(found)),
        ]);
    }
}

/// <summary>
/// Nothing dies on a botanical survey.
///
/// <para>A policy that refuses every death is a real answer rather than an absence: Core's
/// <c>DeathSystem</c> exists and works, and this game says it never runs. A game with no notion of dying
/// could equally declare no policy at all and get the same outcome by a different route — Core would
/// then move the body and take nothing.</para>
/// </summary>
public sealed class NothingDiesHere : IDeathPolicy
{
    public Refusal MayDie(in Death death) => Refusal.Deny("Nobody dies out here.");
}

/// <summary>
/// A dropped line is not a lost afternoon.
///
/// <para>The body stays put for half a minute so a surveyor who reconnects is where they left off rather
/// than back at the start. The same seam an RPG uses to stop a player escaping a fight by pulling the
/// plug, asked for the opposite reason.</para>
/// </summary>
public sealed class StayWhileSurveying : ILingerPolicy
{
    public Deadline LingerFor(EntityHandle who)
        => Deadline.InSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Survey.LingerSeconds);
}

/// <summary>
/// What a specimen leaves behind is the finder's.
///
/// <para>Two answers to the same question, and they are the two reasons a game has an opinion about loot
/// at all. The CLAIM holds a drop for whoever was standing over it, long enough to bend down — without
/// it, a find belongs to whoever is quickest rather than to whoever did the work. The CHANCE is the
/// other half: a surveyor who has cataloged enough to be a Naturalist spots what a beginner walks past,
/// so their lines land more often.</para>
///
/// <para>Neither is a number Core could have guessed. The engine rolls the table a world authored and
/// puts what lands on the tile; everything above is this game's.</para>
/// </summary>
public sealed class TheFindersSpecimen : ILootPolicy
{
    private IWorld? _world;

    /// <summary>Handed the world once there is one, like everything else here that acts on it.</summary>
    public void Begin(IWorld world) => _world = world;

    public void Weigh(Spoil spoil)
    {
        ArgumentNullException.ThrowIfNull(spoil);

        // Nobody to hold it for and nobody whose eye to read: the world itself took the thing out, and
        // what it was carrying is free to whoever finds it.
        if (!spoil.Killer.IsPlayer) return;

        spoil.ClaimedBy = spoil.Killer;
        spoil.ClaimSeconds = Survey.ClaimSeconds;

        if (_world?.AttributesOf(spoil.Killer) is not { } bag) return;

        long found = bag.TryGet(Survey.Specimens, out var seen) ? seen.AsLong() : 0;
        if (found < Survey.PracticedEye) return;

        spoil.ChancePercent += spoil.ChancePercent * Survey.PracticedEyeBonusPercent / 100;
    }
}

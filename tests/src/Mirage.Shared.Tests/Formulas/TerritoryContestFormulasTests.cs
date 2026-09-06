using NUnit.Framework;

namespace Mirage.Shared.Tests.Formulas;

/// <summary>The king-of-the-hill contest math: capture-point count, the signed capture-meter
/// physics (push / reinforce / drift / flip), per-tick scoring with the defender edge, and winner
/// resolution.</summary>
[TestFixture]
public class TerritoryContestFormulasTests
{
    [Test]
    public void PointCount_OnePerNMaps_Clamped()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TerritoryContestFormulas.PointCount(3), Is.EqualTo(2));   // below min → min 2
            Assert.That(TerritoryContestFormulas.PointCount(10), Is.EqualTo(2));  // 10/5 = 2
            Assert.That(TerritoryContestFormulas.PointCount(15), Is.EqualTo(3));  // 15/5 = 3
            Assert.That(TerritoryContestFormulas.PointCount(25), Is.EqualTo(5));  // 25/5 = 5 (cap)
            Assert.That(TerritoryContestFormulas.PointCount(100), Is.EqualTo(5)); // above max → max 5
        });
    }

    [Test]
    public void AdvanceMeter_ChallengerPushes_AndFlipsAtFull()
    {
        int full = Constants.TerritoryCaptureFull;
        // Owner 5 securely holds (-full); challenger 6 with the majority pushes the meter up one step.
        var step = TerritoryContestFormulas.AdvanceMeter(-full, owner: 5, challenger: 0, majorityGuild: 6);
        Assert.That(step, Is.EqualTo(new TerritoryContestFormulas.MeterResult(-full + 1, 5, 6)));

        // One step short of full → reaching full flips the point: new owner 6, reset to -full, challenger cleared.
        var flip = TerritoryContestFormulas.AdvanceMeter(full - 1, owner: 5, challenger: 6, majorityGuild: 6);
        Assert.That(flip, Is.EqualTo(new TerritoryContestFormulas.MeterResult(-full, 6, 0)));
    }

    [Test]
    public void AdvanceMeter_OwnerReinforces_And_ContestedDriftsToNeutral()
    {
        int full = Constants.TerritoryCaptureFull;
        // Owner regains the majority → meter pushes back toward secure (capped at -full), clearing a challenger.
        Assert.That(TerritoryContestFormulas.AdvanceMeter(0, owner: 5, challenger: 6, majorityGuild: 5),
            Is.EqualTo(new TerritoryContestFormulas.MeterResult(-1, 5, 0)));
        Assert.That(TerritoryContestFormulas.AdvanceMeter(-full, owner: 5, challenger: 0, majorityGuild: 5),
            Is.EqualTo(new TerritoryContestFormulas.MeterResult(-full, 5, 0)));   // already secure
        // Contested (majority 0) drifts toward neutral; a positive meter keeps its challenger until it hits 0.
        Assert.That(TerritoryContestFormulas.AdvanceMeter(2, owner: 5, challenger: 6, majorityGuild: 0),
            Is.EqualTo(new TerritoryContestFormulas.MeterResult(1, 5, 6)));
        Assert.That(TerritoryContestFormulas.AdvanceMeter(1, owner: 5, challenger: 6, majorityGuild: 0),
            Is.EqualTo(new TerritoryContestFormulas.MeterResult(0, 5, 0)));
    }

    [Test]
    public void ScorerAndScoreDelta_OwnerScoresWhenSecure_DefenderEdge()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TerritoryContestFormulas.ScorerThisTick(-Constants.TerritoryCaptureFull, 5), Is.EqualTo(5)); // secure → owner scores
            Assert.That(TerritoryContestFormulas.ScorerThisTick(0, 5), Is.EqualTo(0));   // neutral band → nobody
            Assert.That(TerritoryContestFormulas.ScorerThisTick(-Constants.TerritoryCaptureFull, 0), Is.EqualTo(0)); // unowned
            Assert.That(TerritoryContestFormulas.ScoreDelta(5, defenderGuild: 5),
                Is.EqualTo(Constants.TerritoryOwnedScorePerTick + Constants.TerritoryDefenderScoreBonus)); // defender edge
            Assert.That(TerritoryContestFormulas.ScoreDelta(6, defenderGuild: 5), Is.EqualTo(Constants.TerritoryOwnedScorePerTick)); // attacker
            Assert.That(TerritoryContestFormulas.ScoreDelta(0, defenderGuild: 5), Is.EqualTo(0)); // no scorer
        });
    }

    [Test]
    public void WithinRadius_InclusiveEuclideanDisc()
    {
        int r = Constants.TerritoryCapturePointRadius;
        Assert.Multiple(() =>
        {
            Assert.That(TerritoryContestFormulas.WithinRadius(5, 5, 5, 5, r), Is.True);          // dead center
            Assert.That(TerritoryContestFormulas.WithinRadius(5 + r, 5, 5, 5, r), Is.True);      // exactly on the edge (inclusive)
            Assert.That(TerritoryContestFormulas.WithinRadius(5 + r + 1, 5, 5, 5, r), Is.False); // one past the edge
            Assert.That(TerritoryContestFormulas.WithinRadius(0, 0, 0, 0, 0), Is.True);          // zero radius = only the center
            Assert.That(TerritoryContestFormulas.WithinRadius(1, 0, 0, 0, 0), Is.False);
        });
    }

    [Test]
    public void DetermineWinner_StrictTopWins_TieGoesToDefender()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TerritoryContestFormulas.DetermineWinner(new Dictionary<int, long> { [5] = 100, [6] = 50 }, 5), Is.EqualTo(5));
            Assert.That(TerritoryContestFormulas.DetermineWinner(new Dictionary<int, long> { [5] = 50, [6] = 100 }, 5), Is.EqualTo(6)); // attacker outscores defender
            Assert.That(TerritoryContestFormulas.DetermineWinner(new Dictionary<int, long> { [5] = 100, [6] = 100 }, 5), Is.EqualTo(5)); // tie → defender
            Assert.That(TerritoryContestFormulas.DetermineWinner(new Dictionary<int, long> { [6] = 100, [7] = 100 }, 0), Is.EqualTo(0)); // unclaimed tie → unclaimed
            Assert.That(TerritoryContestFormulas.DetermineWinner(new Dictionary<int, long> { [6] = 100, [7] = 50 }, 0), Is.EqualTo(6)); // strict top on unclaimed
            Assert.That(TerritoryContestFormulas.DetermineWinner(new Dictionary<int, long>(), 5), Is.EqualTo(5)); // nobody scored → defender keeps
        });
    }

    /// <summary>
    /// The capture radius is not a free number: it is pinned against <see cref="Constants.SpellRangeTiles"/>,
    /// and the ranged-vs-melee trade on a point is what the pairing buys.
    ///
    /// <para>🔴 This is the assertion that catches somebody retuning either constant on its own. Both edges
    /// land exactly on the current pair, so a one-tile move in either direction breaks a named case here
    /// rather than quietly changing how a war plays.</para>
    /// </summary>
    [Test]
    public void CaptureRadiusHoldsItsTradeWithSpellRange()
    {
        int r = Constants.TerritoryCapturePointRadius;
        int spell = Constants.SpellRangeTiles;
        const int cx = 20, cy = 20;   // away from any edge, so nothing here is a clamp artifact

        // The nearest tile a caster can stand on WITHOUT contesting, and how deep it reaches from there.
        int justOutside = r + 1;
        int deepest = justOutside - spell;

        Assert.Multiple(() =>
        {
            Assert.That(TerritoryContestFormulas.WithinRadius(cx + justOutside, cy, cx, cy, r), Is.False,
                "a caster one tile past the edge is poking, not holding — it must not count for the majority");
            Assert.That(deepest, Is.LessThanOrEqualTo(0),
                "poking from outside has to reach the CENTER, or standing dead center is untouchable");
            Assert.That(deepest, Is.GreaterThanOrEqualTo(0),
                "and no further than the center, or a caster owns the whole point from safety");

            // A melee at the center answering that caster closes to adjacent: distance justOutside - 1 = r.
            Assert.That(TerritoryContestFormulas.WithinRadius(cx + justOutside - 1, cy, cx, cy, r), Is.True,
                "answering a poke must not cost the melee the point it is standing on");

            // What the caster gives up by stepping in far enough to contest: the zone is all the room it has.
            Assert.That(2 * r, Is.GreaterThan(spell),
                "a contesting caster needs somewhere to back off to, or ranged cannot hold a point at all");
            Assert.That(2 * r, Is.LessThan(spell * 2),
                "but not a full kite lane, or a melee that commits can never close the gap");
        });
    }
}

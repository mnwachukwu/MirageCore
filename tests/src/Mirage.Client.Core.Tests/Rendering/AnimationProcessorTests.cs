using Mirage.Client.Core.Logic;
using Mirage.Client.Core.State;
using Mirage.Shared;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.Rendering;

/// <summary>Tile-animation frame counter (advances one frame per interval) and the attack-animation expiry
/// sweep (a swing frame older than 1000ms clears).</summary>
[TestFixture]
public class AnimationProcessorTests
{
    [Test]
    public void Process_AdvancesMapAnimFrame_OnlyAfterInterval()
    {
        var s = new ClientState { MapAnimTimer = 0, MapAnimFrame = 0 };

        AnimationProcessor.Process(s, Constants.MapAnimIntervalMs);   // exactly one interval elapsed
        Assert.Multiple(() =>
        {
            Assert.That(s.MapAnimFrame, Is.EqualTo(1));
            Assert.That(s.MapAnimTimer, Is.EqualTo((long)Constants.MapAnimIntervalMs));
        });

        AnimationProcessor.Process(s, Constants.MapAnimIntervalMs + 50);   // only 50ms since last → no advance
        Assert.That(s.MapAnimFrame, Is.EqualTo(1));
    }

    // AttackTimer 0 is far in the past relative to the wall clock, so the stale swing frame clears.
    [Test]
    public void Process_ClearsStaleAttackFlag()
    {
        var s = new ClientState();
        var p = s.Players[1];
        p.Attacking = true;
        p.AttackTimer = 0;
        AnimationProcessor.Process(s, 1000);
        Assert.That(p.Attacking, Is.False, "an attack animation older than 1000ms clears");
    }

    // A "future" AttackTimer makes the elapsed time negative, so a fresh swing frame is kept.
    [Test]
    public void Process_KeepsFreshAttackFlag()
    {
        var s = new ClientState();
        var p = s.Players[1];
        p.Attacking = true;
        p.AttackTimer = long.MaxValue / 2;
        AnimationProcessor.Process(s, 1000);
        Assert.That(p.Attacking, Is.True, "a fresh attack animation is kept");
    }
}

using Mirage.Client.Core.Logic;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.Rendering;

/// <summary>The pooled, allocation-free particle subsystem: bounded capacity, swap-remove of dead particles,
/// the rain→splash and homing-projectile arrival morphs, and the pure fade/velocity/flight-time helpers the
/// shell reads to draw.</summary>
[TestFixture]
public class ParticleSystemTests
{
    // Flight time scales with distance at the homing speed, capped at the projectile lifetime (900ms).
    [Test]
    public void ProjectileFlightMs_ScalesWithDistance_CapsAtMaxLife()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ParticleSystem.ProjectileFlightMs(260f), Is.EqualTo(500f).Within(0.5f)); // 260/520 = 0.5s
            Assert.That(ParticleSystem.ProjectileFlightMs(10_000f), Is.EqualTo(900f).Within(0.5f)); // capped
            Assert.That(ParticleSystem.ProjectileFlightMs(0f), Is.EqualTo(0f));
        });
    }

    // On-screen velocity = world velocity minus camera velocity (the parallax used to angle streaks).
    [Test]
    public void OnScreenVelocity_SubtractsCameraVelocity()
    {
        var p = new Particle { Vx = 100f, Vy = 200f, Kind = ParticleKind.Debris };
        var (vx, vy) = ParticleSystem.OnScreenVelocity(p, 30f, 50f);
        Assert.Multiple(() =>
        {
            Assert.That(vx, Is.EqualTo(70f));
            Assert.That(vy, Is.EqualTo(150f));
        });
    }

    // A rain streak's apparent fall never drops below the floor, so a fast downward pan can't suspend it.
    [Test]
    public void OnScreenVelocity_RainFloor_KeepsFalling()
    {
        var p = new Particle { Vx = 0f, Vy = 100f, Kind = ParticleKind.RainStreak };
        var (_, vy) = ParticleSystem.OnScreenVelocity(p, 0f, 80f);   // 100-80 = 20 < 40 → clamped to 40
        Assert.That(vy, Is.EqualTo(40f));
    }

    [Test]
    public void AlphaOf_RainConstant_GenericFadesWithAge()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ParticleSystem.AlphaOf(new Particle { Kind = ParticleKind.RainStreak }), Is.EqualTo(0.8f));
            Assert.That(ParticleSystem.AlphaOf(new Particle { Kind = ParticleKind.Spark, Age = 0.5f, Life = 1f }),
                Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(ParticleSystem.AlphaOf(new Particle { Kind = ParticleKind.Spark, Age = 2f, Life = 1f }),
                Is.EqualTo(0f), "alpha clamps at 0, never negative");
        });
    }

    [Test]
    public void EmitsLight_MagicalKindsOnly()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ParticleSystem.EmitsLight(ParticleKind.SpellBall), Is.True);
            Assert.That(ParticleSystem.EmitsLight(ParticleKind.Orbit), Is.True);
            Assert.That(ParticleSystem.EmitsLight(ParticleKind.ImpactBurst), Is.True);
            Assert.That(ParticleSystem.EmitsLight(ParticleKind.RainStreak), Is.False);
            Assert.That(ParticleSystem.EmitsLight(ParticleKind.Spark), Is.False);
        });
    }

    // The pool is deliberately bounded: once full, further spawns are silently dropped (lightweight mandate).
    [Test]
    public void TrySpawn_BoundsPoolAtCapacity()
    {
        var sys = new ParticleSystem();
        for (int i = 0; i < ParticleSystem.Capacity; i++)
            Assert.That(sys.TrySpawn(new Particle { Life = 1f }), Is.True);
        Assert.That(sys.Count, Is.EqualTo(ParticleSystem.Capacity));
        Assert.That(sys.TrySpawn(new Particle { Life = 1f }), Is.False, "a full pool drops further spawns");
    }

    [Test]
    public void ClearAll_EmptiesPool()
    {
        var sys = new ParticleSystem();
        sys.TrySpawn(new Particle { Life = 1f });
        sys.ClearAll();
        Assert.That(sys.Count, Is.EqualTo(0));
    }

    // Seam crossings re-anchor every world-pixel particle by the slide offset so FX stay pinned in the world.
    [Test]
    public void ShiftAll_ReanchorsEveryParticle()
    {
        var sys = new ParticleSystem();
        sys.TrySpawn(new Particle { X = 100f, Y = 50f, Life = 1f });
        sys.ShiftAll(10f, -5f);
        Assert.Multiple(() =>
        {
            Assert.That(sys.Active[0].X, Is.EqualTo(110f));
            Assert.That(sys.Active[0].Y, Is.EqualTo(45f));
        });
    }

    /// <summary>
    /// A bolt in the air when the player crosses a seam still arrives at its victim.
    ///
    /// <para>Both ends of a projectile are stored relative to the 3x3 grid's origin, and a crossing
    /// re-anchors that grid onto a different centre map — sliding the whole space by one map, which is
    /// 16 tiles across at 32px a tile. Carrying the POSITION without the TARGET leaves the bolt correctly
    /// placed and aimed half a screen from anything, which is what a spell flying off in a random
    /// direction actually is.</para>
    ///
    /// <para>Stated as the aim VECTOR rather than as coordinates, because that is the thing that has to
    /// survive: wherever the space moves to, the bolt must still be pointing at the same victim.</para>
    /// </summary>
    [Test]
    public void ShiftAll_KeepsAHomingBoltAimedAtItsTarget()
    {
        const float MapWidthPx = 16 * 32;    // one map slide, the offset a real seam cross applies
        const float MapHeightPx = 12 * 32;
        var sys = new ParticleSystem();
        sys.TrySpawn(new Particle { X = 100f, Y = 50f, Tx = 260f, Ty = 210f, Life = 1f, Kind = ParticleKind.SpellBall });

        var before = sys.Active[0];
        float aimX = before.Tx - before.X, aimY = before.Ty - before.Y;

        sys.ShiftAll(-MapWidthPx, MapHeightPx);

        var after = sys.Active[0];
        Assert.Multiple(() =>
        {
            Assert.That(after.Tx - after.X, Is.EqualTo(aimX), "the bolt still points the same way");
            Assert.That(after.Ty - after.Y, Is.EqualTo(aimY));
            Assert.That(after.Tx, Is.EqualTo(260f - MapWidthPx), "and the target moved with the space it lives in");
            Assert.That(after.Ty, Is.EqualTo(210f + MapHeightPx));
        });
    }

    /// <summary>Re-anchoring is not a reprieve: it moves a bolt without aging it, so nothing gains flight
    /// time by crossing a seam. The damage number is scheduled off the distance measured at cast, and a
    /// bolt that outlived that schedule would land after its own damage.</summary>
    [Test]
    public void ShiftAll_DoesNotExtendAProjectilesLife()
    {
        var sys = new ParticleSystem();
        sys.TrySpawn(new Particle { X = 0f, Y = 0f, Tx = 100f, Ty = 0f, Life = 0.9f, Age = 0.4f, Kind = ParticleKind.SpellBall });

        sys.ShiftAll(-512f, 384f);

        Assert.Multiple(() =>
        {
            Assert.That(sys.Active[0].Age, Is.EqualTo(0.4f), "no age is refunded");
            Assert.That(sys.Active[0].Life, Is.EqualTo(0.9f), "and no life is granted");
        });
    }

    [Test]
    public void Update_ExpiredNonRainParticle_SwapRemoved()
    {
        var sys = new ParticleSystem();
        sys.TrySpawn(new Particle { X = 0f, Y = 0f, Life = 1f, Age = 0f, Kind = ParticleKind.Spark });
        sys.Update(2f);   // Age 2 >= Life 1
        Assert.That(sys.Count, Is.EqualTo(0));
    }

    // A raindrop that reaches the end of its life becomes a splash where it landed, not nothing.
    [Test]
    public void Update_ExpiredRainStreak_MorphsToSplash()
    {
        var sys = new ParticleSystem();
        sys.TrySpawn(new Particle { X = 0f, Y = 0f, Vy = 100f, Life = 1f, Age = 0f, Kind = ParticleKind.RainStreak });
        sys.Update(2f);
        Assert.That(sys.Count, Is.EqualTo(1), "a raindrop becomes a splash, not nothing");
        Assert.That(sys.Active[0].Kind, Is.EqualTo(ParticleKind.Splash));
    }

    // A homing drain bolt that reaches its target bursts into an impact cluster; the bolt itself is consumed.
    [Test]
    public void Update_HomingSpellBall_OnArrival_BurstsIntoImpacts()
    {
        var sys = new ParticleSystem();
        // Target 10px away; one large dt snaps the bolt onto the target this frame.
        sys.TrySpawn(new Particle { X = 0f, Y = 0f, Tx = 10f, Ty = 0f, Life = 0.9f, Kind = ParticleKind.SpellBall });
        sys.Update(1f);
        Assert.That(sys.Count, Is.GreaterThan(0), "arrival spawns an impact burst");
        foreach (var p in sys.Active)
        {
            Assert.That(p.Kind, Is.EqualTo(ParticleKind.ImpactBurst),
                "the bolt is consumed on arrival; only impact particles remain");
        }
    }
}

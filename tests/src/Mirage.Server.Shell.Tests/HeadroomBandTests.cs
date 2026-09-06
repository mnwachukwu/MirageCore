using Mirage.Server.Shell.Bench;
using NUnit.Framework;

namespace Mirage.Server.Shell.Tests;

/// <summary>
/// The benchmark's arithmetic: turning a measured ramp into "this many players, with this much of the
/// game thread left spare". This is the number an operator types into their player limit, so it is worth
/// checking without spending five minutes running a real ramp to get one.
/// </summary>
[TestFixture]
public sealed class HeadroomBandTests
{
    // Players and game-thread utilisation; the rest of a step does not affect the band.
    private static BenchStep Step(int players, double gameThread, long bytes = 0) =>
        new(players, gameThread, ProcessCpu: 0, WorkingSetBytes: bytes, Overruns: 0, PacketsPerSecond: 0, Maps: 1);

    [Test]
    public void InterpolatesBetweenTheStepsEitherSideOfTheLine()
    {
        // 50% headroom means the thread may run at 0.50. That sits exactly halfway between these two.
        var band = LoadBenchmark.Band([Step(100, 0.40), Step(200, 0.60)], headroom: 0.50);

        Assert.That(band.Players, Is.EqualTo(150));
        Assert.That(band.AtLeast, Is.False);
    }

    [Test]
    public void MemoryFollowsTheSameInterpolation()
    {
        var band = LoadBenchmark.Band(
            [Step(100, 0.40, bytes: 200_000_000), Step(200, 0.60, bytes: 300_000_000)], headroom: 0.50);

        Assert.That(band.WorkingSetBytes, Is.EqualTo(250_000_000));
    }

    [Test]
    public void ReportsThePeakAsAFloorWhenTheRampNeverGotThatBusy()
    {
        // Nothing was measured past 300, so 300 is a floor and not the answer.
        var band = LoadBenchmark.Band([Step(100, 0.10), Step(200, 0.15), Step(300, 0.20)], headroom: 0.50);

        Assert.That(band.Players, Is.EqualTo(300));
        Assert.That(band.AtLeast, Is.True);
    }

    [Test]
    public void TakesTheFirstCrossingWhenTheCurveDipsBackUnder()
    {
        // Load measurements are noisy and a later step can read lower. Reading the limit off the
        // optimistic side of that noise is what costs an operator a full server.
        var band = LoadBenchmark.Band(
            [Step(100, 0.40), Step(200, 0.80), Step(300, 0.45), Step(400, 0.90)], headroom: 0.50);

        Assert.That(band.Players, Is.EqualTo(125));
    }

    [Test]
    public void ReportsNobodyWhenEvenTheFirstStepWasOverTheLine()
    {
        var band = LoadBenchmark.Band([Step(50, 0.95)], headroom: 0.50);

        Assert.That(band.Players, Is.Zero);
        Assert.That(band.AtLeast, Is.False);
    }

    [Test]
    public void InterpolatesFromTheLastStepUnderTheLine()
    {
        // Two steps sit under 0.50 before the crossing. The pair either side of the line is (200, 300),
        // not (100, 300) — anchoring on the first step under it would report a number far too low.
        var band = LoadBenchmark.Band([Step(100, 0.40), Step(200, 0.40), Step(300, 0.95)], headroom: 0.50);

        Assert.That(band.Players, Is.EqualTo(218));   // 200 + 100 * (0.50-0.40)/(0.95-0.40)
    }

    [Test]
    public void ReportsNobodyForAnEmptyRun()
    {
        var band = LoadBenchmark.Band([], headroom: 0.50);

        Assert.That(band.Players, Is.Zero);
        Assert.That(band.AtLeast, Is.False);
    }
}

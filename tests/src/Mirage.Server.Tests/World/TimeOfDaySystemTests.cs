using Mirage.Server.Core.GameLogic;
using Mirage.Shared;
using NUnit.Framework;
using System.Reflection;

namespace Mirage.Server.Tests.World;

/// <summary>The day/night cycle's phase mapping (TimeOfDaySystem.PhaseAt, private static): a cycle position in
/// ms maps to the phase (Day→Dusk→Night→Dawn) and the 0..1 progress within it. These boundaries drive the
/// NPC night-boost, the darkness overlay, and the phase broadcasts, so the flip points are pinned exactly.
/// The live Tick/Init/JumpToPhase paths ride Environment.TickCount64 (wall-clock) and aren't unit-targets;
/// this is their deterministic core.</summary>
[TestFixture]
public class TimeOfDaySystemTests
{
    static readonly MethodInfo PhaseAtMethod =
        typeof(TimeOfDaySystem).GetMethod("PhaseAt", BindingFlags.NonPublic | BindingFlags.Static)!;

    static (TimePhase Phase, float Progress) PhaseAt(long posMs)
        => ((TimePhase, float))PhaseAtMethod.Invoke(null, new object[] { posMs })!;

    [Test]
    public void PhaseAt_CycleStart_IsDayAtZeroProgress()
    {
        var (phase, progress) = PhaseAt(0);
        Assert.Multiple(() =>
        {
            Assert.That(phase, Is.EqualTo(TimePhase.Day));
            Assert.That(progress, Is.EqualTo(0f));
        });
    }

    // Each phase flips on the exact scheduled millisecond — last ms of one phase vs first ms of the next.
    [Test]
    public void PhaseAt_BoundariesFlipExactlyOnSchedule()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PhaseAt(Constants.TodDayDurationMs - 1).Phase, Is.EqualTo(TimePhase.Day), "last ms of Day");
            Assert.That(PhaseAt(Constants.TodDayDurationMs).Phase, Is.EqualTo(TimePhase.Dusk), "Dusk begins");
            Assert.That(PhaseAt(Constants.TodNightStartMs - 1).Phase, Is.EqualTo(TimePhase.Dusk), "last ms of Dusk");
            Assert.That(PhaseAt(Constants.TodNightStartMs).Phase, Is.EqualTo(TimePhase.Night), "Night begins");
            Assert.That(PhaseAt(Constants.TodDawnStartMs - 1).Phase, Is.EqualTo(TimePhase.Night), "last ms of Night");
            Assert.That(PhaseAt(Constants.TodDawnStartMs).Phase, Is.EqualTo(TimePhase.Dawn), "Dawn begins");
            Assert.That(PhaseAt(Constants.TodCycleDurationMs - 1).Phase, Is.EqualTo(TimePhase.Dawn), "last ms of the cycle");
        });
    }

    [Test]
    public void PhaseAt_ProgressIsZeroAtEachPhaseStart_AndHalfwayThroughDay()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PhaseAt(Constants.TodDayDurationMs).Progress, Is.EqualTo(0f), "Dusk starts at progress 0");
            Assert.That(PhaseAt(Constants.TodNightStartMs).Progress, Is.EqualTo(0f), "Night starts at progress 0");
            Assert.That(PhaseAt(Constants.TodDawnStartMs).Progress, Is.EqualTo(0f), "Dawn starts at progress 0");
            Assert.That(PhaseAt(Constants.TodDayDurationMs / 2).Progress, Is.EqualTo(0.5f).Within(1e-4f), "halfway through Day");
        });
    }
}

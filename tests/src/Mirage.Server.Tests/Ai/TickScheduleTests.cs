using Mirage.Server.Core.GameLogic;
using NUnit.Framework;

namespace Mirage.Server.Tests.Ai;

/// <summary>
/// When a tick that just ran is next due.
///
/// <para>Restarting the interval from the clock makes every wake-up permanent: a tick that ran a millisecond
/// late pushes the next one a millisecond late as well, and there is nothing that ever gives it back. Two
/// hundred ticks a minute of that is how a 500 ms cadence quietly becomes something else, and it takes the
/// beats that ride on it — spawns, movement, the AI — with it.</para>
///
/// <para>So the deadline advances from the PREVIOUS deadline, and the clock is consulted only to decide
/// whether catching up is still worth attempting.</para>
/// </summary>
[TestFixture]
public class TickScheduleTests
{
    private const int Interval = 500;

    [Test]
    public void OnTime_TheNextDeadlineIsExactlyOneIntervalOn()
    {
        Assert.That(GameLoop.Schedule(due: 1000, now: 1000, Interval), Is.EqualTo(1500));
    }

    /// <summary>The whole point: running late does not move the schedule. The next tick is still due when it
    /// was always due, so the lateness is absorbed rather than carried.</summary>
    [Test]
    public void RunningLate_DoesNotPushTheNextDeadlineOut()
    {
        Assert.That(GameLoop.Schedule(due: 1000, now: 1007, Interval), Is.EqualTo(1500),
            "not 1507 — a schedule that drifts never drifts back");
    }

    /// <summary>Twelve hundred ticks of being a millisecond late, which is twenty minutes of ordinary
    /// running. Anchored to the clock this ends over a second adrift; anchored to itself it ends exactly
    /// where it started.</summary>
    [Test]
    public void OverThousandsOfTicks_TheCadenceHoldsExactly()
    {
        long due = 0;
        long clock = 0;
        for (int i = 0; i < 1200; i++)
        {
            clock = due + 1;                       // woken a millisecond late, every single time
            due = GameLoop.Schedule(due, clock, Interval);
        }

        Assert.That(due, Is.EqualTo(1200L * Interval));
    }

    /// <summary>A tick that fell more than a whole interval behind — a long save, a stalled thread — starts
    /// again from the clock. Replaying the beats it slept through helps nobody, and a deadline already in
    /// the past would fire every pass until it caught up.</summary>
    [Test]
    public void MoreThanAnIntervalBehind_ItStartsAgainFromTheClock()
    {
        Assert.That(GameLoop.Schedule(due: 1000, now: 9000, Interval), Is.EqualTo(9500));
    }

    [Test]
    public void ExactlyOneIntervalBehind_StillStartsAgainRatherThanFiringTwice()
    {
        // due + interval == now: the recovered deadline would be this instant, so the next pass fires it
        // immediately and gains nothing. Taking the clock puts a whole interval between the two beats.
        Assert.That(GameLoop.Schedule(due: 1000, now: 1500, Interval), Is.EqualTo(2000));
    }
}

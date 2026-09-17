using Mirage.Shared.Extensibility;
using NUnit.Framework;

namespace Mirage.Shared.Tests.Extensibility;

/// <summary>
/// Two properties carry the weight here. Zero is the only sentinel, so a field nobody wrote is already
/// unset; and a deadline read against the wrong clock answers "not yet" rather than "long ago", which
/// is the difference between a feature that does not fire and one that fires constantly.
/// </summary>
[TestFixture]
public class DeadlineTests
{
    [Test]
    public void TheZeroValueIsUnset()
    {
        Deadline never = default;

        Assert.Multiple(() =>
        {
            Assert.That(never.IsSet, Is.False);
            Assert.That(never, Is.EqualTo(Deadline.None));
            Assert.That(never.HasPassed(long.MaxValue, DeadlineClock.Utc), Is.False);
            Assert.That(never.IsPending(0, DeadlineClock.Utc), Is.False);
            Assert.That(never.Remaining(0, DeadlineClock.Utc), Is.Zero);
        });
    }

    [Test]
    public void ADeadlinePassesOnTheSecondItNames()
    {
        var at = Deadline.AtUtc(100);

        Assert.Multiple(() =>
        {
            Assert.That(at.IsPending(99, DeadlineClock.Utc), Is.True);
            Assert.That(at.HasPassed(99, DeadlineClock.Utc), Is.False);
            Assert.That(at.HasPassed(100, DeadlineClock.Utc), Is.True);
            Assert.That(at.HasPassed(101, DeadlineClock.Utc), Is.True);
        });
    }

    /// <summary>A tick count compared against Unix seconds is a number that compares fine and means
    /// nothing. Carrying the clock lets the mismatch be refused.</summary>
    [Test]
    public void AMismatchedClockNeitherPassesNorPends()
    {
        var tickDeadline = Deadline.AtTick(100);

        Assert.Multiple(() =>
        {
            Assert.That(tickDeadline.HasPassed(long.MaxValue, DeadlineClock.Utc), Is.False);
            Assert.That(tickDeadline.IsPending(0, DeadlineClock.Utc), Is.False);
            Assert.That(tickDeadline.HasPassed(100, DeadlineClock.Tick), Is.True);
        });
    }

    [Test]
    public void AWindowThatClosesAsItOpensIsNotAWindow()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Deadline.InSeconds(50, 0), Is.EqualTo(Deadline.None));
            Assert.That(Deadline.InSeconds(50, -5), Is.EqualTo(Deadline.None));
            Assert.That(Deadline.InTicks(50, 0), Is.EqualTo(Deadline.None));
            Assert.That(Deadline.InSeconds(50, 10), Is.EqualTo(Deadline.AtUtc(60)));
        });
    }

    [Test]
    public void RemainingNeverGoesNegative()
    {
        var at = Deadline.AtUtc(100);

        Assert.Multiple(() =>
        {
            Assert.That(at.Remaining(90, DeadlineClock.Utc), Is.EqualTo(10));
            Assert.That(at.Remaining(100, DeadlineClock.Utc), Is.Zero);
            Assert.That(at.Remaining(500, DeadlineClock.Utc), Is.Zero);
        });
    }
}

/// <summary>
/// The falling edge is the whole reason this type exists: code that can only ask "still running" can
/// not tell the first tick after expiry from the thousandth, so expiry cleanup either never runs or
/// runs forever.
/// </summary>
[TestFixture]
public class TimedFlagTests
{
    [Test]
    public void ItReportsExpiryOnExactlyOnePoll()
    {
        var flag = default(TimedFlag);
        flag.Set(Deadline.AtTick(10));

        Assert.Multiple(() =>
        {
            Assert.That(flag.Poll(5, DeadlineClock.Tick, out bool earlyEdge), Is.True);
            Assert.That(earlyEdge, Is.False, "still running");

            Assert.That(flag.Poll(10, DeadlineClock.Tick, out bool theEdge), Is.False);
            Assert.That(theEdge, Is.True, "the tick it expired on");

            Assert.That(flag.Poll(11, DeadlineClock.Tick, out bool afterEdge), Is.False);
            Assert.That(afterEdge, Is.False, "and not again");

            Assert.That(flag.Poll(9999, DeadlineClock.Tick, out bool laterEdge), Is.False);
            Assert.That(laterEdge, Is.False);
        });
    }

    [Test]
    public void AFlagThatWasNeverSetNeverReportsAnEdge()
    {
        var flag = default(TimedFlag);

        Assert.Multiple(() =>
        {
            Assert.That(flag.Poll(100, DeadlineClock.Tick, out bool edge), Is.False);
            Assert.That(edge, Is.False);
        });
    }

    /// <summary>Cancelling is not expiring. A flag cleared deliberately must not run the cleanup that
    /// belongs to running out of time.</summary>
    [Test]
    public void ClearingDoesNotReportAnEdge()
    {
        var flag = default(TimedFlag);
        flag.Set(Deadline.AtTick(10));
        flag.Poll(5, DeadlineClock.Tick, out _);

        flag.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(flag.Poll(6, DeadlineClock.Tick, out bool edge), Is.False);
            Assert.That(edge, Is.False);
        });
    }

    [Test]
    public void RestartingArmsTheEdgeAgain()
    {
        var flag = default(TimedFlag);

        flag.Set(Deadline.AtTick(10));
        flag.Poll(10, DeadlineClock.Tick, out bool first);

        flag.Set(Deadline.AtTick(20));
        flag.Poll(15, DeadlineClock.Tick, out _);
        flag.Poll(20, DeadlineClock.Tick, out bool second);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.True);
            Assert.That(second, Is.True);
        });
    }

    [Test]
    public void IsActiveDoesNotConsumeTheEdge()
    {
        var flag = default(TimedFlag);
        flag.Set(Deadline.AtTick(10));

        Assert.Multiple(() =>
        {
            Assert.That(flag.IsActive(20, DeadlineClock.Tick), Is.False);
            Assert.That(flag.Poll(20, DeadlineClock.Tick, out bool edge), Is.False);
            Assert.That(edge, Is.True, "asking whether it was active did not spend the edge");
        });
    }
}

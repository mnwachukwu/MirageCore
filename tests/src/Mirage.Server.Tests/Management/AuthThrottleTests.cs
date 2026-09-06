using Mirage.Server.Host.Management;
using Mirage.Server.Tests.World;
using Mirage.Shared;
using NUnit.Framework;

namespace Mirage.Server.Tests.Management;

/// <summary>
/// The management port is a console behind one secret, so what stops it being a guessing game is the
/// failure limit rather than the token's length.
/// </summary>
[TestFixture]
public sealed class AuthThrottleTests
{
    private sealed class FixedClock : IClock
    {
        public long UtcNowUnix { get; set; }
        public DateTime LocalNow => DateTimeOffset.FromUnixTimeSeconds(UtcNowUnix).LocalDateTime;
    }

    [Test]
    public void AnUnknownAddressIsNotLockedOut()
    {
        var throttle = new AuthThrottle(new FixedClock());

        Assert.That(throttle.IsLockedOut("10.0.0.1"), Is.False);
    }

    [Test]
    public void TakesTheWholeAllowanceBeforeLockingOut()
    {
        var throttle = new AuthThrottle(new FixedClock());

        for (int attempt = 1; attempt < AuthThrottle.MaxFailures; attempt++)
        {
            Assert.That(throttle.RecordFailure("10.0.0.1"), Is.False, $"attempt {attempt} should not lock out");
            Assert.That(throttle.IsLockedOut("10.0.0.1"), Is.False);
        }

        Assert.That(throttle.RecordFailure("10.0.0.1"), Is.True, "the last of the allowance locks out");
        Assert.That(throttle.IsLockedOut("10.0.0.1"), Is.True);
    }

    [Test]
    public void TheLockoutLiftsWhenItExpires()
    {
        var clock = new FixedClock { UtcNowUnix = 1_000 };
        var throttle = new AuthThrottle(clock);
        for (int i = 0; i < AuthThrottle.MaxFailures; i++) throttle.RecordFailure("10.0.0.1");

        clock.UtcNowUnix += AuthThrottle.LockoutSeconds - 1;
        Assert.That(throttle.IsLockedOut("10.0.0.1"), Is.True, "still inside the lockout");

        clock.UtcNowUnix += 2;
        Assert.That(throttle.IsLockedOut("10.0.0.1"), Is.False, "the lockout has run out");
    }

    [Test]
    public void OneAddressLockingOutDoesNotAffectAnother()
    {
        // Otherwise a single wrong guess anywhere would lock every operator out — the exact failure that
        // makes a throttle worse than none.
        var throttle = new AuthThrottle(new FixedClock());
        for (int i = 0; i < AuthThrottle.MaxFailures; i++) throttle.RecordFailure("10.0.0.1");

        Assert.Multiple(() =>
        {
            Assert.That(throttle.IsLockedOut("10.0.0.1"), Is.True);
            Assert.That(throttle.IsLockedOut("10.0.0.2"), Is.False);
        });
    }

    [Test]
    public void AuthenticatingClearsWhatCameBefore()
    {
        var throttle = new AuthThrottle(new FixedClock());
        for (int i = 0; i < AuthThrottle.MaxFailures - 1; i++) throttle.RecordFailure("10.0.0.1");

        throttle.RecordSuccess("10.0.0.1");

        // A fresh allowance, not one attempt from a lockout — a typo before a correct token should not
        // leave the operator on a hair trigger.
        Assert.That(throttle.RecordFailure("10.0.0.1"), Is.False);
    }

    [Test]
    public void CheckingForALockoutDoesNotResetTheCount()
    {
        // The listener asks IsLockedOut on every connection before it reads the token, so a read that
        // disturbed the count would mean the limit was never reached and the throttle did nothing.
        var throttle = new AuthThrottle(new FixedClock());

        for (int i = 0; i < AuthThrottle.MaxFailures - 1; i++)
        {
            throttle.IsLockedOut("10.0.0.1");
            throttle.RecordFailure("10.0.0.1");
        }
        throttle.IsLockedOut("10.0.0.1");

        Assert.That(throttle.RecordFailure("10.0.0.1"), Is.True, "the count survived the reads");
    }

    [Test]
    public void FailuresSpreadOutTooFarDoNotAccumulate()
    {
        // A wrong token once an hour is a typo, not an attack, and should never cost someone a lockout.
        var clock = new FixedClock { UtcNowUnix = 1_000 };
        var throttle = new AuthThrottle(clock);

        for (int i = 0; i < AuthThrottle.MaxFailures * 2; i++)
        {
            Assert.That(throttle.RecordFailure("10.0.0.1"), Is.False);
            clock.UtcNowUnix += AuthThrottle.LockoutSeconds + 1;
        }
    }

    [Test]
    public void FailingAgainAfterALockoutStartsAFreshAllowance()
    {
        var clock = new FixedClock { UtcNowUnix = 1_000 };
        var throttle = new AuthThrottle(clock);
        for (int i = 0; i < AuthThrottle.MaxFailures; i++) throttle.RecordFailure("10.0.0.1");

        clock.UtcNowUnix += AuthThrottle.LockoutSeconds + 1;
        throttle.IsLockedOut("10.0.0.1");   // the read is what prunes the expired record

        Assert.That(throttle.RecordFailure("10.0.0.1"), Is.False, "counting restarts, it does not resume");
    }
}

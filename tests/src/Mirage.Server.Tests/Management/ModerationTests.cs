using Mirage.Server.Core.GameLogic;
using Mirage.Shared.Protocol;
using NUnit.Framework;

namespace Mirage.Server.Tests.Management;

/// <summary>
/// The moderation report's arithmetic and its place on the wire.
///
/// <para>There are deliberately no round-trip tests for the four new packets:
/// <c>PacketSerializer</c>'s static constructor already round-trips EVERY <see cref="IPacket"/> subtype
/// under DEBUG and throws on a missing or mis-wired switch arm, which is both stronger and automatic.
/// Restating it here would only look like coverage.</para>
/// </summary>
[TestFixture]
public class ModerationTests
{
    // ── Minutes remaining ─────────────────────────────────────────────────────
    // Read by whoever is deciding whether a penalty is worth lifting, so the DIRECTION of the rounding is
    // the point: something still running must never read as over.

    [Test]
    public void SecondsRemainingRoundUpToAWholeMinute()
    {
        long in30s = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 30;

        Assert.That(ModerationSystem.MinutesLeft(in30s), Is.EqualTo(1),
            "a penalty with time left must never read as zero");
    }

    [Test]
    public void AnExactMinuteDoesNotRoundUpToTwo()
    {
        long inOneMinute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60;

        Assert.That(ModerationSystem.MinutesLeft(inOneMinute), Is.EqualTo(1));
    }

    [Test]
    public void AnHourReadsAsSixty()
    {
        long inAnHour = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600;

        Assert.That(ModerationSystem.MinutesLeft(inAnHour), Is.EqualTo(60));
    }

    [Test]
    public void AnAlreadyExpiredPenaltyStillReadsAsOne()
    {
        // Nothing should ask — the gather filters expired penalties out — but a zero or a negative here
        // would render as "0 min left" beside a Lift button that then does nothing.
        long past = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 600;

        Assert.That(ModerationSystem.MinutesLeft(past), Is.EqualTo(1));
    }

    // ── The report on the wire ────────────────────────────────────────────────

    [Test]
    public void AnEmptyReportStillCarriesItsScanCount()
    {
        // The page tells "nothing is in force" apart from "nothing has been gathered" by whether a report
        // exists at all, so an empty one has to be a real object with its count intact.
        var report = new ModerationReport { AccountsScanned = 12 };

        Assert.Multiple(() =>
        {
            Assert.That(report.Bans, Is.Empty);
            Assert.That(report.Penalties, Is.Empty);
            Assert.That(report.AccountsScanned, Is.EqualTo(12));
        });
    }

    [Test]
    public void TheTwoMachineLinePrefixesCannotBeConfused()
    {
        // Both ride one stream and are told apart by prefix alone. If either were a prefix of the other,
        // every line of the longer kind would be routed to the wrong parser.
        Assert.Multiple(() =>
        {
            Assert.That(ModerationReport.LinePrefix, Is.Not.EqualTo(ServerStatus.LinePrefix));
            Assert.That(ModerationReport.LinePrefix.StartsWith(ServerStatus.LinePrefix, StringComparison.Ordinal), Is.False);
            Assert.That(ServerStatus.LinePrefix.StartsWith(ModerationReport.LinePrefix, StringComparison.Ordinal), Is.False);
        });
    }
}

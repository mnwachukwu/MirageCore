using Microsoft.Extensions.Logging.Abstractions;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Tests.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests.Platform;

/// <summary>
/// Assertions that were impossible to write before <see cref="IClock"/> and
/// <see cref="IRandomSource"/> existed.
///
/// <para>Every deadline the server owns was read straight off <c>DateTimeOffset.UtcNow</c> at the
/// point of use, and every roll straight off <c>Random.Shared</c>. A test could observe that mail
/// eventually matures, or sample a kite direction ten thousand times and check the spread — but it
/// could not assert that a rule fires at the right moment, or that a specific roll produces a
/// specific outcome. These tests pin the clock and the rolls and assert exactly that.</para>
///
/// <para>The seams are optional trailing constructor parameters defaulting to the real
/// implementations, so nothing else in the suite had to change.</para>
/// </summary>
[TestFixture]
public class DeterminismSeamTests
{
    // ── Test doubles ──────────────────────────────────────────────────────────

    /// <summary>A clock the test moves by hand.</summary>
    sealed class FakeClock : IClock
    {
        public long UtcNowUnix { get; set; }
        public DateTime LocalNow { get; set; } = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Local);

        public void Advance(long seconds) => UtcNowUnix += seconds;
    }

    /// <summary>Replays a fixed sequence of rolls, then throws — so a test that consumes more
    /// randomness than it accounted for fails loudly instead of silently drifting onto real chance.</summary>
    sealed class ScriptedRandom : IRandomSource
    {
        private readonly int[] _ints;
        private readonly double[] _doubles;
        private int _i, _d;

        public ScriptedRandom(int[]? ints = null, double[]? doubles = null)
        {
            _ints = ints ?? [];
            _doubles = doubles ?? [];
        }

        public int IntsConsumed => _i;

        public int Next(int maxExclusive) => Take() % maxExclusive;

        public int Next(int minInclusive, int maxExclusive)
        {
            int v = Take();
            int span = maxExclusive - minInclusive;
            return minInclusive + (span <= 0 ? 0 : v % span);
        }

        public long NextInt64(long minInclusive, long maxExclusive)
        {
            long span = maxExclusive - minInclusive;
            return minInclusive + (span <= 0 ? 0 : Take() % span);
        }

        public double NextDouble()
        {
            if (_d >= _doubles.Length) throw new InvalidOperationException("ScriptedRandom ran out of doubles");
            return _doubles[_d++];
        }

        private int Take()
        {
            if (_i >= _ints.Length) throw new InvalidOperationException("ScriptedRandom ran out of ints");
            return _ints[_i++];
        }
    }

    // No-op packet dispatcher (per-file convention in this suite — each fixture declares its own; the
    // paths under test only fan out to it, never read from it).
    sealed class NoOpDispatcher : IPacketDispatcher
    {
        public void SendTo(int index, IPacket packet) { }
        public void SendToAll(IPacket packet) { }
        public void SendToAllBut(int exclude, IPacket packet) { }
        public void SendToObservers(IReadOnlyCollection<int> observers, IPacket packet) { }
        public void SendToObserversBut(IReadOnlyCollection<int> observers, int exclude, IPacket packet) { }
        public void SendToViewport(int speakerIndex, IPacket packet) { }
        public void SendToViewportAt(int mapNum, int x, int y, IPacket packet) { }
        public void SendChatBubble(int speakerIndex, IPacket packet, string senderLogin, bool wholeRegion) { }
        public void SendToAdmins(IPacket packet) { }
        public void SendToGuild(int guildId, IPacket packet) { }
        public void SendToGuildBut(int guildId, int exclude, IPacket packet) { }
        public void SendLocalizedChatToGuild(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToGuildOfficers(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatTo(int index, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAll(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAllBut(int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObservers(IReadOnlyCollection<int> observers, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObserversBut(IReadOnlyCollection<int> observers, int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewport(int speakerIndex, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewportAt(int mapNum, int x, int y, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAdmins(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendToEditor(int editorIndex, IPacket packet) { }
        public void SendToAllEditors(IPacket packet) { }
        public void Disconnect(int index) { }
        public void DisconnectEditor(int editorIndex) { }
        public void GracefulDisconnect(int index) { }
        public void GracefulDisconnectEditor(int editorIndex) { }
    }

    // ── The doubles themselves ────────────────────────────────────────────────

    [Test]
    public void FakeClock_AdvancesOnlyWhenTold()
    {
        var clock = new FakeClock { UtcNowUnix = 1_000 };
        Assert.That(clock.UtcNowUnix, Is.EqualTo(1_000));
        Assert.That(clock.UtcNowUnix, Is.EqualTo(1_000), "a pinned clock must not drift between reads");
        clock.Advance(60);
        Assert.That(clock.UtcNowUnix, Is.EqualTo(1_060));
    }

    [Test]
    public void ScriptedRandom_ThrowsRatherThanFallingBackToRealChance()
    {
        var rng = new ScriptedRandom([1]);
        Assert.That(rng.Next(10), Is.EqualTo(1));
        Assert.That(() => rng.Next(10), Throws.InvalidOperationException,
            "over-consuming the script must fail the test, not silently use real randomness");
    }

    // ── Mail: maturity is a rule, and it is now assertable ────────────────────

    static (PlayerManager pm, MailSystem mail, FakeClock clock) MailSetup()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var items = new ItemSystem(world, pm, dispatcher, persistence: null!, bg: null!);
        var saver = new PlayerSaver(persistence: null!, NullLogger<PlayerSaver>.Instance);
        var clock = new FakeClock { UtcNowUnix = 10_000 };
        var mail = new MailSystem(pm, dispatcher, saver, items, clock: clock);
        return (pm, mail, clock);
    }

    static ServerPlayer Online(PlayerManager pm, int idx, string login)
    {
        var sp = pm[idx];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Login = login;
        return sp;
    }

    // In-transit mail must refuse to claim BEFORE its DeliverAt second and accept AFTER it. Only the
    // injected clock makes both halves reachable — with a real one a test cannot sit on the boundary.
    [Test]
    public void InTransitMail_RefusesClaimBeforeDeliverAt_AndAllowsItAfter()
    {
        var (pm, mail, clock) = MailSetup();
        var sp = Online(pm, 1, "tester");

        const long deliverAt = 10_600;                        // 10 minutes out
        mail.Deliver("tester", "sender", "subject", "body",
                     [new MailAttachment { ItemNum = Constants.GoldItemIndex, Quantity = 500 }],
                     deliverAt: deliverAt);

        var msg = sp.Mail.Single();
        Assert.That(msg.DeliverAt, Is.EqualTo(deliverAt), "the delivery deadline is stamped from the caller, not the clock");

        // One second short of maturity: the attachment must still be unclaimed.
        clock.UtcNowUnix = deliverAt - 1;
        mail.Claim(1, msg.Id);
        Assert.That(msg.Attachments.Single().Claimed, Is.False,
                    "mail one second short of DeliverAt is still in transit and must not claim");

        // Exactly at maturity: claiming succeeds. The boundary is inclusive (NowUtc < DeliverAt refuses).
        clock.UtcNowUnix = deliverAt;
        mail.Claim(1, msg.Id);
        Assert.That(msg.Attachments.Single().Claimed, Is.True,
                    "at DeliverAt the mail has matured and its attachment must claim");
    }

    // Deliver() stamps TimeUtc from the clock, and DeleteAt from the maturity plus the retention
    // window — so pinning the clock pins the whole retention arithmetic.
    [Test]
    public void DeliveredMail_StampsItsTimestampsFromTheInjectedClock()
    {
        var (pm, mail, clock) = MailSetup();
        var sp = Online(pm, 1, "tester");
        clock.UtcNowUnix = 55_555;

        mail.Deliver("tester", "sender", "subject", "body");

        var msg = sp.Mail.Single();
        Assert.Multiple(() =>
        {
            Assert.That(msg.TimeUtc, Is.EqualTo(55_555), "TimeUtc comes off the injected clock");
            Assert.That(msg.DeliverAt, Is.EqualTo(55_555), "deliverAt 0 collapses to now (instant system mail)");
            Assert.That(msg.DeleteAt, Is.EqualTo(55_555 + Constants.MailRetentionSeconds),
                        "retention is measured from maturity, so it moves with the clock too");
        });
    }

    // ── Randomness: a scripted roll produces a determined outcome ─────────────

    // Weather is the cleanest roll to pin: the trigger check and the weighted pick are both single
    // draws with no other state in the way, so the outcome is determined rather than sampled.
    [Test]
    public void WeatherRoll_IsDeterminedByTheInjectedSource()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();

        // First draw decides whether the trigger hits: Next(100) < WeatherTriggerChancePercent.
        // Feed a value below the threshold, then a weighted pick that lands in the Rain band.
        var rng = new ScriptedRandom([0, 0]);
        var weather = new WeatherSystem(world, new NoOpDispatcher(), pm, rng: rng);

        var roll = typeof(WeatherSystem).GetMethod("RollTriggerHits",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var pick = typeof(WeatherSystem).GetMethod("RollWeightedWeather",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        Assert.That((bool)roll.Invoke(weather, null)!, Is.True,
                    "a scripted 0 is below the trigger threshold, so the roll must hit");
        Assert.That((WeatherType)pick.Invoke(weather, null)!, Is.EqualTo(WeatherType.Rain),
                    "a scripted 0 lands in the first weight band, which is Rain");
        Assert.That(rng.IntsConsumed, Is.EqualTo(2), "exactly two draws — no hidden extra randomness");
    }

    // The same roll, scripted differently, must produce a different weather — otherwise the test above
    // would pass against a hard-coded return value.
    [Test]
    public void WeightedWeatherPick_FollowsTheScriptedBand()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        int total = Constants.WeatherWeightRain + Constants.WeatherWeightHeatWave
                    + Constants.WeatherWeightSnow + Constants.WeatherWeightHeavyWind;

        // A draw past every band but the last must select HeavyWind (the fallthrough).
        var rng = new ScriptedRandom([total - 1]);
        var weather = new WeatherSystem(world, new NoOpDispatcher(), pm, rng: rng);
        var pick = typeof(WeatherSystem).GetMethod("RollWeightedWeather",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        Assert.That((WeatherType)pick.Invoke(weather, null)!, Is.EqualTo(WeatherType.HeavyWind),
                    "the top of the weight range falls through to the last band");
    }

    // ── The defaults must preserve production behavior ───────────────────────

    // A system built without the seams must still work off the real clock and real randomness — this is
    // what makes the change behavior-preserving for every existing construction site.
    [Test]
    public void OmittingTheSeams_FallsBackToTheRealImplementations()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var items = new ItemSystem(world, pm, dispatcher, persistence: null!, bg: null!);
        var saver = new PlayerSaver(persistence: null!, NullLogger<PlayerSaver>.Instance);

        // No clock argument: MailSystem seeds its sweep watermark from the machine clock.
        var mail = new MailSystem(pm, dispatcher, saver, items);
        Online(pm, 1, "tester");
        mail.Deliver("tester", "sender", "s", "b");

        long stamped = pm[1].Mail.Single().TimeUtc;
        long realNow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Assert.That(stamped, Is.EqualTo(realNow).Within(60),
                    "with no injected clock the stamp must come from the machine clock");
    }

    [Test]
    public void SystemClockAndSharedRandom_BehaveAsTheirUnderlyingSources()
    {
        Assert.That(SystemClock.Instance.UtcNowUnix,
                    Is.EqualTo(DateTimeOffset.UtcNow.ToUnixTimeSeconds()).Within(2));
        Assert.That(SystemClock.Instance.LocalNow.Date, Is.EqualTo(DateTime.Now.Date));

        for (int i = 0; i < 200; i++)
        {
            Assert.That(SharedRandom.Instance.Next(10), Is.InRange(0, 9));
            Assert.That(SharedRandom.Instance.Next(5, 8), Is.InRange(5, 7));
            Assert.That(SharedRandom.Instance.NextDouble(), Is.InRange(0.0, 1.0));
            Assert.That(SharedRandom.Instance.NextInt64(100, 200), Is.InRange(100L, 199L));
        }
    }
}

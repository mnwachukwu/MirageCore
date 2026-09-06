using Mirage.Client.Core.Logic;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.Rendering;

/// <summary>
/// The frame-time distribution, which exists because an average cannot see a stutter.
///
/// <para>These pin the property the whole thing is for: one bad frame in a hundred good ones has to show up
/// somewhere a reader will look. If the 99th and the worst can be swallowed by the median, the instrument is
/// no better than the frame counter it was built to replace.</para>
/// </summary>
[TestFixture]
public class FrameMetricsTests
{
    private static FrameMetrics Steady(int frames, double ms)
    {
        var m = new FrameMetrics();
        for (int i = 0; i < frames; i++) m.Record(ms, ms * 0.4, ms * 0.6, 1, false);
        return m;
    }

    [Test]
    public void WithNothingRecorded_ItSaysSoRatherThanReportingZeroes()
    {
        Assert.That(new FrameMetrics().Take().Frames, Is.Zero);
    }

    [Test]
    public void ASteadyFrameRate_ReadsTheSameAtEveryPercentile()
    {
        var s = Steady(300, 16.0).Take();

        Assert.Multiple(() =>
        {
            Assert.That(s.Frames, Is.EqualTo(300));
            Assert.That(s.Frame.P50, Is.EqualTo(16.0).Within(0.001));
            Assert.That(s.Frame.P99, Is.EqualTo(16.0).Within(0.001));
            Assert.That(s.Frame.Max, Is.EqualTo(16.0).Within(0.001));
        });
    }

    /// <summary>The case the instrument exists for: a few frames are terrible and the rest are fine. An
    /// average would read 17.6 ms here — a number nobody would look twice at.</summary>
    [Test]
    public void ABadFrameEveryFiftieth_ShowsAtThe99thAndTheMax_ButNotTheMedian()
    {
        var m = new FrameMetrics();
        for (int i = 0; i < 600; i++) m.Record(i % 50 == 0 ? 180.0 : 16.0, 8, 8, 1, false);
        var s = m.Take();

        Assert.Multiple(() =>
        {
            Assert.That(s.Frame.P50, Is.EqualTo(16.0).Within(0.001), "the median still says it runs fine");
            Assert.That(s.Frame.P99, Is.EqualTo(180.0).Within(0.001), "the 99th catches it");
            Assert.That(s.Frame.Max, Is.EqualTo(180.0).Within(0.001));
        });
    }

    /// <summary>And why the WORST is reported next to the 99th rather than instead of it. At exactly one bad
    /// frame in a hundred the bad ones fill the top percentile exactly, so a nearest-rank 99th lands on the
    /// last good sample and reads clean. A hitch a player would describe as "every couple of seconds" sits
    /// right about there, and max is the only band that still sees it.</summary>
    [Test]
    public void AtExactlyOneInAHundred_OnlyTheMaxSeesIt()
    {
        var m = new FrameMetrics();
        for (int i = 0; i < 600; i++) m.Record(i % 100 == 0 ? 180.0 : 16.0, 8, 8, 1, false);
        var s = m.Take();

        Assert.Multiple(() =>
        {
            Assert.That(s.Frame.P99, Is.EqualTo(16.0).Within(0.001));
            Assert.That(s.Frame.Max, Is.EqualTo(180.0).Within(0.001), "which is what max is for");
        });
    }

    [Test]
    public void TheWindowRolls_SoTheNumbersDescribeNow()
    {
        var m = Steady(FrameMetrics.Window, 100.0);
        for (int i = 0; i < FrameMetrics.Window; i++) m.Record(16.0, 8, 8, 1, false);
        var s = m.Take();

        Assert.Multiple(() =>
        {
            Assert.That(s.Frames, Is.EqualTo(FrameMetrics.Window), "the band is over a full window");
            Assert.That(s.Frame.Max, Is.EqualTo(16.0).Within(0.001), "and the bad stretch has rolled off it");
        });
    }

    /// <summary>The distinction the readout lives or dies on: a WINDOWED count says whether something is
    /// happening, a TOTAL says only that it once did. Reporting the total alone lets a burst at startup keep
    /// a counter lit for the rest of the session, so a reader watching it climb cannot tell an ongoing
    /// problem from a remembered one.</summary>
    [Test]
    public void CatchUpAndSlowFrames_AreWindowedAndTotalled_Separately()
    {
        var m = new FrameMetrics();
        m.Record(16, 8, 8, 1, false);
        m.Record(50, 30, 20, 3, true);     // one overrun: two extra Updates
        m.Record(40, 20, 20, 2, true);     // and another: one more
        var during = m.Take();

        for (int i = 0; i < FrameMetrics.Window; i++) m.Record(16, 8, 8, 1, false);
        var after = m.Take();

        Assert.Multiple(() =>
        {
            Assert.That(during.CatchUpFrames, Is.EqualTo(2), "while it is happening, the window says so");
            Assert.That(during.ExtraUpdates, Is.EqualTo(3));
            Assert.That(during.SlowFrames, Is.EqualTo(2));

            Assert.That(after.CatchUpFrames, Is.Zero, "once it has passed, the window says that too");
            Assert.That(after.SlowFrames, Is.Zero);
            Assert.That(after.TotalCatchUpFrames, Is.EqualTo(2), "but the total still remembers");
            Assert.That(after.TotalExtraUpdates, Is.EqualTo(3));
            Assert.That(after.TotalSlowFrames, Is.EqualTo(2));
        });
    }

    /// <summary>A spike's cause belongs to one frame, so the worst frame is kept whole rather than averaged.
    /// Whether the collector ran during it is the difference between "the draw did that much work" and
    /// "something stopped the world".</summary>
    [Test]
    public void TheWorstFrame_IsKeptWholeWithWhatWasHappeningInIt()
    {
        var m = new FrameMetrics();
        for (int i = 0; i < 100; i++) m.Record(16, 2, 14, 1, false);
        m.Record(249.3, 0.9, 12.0, 15, true, gen0: 1, gen1: 1, gen2: 1);
        for (int i = 0; i < 100; i++) m.Record(16, 2, 14, 1, false);
        var s = m.Take();

        Assert.Multiple(() =>
        {
            Assert.That(s.Worst.TotalMs, Is.EqualTo(249.3).Within(0.001));
            Assert.That(s.Worst.DrawMs, Is.EqualTo(12.0).Within(0.001),
                "the draw was ordinary, so the quarter-second went somewhere else");
            Assert.That(s.Worst.Gen2, Is.EqualTo(1), "and this is where it went");
            Assert.That(s.Gen2, Is.EqualTo(1), "counted into the session total as well");
        });
    }

    [Test]
    public void UpdateAndDraw_AreBandedSeparately_SoTheHalfAtFaultIsNamed()
    {
        var m = new FrameMetrics();
        for (int i = 0; i < 200; i++) m.Record(20.0, 2.0, 18.0, 1, false);
        var s = m.Take();

        Assert.Multiple(() =>
        {
            Assert.That(s.Update.P50, Is.EqualTo(2.0).Within(0.001));
            Assert.That(s.Draw.P50, Is.EqualTo(18.0).Within(0.001));
        });
    }

    /// <summary>An instrument loud enough to appear in its own reading is measuring itself. Sorting three
    /// six-hundred-element windows per snapshot was around a megabyte a second of churn while the readout
    /// was on screen — which then showed up in the allocation rate it exists to report.</summary>
    [Test]
    public void TakingASnapshot_DoesNotAllocate()
    {
        var m = Steady(FrameMetrics.Window, 16.0);
        m.Take();   // first call settles anything lazily built

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) m.Take();
        long per = (GC.GetAllocatedBytesForCurrentThread() - before) / 100;

        Assert.That(per, Is.LessThan(64), $"a snapshot allocated {per} B");
    }

    [Test]
    public void Reset_ClearsTheCountersAndTheWindow()
    {
        var m = Steady(100, 33.0);
        m.Record(200, 100, 100, 4, true);
        m.Reset();

        var s = m.Take();
        Assert.Multiple(() =>
        {
            Assert.That(s.Frames, Is.Zero);
            Assert.That(s.CatchUpFrames, Is.Zero);
            Assert.That(s.SlowFrames, Is.Zero);
        });
    }
}

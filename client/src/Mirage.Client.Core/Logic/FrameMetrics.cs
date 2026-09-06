namespace Mirage.Client.Core.Logic;

/// <summary>
/// What a frame actually cost, over a rolling window.
///
/// <para>An average frame rate cannot see a stutter. A frame that takes 100 ms once a second moves a 60 fps
/// average to 58 — a number nobody would investigate — while being exactly the thing a player feels. So this
/// keeps the DISTRIBUTION: the median says how it runs, and the 99th and the worst say how it hitches.</para>
///
/// <para>Counters come in two flavours, and the difference is the whole point of having them. A WINDOWED
/// count answers "is this happening now"; a TOTAL answers "has this ever happened". Reporting only the
/// total means a burst at startup leaves the readout lit for the rest of the session and nothing that
/// happens afterwards changes it.</para>
///
/// <para>The worst frame in the window is kept whole — its halves, and whether the collector ran during it —
/// because a spike's cause is a property of that one frame and averages cannot hold it.</para>
///
/// <para>Recording is a handful of array writes; the sorting only happens when somebody asks.</para>
/// </summary>
public sealed class FrameMetrics
{
    /// <summary>Frames held. Ten seconds at 60 fps — long enough to catch an intermittent hitch, short
    /// enough that the numbers still describe what is happening now rather than a minute ago.</summary>
    public const int Window = 600;

    private readonly double[] _frameMs = new double[Window];
    private readonly double[] _updateMs = new double[Window];
    private readonly double[] _drawMs = new double[Window];
    private readonly double[] _lightMs = new double[Window];
    private readonly int[] _updates = new int[Window];
    private readonly bool[] _slow = new bool[Window];
    private readonly long[] _bytes = new long[Window];
    private readonly int[] _gen0 = new int[Window];
    private readonly int[] _gen1 = new int[Window];
    private readonly int[] _gen2 = new int[Window];
    private int _next;

    /// <summary>Frames recorded since the last <see cref="Reset"/>.</summary>
    public int Recorded { get; private set; }

    /// <summary>Frames that took more than one Update, for the whole session — the fixed timestep catching
    /// up after it fell behind.</summary>
    public int TotalCatchUpFrames { get; private set; }

    /// <summary>Updates beyond the first, totalled for the whole session.</summary>
    public int TotalExtraUpdates { get; private set; }

    /// <summary>Frames the host flagged as running slowly, for the whole session.</summary>
    public int TotalSlowFrames { get; private set; }

    /// <summary>Collections seen since the last <see cref="Reset"/>, by generation.</summary>
    public int Gen0 { get; private set; }
    public int Gen1 { get; private set; }
    public int Gen2 { get; private set; }

    /// <summary>One frame, whole. <paramref name="Collections"/> is how many collections ran during it, by
    /// generation — a spike that coincides with a gen-2 has named itself.</summary>
    public readonly record struct Frame(
        double TotalMs, double UpdateMs, double DrawMs, int Updates, bool Slow,
        int Gen0, int Gen1, int Gen2);

    private Frame _worst;

    /// <summary>Records one presented frame. <paramref name="updates"/> is how many times Update ran for it;
    /// the generation counts are collections that ran DURING it, not totals.</summary>
    public void Record(double frameMs, double updateMs, double drawMs, int updates, bool runningSlowly,
                       int gen0 = 0, int gen1 = 0, int gen2 = 0, long bytes = 0, double lightMs = 0)
    {
        int i = _next;
        _frameMs[i] = frameMs;
        _updateMs[i] = updateMs;
        _drawMs[i] = drawMs;
        _lightMs[i] = lightMs;
        _updates[i] = updates;
        _slow[i] = runningSlowly;
        _bytes[i] = bytes;
        _gen0[i] = gen0;
        _gen1[i] = gen1;
        _gen2[i] = gen2;
        _next = i + 1 == Window ? 0 : i + 1;
        if (Recorded < int.MaxValue) Recorded++;

        if (updates > 1) { TotalCatchUpFrames++; TotalExtraUpdates += updates - 1; }
        if (runningSlowly) TotalSlowFrames++;
        Gen0 += gen0;
        Gen1 += gen1;
        Gen2 += gen2;
    }

    public void Reset()
    {
        Array.Clear(_frameMs);
        Array.Clear(_updateMs);
        Array.Clear(_drawMs);
        Array.Clear(_lightMs);
        Array.Clear(_updates);
        Array.Clear(_slow);
        Array.Clear(_bytes);
        Array.Clear(_gen0);
        Array.Clear(_gen1);
        Array.Clear(_gen2);
        _next = 0;
        Recorded = 0;
        TotalCatchUpFrames = 0;
        TotalExtraUpdates = 0;
        TotalSlowFrames = 0;
        Gen0 = Gen1 = Gen2 = 0;
        _worst = default;
    }

    /// <summary>One band of the distribution, in milliseconds.</summary>
    public readonly record struct Band(double P50, double P99, double Max);

    /// <summary><paramref name="Frames"/> is how many the bands are over — fewer than <see cref="Window"/>
    /// until it has filled once. The CatchUp and Slow pairs are windowed first, total second.</summary>
    public readonly record struct Snapshot(
        int Frames, Band Frame, Band Update, Band Draw,
        Band Light,
        int CatchUpFrames, int TotalCatchUpFrames,
        int ExtraUpdates, int TotalExtraUpdates,
        int SlowFrames, int TotalSlowFrames,
        int WindowGen0, int WindowGen2, double KbPerSecond,
        int Gen0, int Gen1, int Gen2,
        Frame Worst);

    public Snapshot Take()
    {
        int n = Math.Min(Recorded, Window);
        if (n == 0) return default;

        int catchUp = 0, extra = 0, slow = 0, g0 = 0, g2 = 0, worst = 0;
        long bytes = 0;
        double ms = 0;
        for (int i = 0; i < n; i++)
        {
            if (_updates[i] > 1) { catchUp++; extra += _updates[i] - 1; }
            if (_slow[i]) slow++;
            g0 += _gen0[i];
            g2 += _gen2[i];
            bytes += _bytes[i];
            ms += _frameMs[i];
            if (_frameMs[i] > _frameMs[worst]) worst = i;
        }

        // The worst frame IN THE WINDOW, matching the bands beside it. An all-time worst would keep
        // reporting whatever startup did for the rest of the session, which describes a spike nobody can
        // still reproduce and hides the one that just happened.
        _worst = new Frame(_frameMs[worst], _updateMs[worst], _drawMs[worst], _updates[worst],
                           _slow[worst], _gen0[worst], _gen1[worst], _gen2[worst]);

        // Per SECOND rather than per frame: allocation is a rate against the collector's budget, and a frame
        // is not a fixed amount of time — the same churn reads differently at 30 fps and at 144.
        double kbPerSec = ms > 0 ? bytes / 1024.0 / (ms / 1000.0) : 0;

        return new Snapshot(n, BandOf(_frameMs, n), BandOf(_updateMs, n), BandOf(_drawMs, n),
            BandOf(_lightMs, n),
            catchUp, TotalCatchUpFrames, extra, TotalExtraUpdates, slow, TotalSlowFrames,
            g0, g2, kbPerSec, Gen0, Gen1, Gen2, _worst);
    }

    // Sorting needs somewhere to sort, and a snapshot taken every frame allocating three of these is an
    // instrument loud enough to show up in its own reading. One buffer, reused, used up before the next.
    private readonly double[] _sortScratch = new double[Window];

    private Band BandOf(double[] samples, int n)
    {
        Array.Copy(samples, _sortScratch, n);
        Array.Sort(_sortScratch, 0, n);
        return new Band(At(_sortScratch, n, 0.50), At(_sortScratch, n, 0.99), _sortScratch[n - 1]);
    }

    // Nearest-rank: the smallest sample at or above the given share of the window. No interpolation — with
    // 600 samples the neighbours are close enough that averaging them only makes the number harder to relate
    // back to a frame that actually happened.
    private static double At(double[] sorted, int n, double q) =>
        sorted[Math.Clamp((int)Math.Ceiling(q * n) - 1, 0, n - 1)];
}

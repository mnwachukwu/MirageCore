using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Server.Core.Configuration;
using Mirage.Server.Shell.Bench;
using Mirage.Server.Shell.Localization;

namespace Mirage.Server.Shell.ViewModels;

/// <summary>
/// The load benchmark's dialog: a target, a run, and the table it produces.
///
/// <para>The headroom rows are the point of the whole exercise. An operator is not really asking "what
/// is the maximum" — they are asking how much of their machine they are willing to hand over, and each
/// row answers that question at one price.</para>
/// </summary>
public sealed partial class BenchViewModel : ObservableObject
{
    /// <summary>Lines of the scratch server's console kept for diagnosis. Small: this is here for the
    /// case where the child never boots, not as a second console.</summary>
    private const int MaxLogLines = 400;

    private readonly Func<ServerConfig> _readConfig;
    private readonly Action<int> _applyLimit;
    private readonly System.Text.StringBuilder _log = new();
    private int _logLines;
    private CancellationTokenSource? _cts;

    public BenchViewModel(Func<ServerConfig> readConfig, Action<int> applyLimit)
    {
        _readConfig = readConfig;
        _applyLimit = applyLimit;
    }

    // ── Chrome ────────────────────────────────────────────────────────────────

    public string Title => ShellStrings.Get(ShellStrings.Bench_Title);
    public string Blurb => ShellStrings.Get(ShellStrings.Bench_Blurb);
    public string Caveat => ShellStrings.Get(ShellStrings.Bench_Caveat);
    public string Warning => ShellStrings.Get(ShellStrings.Bench_Warning);
    public string TargetLabel => ShellStrings.Get(ShellStrings.Bench_Target);
    public string RunLabel => ShellStrings.Get(ShellStrings.Bench_Run);
    public string StopLabel => ShellStrings.Get(ShellStrings.Bench_Stop);
    public string CloseLabel => ShellStrings.Get(ShellStrings.Bench_Close);
    public string ApplyLabel => ShellStrings.Get(ShellStrings.Bench_Apply);
    public string BandsHeading => ShellStrings.Get(ShellStrings.Bench_Bands);
    public string StepsHeading => ShellStrings.Get(ShellStrings.Bench_Steps);
    public string ColPlayers => ShellStrings.Get(ShellStrings.Bench_ColPlayers);
    public string ColGameThread => ShellStrings.Get(ShellStrings.Bench_ColGameThread);
    public string ColCpu => ShellStrings.Get(ShellStrings.Bench_ColCpu);
    public string ColMemory => ShellStrings.Get(ShellStrings.Bench_ColMemory);
    public string ColOverruns => ShellStrings.Get(ShellStrings.Bench_ColOverruns);
    public string ColPackets => ShellStrings.Get(ShellStrings.Bench_ColPackets);

    /// <summary>The protocol ceiling, so the spinner cannot ask for a number a shipped client could not
    /// index even if the machine managed it.</summary>
    public decimal MaxTarget => Mirage.Shared.Constants.MaxPlayers;

    [ObservableProperty]
    public partial decimal Target { get; set; } = LoadBenchmark.DefaultTarget;

    // ── Run state ─────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditTarget))]
    public partial bool IsRunning { get; private set; }

    public bool CanEditTarget => !IsRunning;

    [ObservableProperty]
    public partial string PhaseText { get; private set; } = "";

    [ObservableProperty]
    public partial string ProgressText { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorText { get; private set; } = "";

    public bool HasError => ErrorText.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSummary))]
    public partial string SummaryText { get; private set; } = "";

    public bool HasSummary => SummaryText.Length > 0;

    /// <summary>The machine's own numbers: cores, the empty-world footprint, and what a player added to
    /// it. Separate from the summary because they describe the box rather than the run.</summary>
    [ObservableProperty]
    public partial string CoresText { get; private set; } = "";

    [ObservableProperty]
    public partial string BaselineText { get; private set; } = "";

    [ObservableProperty]
    public partial string PerPlayerText { get; private set; } = "";

    /// <summary>Non-empty only when the bench itself fell behind, which makes the whole run a lower
    /// bound. Said out loud rather than folded into the numbers.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMissedBeats))]
    public partial string MissedBeatsText { get; private set; } = "";

    public bool HasMissedBeats => MissedBeatsText.Length > 0;

    public string LogText => _log.ToString();

    public System.Collections.ObjectModel.ObservableCollection<BenchBandRow> Bands { get; } = [];
    public System.Collections.ObjectModel.ObservableCollection<BenchStepRow> Steps { get; } = [];

    public bool HasBands => Bands.Count > 0;
    public bool HasSteps => Steps.Count > 0;

    // ── Running it ────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task RunAsync()
    {
        if (IsRunning) return;
        Reset();
        IsRunning = true;
        _cts = new CancellationTokenSource();

        // Constructed here so its callbacks come back on the UI thread; the ramp runs on a pool thread.
        var progress = new Progress<BenchProgress>(OnProgress);
        try
        {
            var report = await new LoadBenchmark()
                .RunAsync(_readConfig(), (int)Target, progress, Append, _cts.Token)
                .ConfigureAwait(true);
            Show(report);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException
                                      or UnauthorizedAccessException or System.Net.Sockets.SocketException)
        {
            ErrorText = ShellStrings.Format(ShellStrings.Bench_Failed, ("Error", ex.Message));
        }
        finally
        {
            IsRunning = false;
            PhaseText = "";
            ProgressText = "";
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>Stops the ramp. What has already been measured is kept — the steps that ran are real
    /// whether or not the run finished.</summary>
    [RelayCommand]
    private void Stop() => _cts?.Cancel();

    /// <summary>Takes a row's number back to the Configuration tab. Written into the form rather than the
    /// file, so it goes through the same Save the operator uses for everything else.</summary>
    [RelayCommand]
    private void UseBand(BenchBandRow? row)
    {
        if (row is null) return;
        _applyLimit(row.Players);
        SummaryText = ShellStrings.Format(ShellStrings.Bench_Applied, ("Players", row.Players));
    }

    private void Reset()
    {
        ErrorText = "";
        SummaryText = "";
        CoresText = "";
        BaselineText = "";
        PerPlayerText = "";
        MissedBeatsText = "";
        Bands.Clear();
        Steps.Clear();
        _log.Clear();
        _logLines = 0;
        OnPropertyChanged(nameof(LogText));
        OnPropertyChanged(nameof(HasBands));
        OnPropertyChanged(nameof(HasSteps));
    }

    private void OnProgress(BenchProgress p)
    {
        PhaseText = ShellStrings.Get(p.Phase switch
        {
            BenchPhase.Preparing => ShellStrings.Bench_Preparing,
            BenchPhase.Booting => ShellStrings.Bench_Booting,
            BenchPhase.Joining => ShellStrings.Bench_Joining,
            BenchPhase.Measuring => ShellStrings.Bench_Measuring,
            _ => ShellStrings.Bench_Finishing,
        });
        ProgressText = ShellStrings.Format(ShellStrings.Bench_Progress,
            ("Players", p.Players), ("Target", p.Target));

        // Rows appear as they are measured. A benchmark that shows nothing for several minutes is one an
        // operator kills before it finishes.
        if (p.Step is not { } step) return;
        Steps.Add(new BenchStepRow(
            step.Players.ToString(),
            Percent(step.GameThread),
            Percent(step.ProcessCpu),
            Bytes(step.WorkingSetBytes),
            step.Overruns.ToString(),
            $"{step.PacketsPerSecond:0}"));
        OnPropertyChanged(nameof(HasSteps));
    }

    private void Show(BenchReport report)
    {
        SummaryText = report.Outcome switch
        {
            BenchOutcome.Reached => ShellStrings.Format(ShellStrings.Bench_Reached, ("Peak", report.Peak)),
            BenchOutcome.ThreadSaturated => ShellStrings.Format(ShellStrings.Bench_Saturated, ("Peak", report.Peak)),
            BenchOutcome.JoinsFailed => ShellStrings.Format(ShellStrings.Bench_JoinsFailed,
                ("Peak", report.Peak), ("Reason", report.FailureReason ?? "")),
            BenchOutcome.PlayersDropped => ShellStrings.Format(ShellStrings.Bench_Dropped, ("Peak", report.Peak)),
            _ => ShellStrings.Get(ShellStrings.Bench_Cancelled),
        };

        CoresText = ShellStrings.Format(ShellStrings.Bench_Cores, ("Cores", report.ProcessorCount));
        BaselineText = ShellStrings.Format(ShellStrings.Bench_Baseline, ("Memory", Bytes(report.BaselineBytes)));

        // Per player is the DIFFERENCE from the empty world. The world is most of the footprint, so the
        // figure at a step says almost nothing about what another player would cost.
        var peak = report.Steps.Count == 0 ? null : report.Steps[^1];
        PerPlayerText = peak is { Players: > 0 } && report.BaselineBytes > 0
            ? ShellStrings.Format(ShellStrings.Bench_PerPlayer,
                ("Memory", Bytes(Math.Max(0, peak.WorkingSetBytes - report.BaselineBytes) / peak.Players)))
            : "";

        MissedBeatsText = report.MissedBeats > 0
            ? ShellStrings.Format(ShellStrings.Bench_MissedBeats, ("Count", report.MissedBeats))
            : "";

        Bands.Clear();
        foreach (var band in report.Bands)
        {
            Bands.Add(new BenchBandRow(
                ShellStrings.Format(ShellStrings.Bench_BandLabel, ("Percent", (int)Math.Round(band.Headroom * 100))),
                band.AtLeast
                    ? ShellStrings.Format(ShellStrings.Bench_AtLeast, ("Players", band.Players))
                    : band.Players.ToString(),
                Bytes(band.WorkingSetBytes),
                band.Players));
        }
        OnPropertyChanged(nameof(HasBands));
    }

    private void Append(string line) => Dispatcher.UIThread.Post(() =>
    {
        _log.Append(line).Append('\n');
        if (++_logLines > MaxLogLines)
        {
            int firstBreak = -1;
            for (int i = 0; i < _log.Length; i++)
                if (_log[i] == '\n') { firstBreak = i; break; }
            if (firstBreak >= 0) { _log.Remove(0, firstBreak + 1); _logLines--; }
        }
        OnPropertyChanged(nameof(LogText));
    });

    private static string Percent(double fraction) => $"{fraction * 100:0}%";

    private static string Bytes(long value) => value >= 1L << 30
        ? $"{value / (double)(1L << 30):0.0} GB"
        : $"{value / (double)(1L << 20):0} MB";
}

/// <summary>One headroom row: how many players fit while leaving that much of the game thread spare, and
/// the button that adopts the number.</summary>
public sealed record BenchBandRow(string Label, string PlayersText, string Memory, int Players);

/// <summary>One ramp step, formatted. Strings rather than numbers so the table has no converters in it.</summary>
public sealed record BenchStepRow(
    string Players,
    string GameThread,
    string Cpu,
    string Memory,
    string Overruns,
    string Packets);

namespace Mirage.Editor;

/// <summary>How much detail the log file captures. Ordered least to most, matching Serilog's own
/// ordering so the enum maps straight onto a level switch.</summary>
public enum LogCaptureLevel
{
    /// <summary>Failures only.</summary>
    Error,
    /// <summary>Failures and anything that degraded but carried on.</summary>
    Warning,
    /// <summary>The default. Every action worth reconstructing a session from: connects, loads, saves,
    /// navigation, dialogs opened, records written.</summary>
    Information,
    /// <summary>Adds per-packet traffic, command dispatch, and the UI-thread stall watchdog's detail.</summary>
    Debug,
    /// <summary>Everything, including per-tile and per-frame chatter. Large files.</summary>
    Verbose,
}

/// <summary>How long log files are kept. Rolling is daily, so each value is a file count.</summary>
public enum LogRetention
{
    ThreeDays,
    SevenDays,
    FourteenDays,
    ThirtyDays,
    Forever,
}

/// <summary>The editor's file-logging configuration.</summary>
public sealed class LoggingSetting
{
    public LogCaptureLevel Level { get; set; } = LogCaptureLevel.Information;

    public LogRetention Retention { get; set; } = LogRetention.ThreeDays;

    /// <summary>Files kept by the daily-rolling sink, or null for "keep everything".</summary>
    public int? RetainedFileCount => Retention switch
    {
        LogRetention.ThreeDays => 3,
        LogRetention.SevenDays => 7,
        LogRetention.FourteenDays => 14,
        LogRetention.ThirtyDays => 30,
        _ => null,
    };

    /// <summary>The levels the configuration window offers, least detail first.</summary>
    public static readonly LogCaptureLevel[] Levels =
        [LogCaptureLevel.Error, LogCaptureLevel.Warning, LogCaptureLevel.Information,
         LogCaptureLevel.Debug, LogCaptureLevel.Verbose];

    /// <summary>The retentions the configuration window offers, shortest first.</summary>
    public static readonly LogRetention[] Retentions =
        [LogRetention.ThreeDays, LogRetention.SevenDays, LogRetention.FourteenDays,
         LogRetention.ThirtyDays, LogRetention.Forever];

    public LoggingSetting Clone() => new() { Level = Level, Retention = Retention };
}

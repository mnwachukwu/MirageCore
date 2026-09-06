using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Editor.Services;

namespace Mirage.Editor.ViewModels;

/// <summary>One capture level the combo offers, with its own caption and the one-line summary shown
/// underneath so the cost of each level is stated before it is chosen.</summary>
public sealed class LogLevelOption(LogCaptureLevel level, string labelKey, string detailKey)
{
    public LogCaptureLevel Level { get; } = level;
    public string Label => EditorStrings.Get(labelKey);
    public string Detail => EditorStrings.Get(detailKey);
}

/// <summary>One retention the combo offers.</summary>
public sealed class LogRetentionOption(LogRetention retention, string labelKey)
{
    public LogRetention Retention { get; } = retention;
    public string Label => EditorStrings.Get(labelKey);
}

/// <summary>Edits a COPY of the stored logging setting: the window writes back only when confirmed, so
/// backing out leaves the log exactly as it was.</summary>
public sealed partial class LoggingDialogViewModel : ObservableObject
{
    public event Action? Confirmed;
    public event Action? Canceled;

    /// <summary>Set by the view so the folder button can hand the path to the OS file manager.</summary>
    public Func<string, Task>? RevealFolderAsync { get; set; }

    [ObservableProperty] private LogLevelOption _level;
    [ObservableProperty] private LogRetentionOption _retention;

    public IReadOnlyList<LogLevelOption> Levels { get; }
    public IReadOnlyList<LogRetentionOption> Retentions { get; }

    /// <summary>Where the files are, shown verbatim so it can be copied out of the window.</summary>
    public string LogDirectory => EditorLog.Directory;

    public string LevelDetail => Level.Detail;

    public LoggingDialogViewModel(LoggingSetting current)
    {
        Levels =
        [
            new(LogCaptureLevel.Error, EditorStrings.Logging_LevelError, EditorStrings.Logging_LevelErrorDetail),
            new(LogCaptureLevel.Warning, EditorStrings.Logging_LevelWarning, EditorStrings.Logging_LevelWarningDetail),
            new(LogCaptureLevel.Information, EditorStrings.Logging_LevelInformation, EditorStrings.Logging_LevelInformationDetail),
            new(LogCaptureLevel.Debug, EditorStrings.Logging_LevelDebug, EditorStrings.Logging_LevelDebugDetail),
            new(LogCaptureLevel.Verbose, EditorStrings.Logging_LevelVerbose, EditorStrings.Logging_LevelVerboseDetail),
        ];
        Retentions =
        [
            new(LogRetention.ThreeDays, EditorStrings.Logging_Retain3),
            new(LogRetention.SevenDays, EditorStrings.Logging_Retain7),
            new(LogRetention.FourteenDays, EditorStrings.Logging_Retain14),
            new(LogRetention.ThirtyDays, EditorStrings.Logging_Retain30),
            new(LogRetention.Forever, EditorStrings.Logging_RetainForever),
        ];
        _level = Levels.First(l => l.Level == current.Level);
        _retention = Retentions.First(r => r.Retention == current.Retention);
    }

    partial void OnLevelChanged(LogLevelOption value) => OnPropertyChanged(nameof(LevelDetail));

    public LoggingSetting ToSetting() => new() { Level = Level.Level, Retention = Retention.Retention };

    [RelayCommand]
    private void Confirm() => Confirmed?.Invoke();

    [RelayCommand]
    private void Cancel() => Canceled?.Invoke();

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        if (RevealFolderAsync is null) return;
        Directory.CreateDirectory(EditorLog.Directory);
        await RevealFolderAsync(EditorLog.Directory);
    }
}

using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Editor.Services;
using Serilog.Events;
using System.Collections.ObjectModel;

namespace Mirage.Editor.ViewModels;

/// <summary>One line in the console, already colored by severity.</summary>
public sealed class ConsoleLineViewModel
{
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x6C, 0x6C));
    private static readonly IBrush WarnBrush = new SolidColorBrush(Color.FromRgb(0xD9, 0xA4, 0x41));
    private static readonly IBrush QuietBrush = new SolidColorBrush(Color.FromRgb(0x8A, 0x8F, 0x9A));

    public ConsoleLineViewModel(ConsoleSink.Line line)
    {
        Text = $"{line.At.LocalDateTime:HH:mm:ss.fff}  {line.Text}";
        Level = line.Level;
        Brush = line.Level switch
        {
            LogEventLevel.Fatal or LogEventLevel.Error => ErrorBrush,
            LogEventLevel.Warning => WarnBrush,
            LogEventLevel.Debug or LogEventLevel.Verbose => QuietBrush,
            _ => Brushes.Gainsboro,
        };
    }

    public string Text { get; }
    public LogEventLevel Level { get; }
    public IBrush Brush { get; }
}

/// <summary>
/// The editor's log, on screen. A third modeless window beside World Preview and Layer Visibility, and
/// the same deal as the client's console: it displays and nothing else.
///
/// <para>It reads <see cref="EditorLog.Console"/>, which has been filling since the editor started, so
/// opening the window shows the history rather than starting a recording. The backlog lands once at
/// construction and everything after it arrives through the sink's event.</para>
///
/// <para>How much detail there is to see is <b>Help &gt; Logging</b>'s business, not this window's: the
/// level switch governs both the file and this sink, so raising it to Debug fills both without a restart.
/// Offering a second level control here would let the window disagree with the file it is supposed to be
/// showing.</para>
///
/// <para>Clear empties the view only. The file is the record and is never touched from here.</para>
/// </summary>
public sealed partial class ConsoleViewModel : ObservableObject, IDisposable
{
    /// <summary>Lines held in the view. The sink keeps more; this is what a scrollback can render without
    /// the list virtualizer having to work for it.</summary>
    private const int MaxRows = 1000;

    public ConsoleViewModel()
    {
        foreach (var line in EditorLog.Console.Snapshot()) Lines.Add(new ConsoleLineViewModel(line));

        EditorLog.Console.Written += OnWritten;
        // Modeless and long-lived, so it outlives a language switch and has to re-read its own captions
        // rather than resolving them once at construction the way a dialog does.
        EditorStrings.LanguageChanged += OnLanguageChanged;
    }

    public ObservableCollection<ConsoleLineViewModel> Lines { get; } = [];

    /// <summary>Where the file this mirrors is written, so a bug report can be found without hunting.</summary>
    public string LogDirectory => EditorLog.Directory;

    public string Status => EditorStrings.Format(EditorStrings.Console_Status, ("Count", Lines.Count));

    /// <summary>Raised after lines land, so the view can follow the tail.</summary>
    public event Action? LineAppended;

    [RelayCommand]
    private void Clear()
    {
        EditorLog.Console.Clear();
        Lines.Clear();
        OnPropertyChanged(nameof(Status));
    }

    /// <summary>Set by the window: opening a folder needs a TopLevel, which a view-model has no business
    /// holding. The same delegate the logging dialog takes.</summary>
    public Func<string, Task>? RevealFolderAsync { get; set; }

    /// <summary>Set by the window: opens the SAME logging dialog the Help menu opens, so there is one
    /// control over the capture level rather than two that can disagree.</summary>
    public Func<Task>? ShowLoggingAsync { get; set; }

    [RelayCommand]
    private async Task ConfigureAsync()
    {
        if (ShowLoggingAsync is null) return;
        await ShowLoggingAsync();
    }

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        if (RevealFolderAsync is null) return;
        try { await RevealFolderAsync(EditorLog.Directory); }
        catch (Exception ex) { EditorLog.Warn(ex, "Could not open the log folder."); }
    }

    /// <summary>The sink raises this from whichever thread logged — the server connection runs off the UI
    /// thread — so the hop to the UI thread happens here. Posting rather than invoking keeps a logging
    /// call from ever waiting on the UI, which is what would turn a stalled editor into a deadlocked one.</summary>
    private void OnWritten(ConsoleSink.Line line)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Lines.Add(new ConsoleLineViewModel(line));
            while (Lines.Count > MaxRows) Lines.RemoveAt(0);
            OnPropertyChanged(nameof(Status));
            LineAppended?.Invoke();
        });
    }

    private void OnLanguageChanged() => OnPropertyChanged(nameof(Status));

    public void Dispose()
    {
        EditorLog.Console.Written -= OnWritten;
        EditorStrings.LanguageChanged -= OnLanguageChanged;
    }
}

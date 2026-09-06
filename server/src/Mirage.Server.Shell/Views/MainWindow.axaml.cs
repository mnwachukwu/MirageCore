using Avalonia.Controls;
using System.ComponentModel;
// SetTextAsync is an EXTENSION here, not a member: Avalonia 12 moved IClipboard to a data-transfer model
// and left the text convenience in ClipboardExtensions.
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FluentAvalonia.UI.Windowing;
using Mirage.Server.Shell.ViewModels;

namespace Mirage.Server.Shell.Views;

/// <summary>
/// The operator's window.
///
/// <para><see cref="FAAppWindow"/> rather than <see cref="Window"/>: on Windows it draws its own title
/// bar, which is what lets the frame carry the app's palette instead of the system's grey. It does that
/// ONLY under <c>OperatingSystem.IsWindows()</c> — elsewhere the window keeps native decorations and
/// every bit of native window behaviour with them.</para>
/// </summary>
public sealed partial class MainWindow : FAAppWindow
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
                vm.PropertyChanged += FollowTail;
        };
    }

    private async void HelpAbout_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var dlg = new AboutDialog();
        await dlg.ShowDialog(this);
    }

    /// <summary>Keeps the console pinned to its newest line — unless the operator is holding a
    /// selection, in which case scrolling would drag the view out from under what they are trying to
    /// copy. That is the whole reason to check: the pane is worth following only while nobody is
    /// reading it.</summary>
    private void FollowTail(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.ConsoleText)) return;
        var box = this.FindControl<TextBox>("ConsoleOutput");
        if (box is null || box.SelectionStart != box.SelectionEnd) return;
        // Queued: the new text is on the control but has not been measured yet, so moving the caret now
        // would land one line short every time.
        Dispatcher.UIThread.Post(() => box.CaretIndex = box.Text?.Length ?? 0, DispatcherPriority.Background);
    }

    /// <summary>Code-behind rather than a command, because the folder picker hangs off the TopLevel: a
    /// view-model that reached for it would be holding a window.</summary>
    private async void BrowseForDataDir(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = vm.DataDirLabel,
            AllowMultiple = false,
        });
        if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path) vm.DataDir = path;
    }

    /// <summary>Code-behind because a dialog needs an owner, and an owner is a window.</summary>
    private async void OpenBenchmark(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        await new BenchWindow { DataContext = vm.CreateBenchmark() }.ShowDialog(this);
    }

    /// <summary>Code-behind for the same reason as the folder picker: the clipboard hangs off the TopLevel.</summary>
    private async void CopyManagementToken(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || Clipboard is null) return;
        await Clipboard.SetTextAsync(vm.ManagementToken);
        vm.ReportTokenCopied();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

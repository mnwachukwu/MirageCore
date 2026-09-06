using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Mirage.Editor.Localization;
using Mirage.Editor.ViewModels;

namespace Mirage.Editor.Views;

/// <summary>
/// The Console: a modeless window showing what the editor has been logging.
///
/// <para>Shown rather than dialogued, like the World Preview and the Layer Visibility picker — it is
/// meant to sit beside the editor while that keeps taking input, so it never returns a result. It
/// carries its own geometry for the same reason: <c>MainWindow.SaveWindowState</c> cannot reach a window
/// that is not in its visual tree.</para>
///
/// <para>Not <c>Topmost</c>, unlike those two. They are tools you act through while looking at the map;
/// this is a thing you read, and pinning a wall of log text over the editor is the opposite of helpful.</para>
///
/// <para>It follows the tail only while the view is already at the bottom. Scrolling up is how somebody
/// reads what just went wrong, and a window that yanked them back down on the next line would make that
/// impossible.</para>
/// </summary>
public partial class ConsoleWindow : Window
{
    /// <summary>How close to the bottom still counts as "at the bottom". A line's height, so following
    /// survives the pixel or two of drift a fresh line introduces.</summary>
    private const double FollowSlackPx = 24;

    private ConsoleViewModel? _vm;

    public ConsoleWindow()
    {
        InitializeComponent();
        Title = EditorStrings.TitleFor(EditorStrings.Console_Title);
        _intro.Text = EditorStrings.Get(EditorStrings.Console_Intro);
        _clearButton.Content = EditorStrings.Get(EditorStrings.Console_Clear);
        _openFolderButton.Content = EditorStrings.Get(EditorStrings.Console_OpenFolder);
        // The Help menu's own caption, so the button and the menu item name one thing.
        _configureButton.Content = EditorStrings.Get(EditorStrings.MainWindow_HelpLogging);

        var settings = AppSettings.Current;
        if (settings.ConsoleWidth is { } w && w > MinWidth) Width = w;
        if (settings.ConsoleHeight is { } h && h > MinHeight) Height = h;

        Opened += OnOpened;
        Closing += OnClosing;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null) _vm.LineAppended -= FollowTail;

        _vm = DataContext as ConsoleViewModel;
        if (_vm is null) return;

        _vm.RevealFolderAsync = async path =>
            await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
        // Modal to THIS window rather than the editor: a dialog opened from here and owned by the main
        // window would sit behind the one that opened it.
        _vm.ShowLoggingAsync = async () =>
        {
            if (Owner is MainWindow main) await main.ShowLoggingDialogAsync(this);
        };
        _vm.LineAppended += FollowTail;
        FollowTail();
    }

    private void FollowTail()
    {
        var scroller = _scroller;
        if (scroller is null) return;

        bool atBottom = scroller.Offset.Y >= scroller.Extent.Height - scroller.Viewport.Height - FollowSlackPx;
        if (atBottom) scroller.ScrollToEnd();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        // Position lands here rather than in the constructor: before the window is shown the platform can
        // still move it, and the restored point would be overwritten.
        var settings = AppSettings.Current;
        if (settings.ConsoleX is { } x && settings.ConsoleY is { } y)
            Position = new PixelPoint((int)x, (int)y);

        _scroller.ScrollToEnd();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        var settings = AppSettings.Current;
        if (WindowState == WindowState.Normal)
        {
            settings.ConsoleX = Position.X;
            settings.ConsoleY = Position.Y;
            settings.ConsoleWidth = Width;
            settings.ConsoleHeight = Height;
        }
        settings.Save();

        if (_vm is not null)
        {
            _vm.LineAppended -= FollowTail;
            _vm.Dispose();
            _vm = null;
        }
    }
}

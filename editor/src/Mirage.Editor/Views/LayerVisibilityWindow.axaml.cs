using Avalonia;
using Avalonia.Controls;
using Mirage.Editor.Localization;
using Mirage.Editor.ViewModels;

namespace Mirage.Editor.Views;

/// <summary>
/// The Layer Visibility picker: a modeless window that decides which of the map canvas's layers are drawn.
///
/// <para>Shown rather than dialogued, like the World Preview — it has to sit beside the map editor while
/// that keeps taking input, so it never returns a result. It carries its own geometry for the same reason:
/// <c>MainWindow.SaveWindowState</c> cannot reach a window that is not in its visual tree.</para>
/// </summary>
public partial class LayerVisibilityWindow : Window
{
    private LayerVisibilityViewModel? _vm;

    public LayerVisibilityWindow()
    {
        InitializeComponent();
        Title = EditorStrings.TitleFor(EditorStrings.LayerVisibility_Title);
        _intro.Text = EditorStrings.Get(EditorStrings.LayerVisibility_Intro);

        // A remembered size is only honored above the minimum. The title bar carries the app name and the
        // window's name, so a size saved before the minimum was raised would come back too narrow to read.
        var settings = AppSettings.Current;
        if (settings.LayerVisibilityWidth is { } w && w > MinWidth) Width = w;
        if (settings.LayerVisibilityHeight is { } h && h > MinHeight) Height = h;

        Opened += OnOpened;
        Closing += OnClosing;
        DataContextChanged += (_, _) => _vm = DataContext as LayerVisibilityViewModel;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        // Position lands here rather than in the constructor: before the window is shown the platform can
        // still move it, and the restored point would be overwritten.
        var settings = AppSettings.Current;
        if (settings.LayerVisibilityX is { } x && settings.LayerVisibilityY is { } y)
            Position = new PixelPoint((int)x, (int)y);
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        var settings = AppSettings.Current;
        if (WindowState == WindowState.Normal)
        {
            settings.LayerVisibilityX = Position.X;
            settings.LayerVisibilityY = Position.Y;
            settings.LayerVisibilityWidth = Width;
            settings.LayerVisibilityHeight = Height;
        }
        settings.Save();

        // Putting the layers back is the view-model's job and it does it on dispose, so closing the window
        // can never leave the canvas hiding something with nothing on screen to say so.
        _vm?.Dispose();
        _vm = null;
    }
}

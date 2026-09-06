using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Mirage.Editor.Localization;
using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;

namespace Mirage.Editor;

/// <summary>Avalonia application entry point: loads the localized strings, wires up the editor's
/// three long-lived services (bitmap cache, server connection, data store), and opens the main
/// window bound to a fresh <see cref="MainWindowViewModel"/>.</summary>
public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>Builds the object graph and shows the main window. The view-model's async startup is
    /// deliberately not awaited — the window must appear before the offline data set finishes loading.</summary>
    public override void OnFrameworkInitializationCompleted()
    {
        EditorStrings.Load(Path.Combine(AppContext.BaseDirectory, "lang"), AppSettings.Current.Language);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var bitmapCache = new EditorBitmapCache();
            var connection = new EditorConnection();
            var dataService = new EditorDataService();
            var vm = new MainWindowViewModel(dataService, connection, bitmapCache);

            desktop.MainWindow = new MainWindow { DataContext = vm };
            desktop.ShutdownRequested += (_, _) => EditorLog.Info("Shutdown requested by the desktop lifetime.");
            EditorLog.StartStallWatchdog();
            _ = vm.InitializeAsync();
        }
        base.OnFrameworkInitializationCompleted();
    }
}

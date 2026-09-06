using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Mirage.Server.Core.Configuration;
using Mirage.Server.Shell.Localization;
using Mirage.Server.Shell.ViewModels;
using Mirage.Server.Shell.Views;

namespace Mirage.Server.Shell;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // ONE language setting, read from the server's own appsettings.json. This window is the
        // operator's view of that server, so it speaks whatever the server's console and logs speak —
        // there is deliberately no second knob for the shell.
        // lang/shell/, NOT lang/ — the server's own table lives there and this one would overwrite it.
        // See the note in the csproj; it is a crash in the other program if this moves.
        ShellStrings.Load(Path.Combine(AppContext.BaseDirectory, "lang", "shell"), OperatorLanguage());

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow { DataContext = vm };
            // Closing the window must not orphan a running server: with no console attached there would
            // be no way left to shut it down gracefully. ShutdownRequested runs before the window dies,
            // which is the last point at which the drain can still be awaited.
            desktop.ShutdownRequested += (_, _) => vm.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>The operator language, read from the same serverconfig.json the server reads — through
    /// the same type, so the two can never disagree about what the setting means. Any failure yields
    /// English rather than stopping: a shell that refused to open because its server's config had a typo
    /// would be refusing to open the one tool that could fix it.</summary>
    private static string OperatorLanguage() =>
        ServerConfigStore.Load(ServerConfigStore.DefaultPath).Config.Language;
}

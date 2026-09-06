using Avalonia;
using Mirage.Editor.Services;
using Mirage.Shared;
using Mirage.Updates;
using Velopack;

namespace Mirage.Editor;

/// <summary>Process entry point. Runs the Velopack update hooks before Avalonia starts, since an
/// install or update step may need to exit the process before any UI exists.</summary>
sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        EditorLog.Initialize();
        InstallCrashHandlers();

        // Look for a newer build in the background and stage it for the next launch. Fire-and-forget:
        // nothing should wait on GitHub to open an editor, and AppUpdates swallows every failure.
        // Does nothing on macOS or a portable copy.
        _ = AppUpdates.StageForNextLaunchAsync(UpdatableApp.Editor);

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            EditorLog.Shutdown("normal exit");
        }
        catch (Exception ex)
        {
            EditorLog.Error(ex, "The editor terminated on an unhandled exception from the Avalonia lifetime.");
            EditorLog.Shutdown("unhandled exception");
            throw;
        }
    }

    /// <summary>Catches what escapes a handler. Neither hook can keep the process alive, so both exist to
    /// leave a record: a crash with no log is the case this whole sink was added for.</summary>
    private static void InstallCrashHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                EditorLog.Error(ex, "Unhandled exception (terminating: {Terminating}).", e.IsTerminating);
            else
                EditorLog.Error("Unhandled non-exception throw (terminating: {Terminating}).", e.IsTerminating);
        };

        // A faulted Task nobody awaited. Observed here so the finalizer does not tear the process down, and
        // so a fire-and-forget failure leaves the same trail an awaited one would.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            EditorLog.Error(e.Exception, "Unobserved task exception.");
            e.SetObserved();
        };
    }

    /// <summary>Avalonia host configuration. Also called by the XAML previewer, which requires this
    /// exact name and signature.</summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

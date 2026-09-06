using Avalonia;
using Velopack;

namespace Mirage.Server.Shell;

/// <summary>Process entry point. Velopack's update hooks run before Avalonia starts, since an install
/// or update step may exit the process before any window exists.</summary>
sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Avalonia host configuration. Also called by the XAML previewer, which requires this
    /// exact name and signature.</summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

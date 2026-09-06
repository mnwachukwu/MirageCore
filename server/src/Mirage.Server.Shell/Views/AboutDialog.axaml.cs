using Avalonia.Controls;
using Avalonia.Interactivity;
using Mirage.Server.Shell.Localization;
using Mirage.Shared;
using Mirage.Updates;
using System.Reflection;

namespace Mirage.Server.Shell.Views;

/// <summary>Who made the server. Reads from <see cref="Credits"/>, the same place the editor's About,
/// the client's credits screen and the console's <c>/credits</c> read from.</summary>
public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();
        Title = ShellStrings.Format(ShellStrings.About_Title, ("GameName", Constants.GameName));
        _product.Text = ShellStrings.Format(ShellStrings.Window_Title, ("GameName", Constants.GameName));
        _version.Text = ShellStrings.Format(ShellStrings.About_Version, ("Version", AppVersion()));
        _roleLabel.Text = ShellStrings.Get(ShellStrings.About_CreatorDeveloper);
        _author.Text = Credits.Author;
        _siteLink.Content = Credits.Studio;
        ToolTip.SetTip(_siteLink, Credits.SiteUrl);
        _copyright.Text = Credits.CopyrightLine(DateTime.Now.Year);
        _closeBtn.Content = ShellStrings.Get(ShellStrings.About_Close);
        _ = ReportAvailableUpdateAsync();
    }

    /// <summary>Append "update available: X" to the version line when a newer build has been released.
    ///
    /// <para>The check belongs to THIS window, not to the console command. Velopack's mainExe for the
    /// server package is this executable, so the shell is what an update replaces; and the console's
    /// <c>/update</c> needs a running host, and in remote mode answers about the machine it is attached
    /// to rather than the one this window is running on.</para>
    ///
    /// <para>Reports only. Applying restarts the process, and on a server that disconnects everyone.</para></summary>
    private async Task ReportAvailableUpdateAsync()
    {
        string? available = await AppUpdates.CheckAsync(UpdatableApp.Server);
        if (available is null) return;
        _version.Text = ShellStrings.Format(ShellStrings.About_UpdateAvailable, ("Version", available));
    }

    // Informational version when the build stamped one, minus the "+commit" suffix the SDK appends.
    private static string AppVersion()
    {
        string? informational = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            int plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }
        return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "";
    }

    /// <summary>Open the site through Avalonia's launcher. Failures are swallowed: a machine with no
    /// browser is not something an About box can fix, and crashing on one would be far worse.</summary>
    private async void Site_Click(object? sender, RoutedEventArgs e)
    {
        try { await Launcher.LaunchUriAsync(new Uri(Credits.SiteUrl)); }
        catch { /* no browser configured / launcher refused */ }
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}

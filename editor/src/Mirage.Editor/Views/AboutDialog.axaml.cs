using Avalonia.Controls;
using Avalonia.Interactivity;
using Mirage.Editor.Localization;
using Mirage.Shared;
using System.Reflection;

namespace Mirage.Editor.Views;

/// <summary>Who made the editor. The name, studio and URL come from <see cref="Credits"/> — the same
/// place the client's credits screen, the server shell's About and the console's <c>/credits</c> read
/// from — so there is one spelling of each rather than four.</summary>
public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();
        Title = EditorStrings.TitleFor(EditorStrings.About_Title);
        _product.Text = $"{Constants.GameName} — {EditorStrings.Get(EditorStrings.MainWindow_Title)}";
        _version.Text = EditorStrings.Format(EditorStrings.About_Version, ("Version", AppVersion()));
        _roleLabel.Text = EditorStrings.Get(EditorStrings.About_CreatorDeveloper);
        _author.Text = Credits.Author;
        _siteLink.Content = Credits.Studio;
        ToolTip.SetTip(_siteLink, Credits.SiteUrl);
        _copyright.Text = Credits.CopyrightLine(DateTime.Now.Year);
        _closeBtn.Content = EditorStrings.Get(EditorStrings.Common_Close);
    }

    // Informational version when the build stamped one, else the assembly version. Trimmed of the
    // "+commit" suffix the SDK appends, which is noise in a dialog.
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

    /// <summary>Open the site in the default browser. Avalonia's own launcher rather than
    /// <c>Process.Start</c>: it is the platform-correct path on every target instead of a shell-execute
    /// quirk per OS. Failures are swallowed — a machine with no browser is not something a credits
    /// dialog can fix, and crashing on one would be far worse than the link doing nothing.</summary>
    private async void Site_Click(object? sender, RoutedEventArgs e)
    {
        try { await Launcher.LaunchUriAsync(new Uri(Credits.SiteUrl)); }
        catch { /* no browser configured / launcher refused */ }
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => this.CloseDeferred();
}

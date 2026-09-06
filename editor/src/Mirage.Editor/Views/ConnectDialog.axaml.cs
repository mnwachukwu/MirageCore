using Avalonia.Controls;
using Mirage.Editor.Localization;

namespace Mirage.Editor.Views;

/// <summary>Host / credentials prompt for connecting to a running server. Bound to
/// <see cref="ViewModels.ConnectDialogViewModel"/>, which owns the connection attempt.</summary>
public partial class ConnectDialog : Window
{
    public ConnectDialog()
    {
        InitializeComponent();
        Title = EditorStrings.TitleFor(EditorStrings.ConnectDialog_Title);
        _usernameLabel.Text = EditorStrings.Get(EditorStrings.ConnectDialog_UsernameLabel);
        _passwordLabel.Text = EditorStrings.Get(EditorStrings.ConnectDialog_PasswordLabel);
        _hostLabel.Text = EditorStrings.Get(EditorStrings.ConnectDialog_HostLabel);
        _portLabel.Text = EditorStrings.Get(EditorStrings.ConnectDialog_PortLabel);
        _signInGroup.Header = EditorStrings.Get(EditorStrings.ConnectDialog_SignInHeader);
        _serverGroup.Header = EditorStrings.Get(EditorStrings.ConnectDialog_ServerHeader);
        _savedServersGroup.Header = EditorStrings.Get(EditorStrings.ConnectDialog_SavedServersHeader);
        _cancelBtn.Content = EditorStrings.Get(EditorStrings.Common_Cancel);
        _connectBtn.Content = EditorStrings.Get(EditorStrings.Common_Connect);
    }

    /// <summary>Focus the first empty field when the dialog opens, so a remembered host and port
    /// can be accepted without reaching for the mouse.</summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        this.FindControl<TextBox>("UsernameBox")?.Focus();
    }
}

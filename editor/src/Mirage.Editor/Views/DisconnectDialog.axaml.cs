using Avalonia.Controls;
using Mirage.Editor.Localization;

namespace Mirage.Editor.Views;

/// <summary>Shown when the server connection drops: offers a reconnect or a switch to offline
/// editing. Deliberately cannot be dismissed with the title-bar close button — the owner cancels
/// the Closing event until a choice is made.</summary>
public partial class DisconnectDialog : Window
{
    public DisconnectDialog()
    {
        InitializeComponent();
        Title = EditorStrings.TitleFor(EditorStrings.DisconnectDialog_Title);
        _headerBlock.Text = EditorStrings.Get(EditorStrings.DisconnectDialog_Header);
        _abandonBtn.Content = EditorStrings.Get(EditorStrings.DisconnectDialog_AbandonButton);
        _reconnectBtn.Content = EditorStrings.Get(EditorStrings.DisconnectDialog_ReconnectButton);
    }
}

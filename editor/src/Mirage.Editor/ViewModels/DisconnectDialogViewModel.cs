using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Editor.Services;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;

namespace Mirage.Editor.ViewModels;

/// <summary>
/// Shown when the server connection drops unexpectedly. Offers a reconnect attempt (which reuses
/// <see cref="ConnectDialogViewModel"/>) or dropping to offline editing.
/// <para>A failed or canceled reconnect leaves the dialog open with the reason in
/// <see cref="StatusMessage"/>, so the author can retry without losing unsaved work.</para>
/// </summary>
public sealed partial class DisconnectDialogViewModel : ObservableObject
{
    private readonly EditorConnection _conn;

    [ObservableProperty]
    private string _statusMessage =
        EditorStrings.Get(EditorStrings.DisconnectDialog_ConnectionLostBody);

    // Set by the view layer so ConnectDialog is owned by this dialog window.
    public Func<ConnectDialogViewModel, Task>? ShowConnectDialogAsync { get; set; }

    /// <summary>Raised when a reconnect succeeded, carrying the fresh data payload and access level.</summary>
    public event Action<EditorDataPacket, AdminLevel>? ReconnectSuccess;
    /// <summary>Raised when the dialog should close.</summary>
    public event Action? CloseRequested;

    public DisconnectDialogViewModel(EditorConnection conn)
    {
        _conn = conn;
    }

    /// <summary>Tear down the dead socket, then run the connect dialog again. Success re-arms the
    /// session; anything else reports why and leaves this dialog up.</summary>
    [RelayCommand]
    private async Task AttemptReconnectAsync()
    {
        if (ShowConnectDialogAsync is null) return;

        // Clean up the dead connection before a fresh attempt.
        await _conn.DisconnectAsync();

        bool succeeded = false;
        EditorDataPacket? data = null;
        AdminLevel access = AdminLevel.Player;

        var dlgVm = new ConnectDialogViewModel(_conn) { CloseOnFailure = true };
        dlgVm.ConnectSuccess += (pkt, lvl) => { succeeded = true; data = pkt; access = lvl; };

        await ShowConnectDialogAsync(dlgVm);

        if (succeeded && data is not null)
        {
            ReconnectSuccess?.Invoke(data, access);
            CloseRequested?.Invoke();
        }
        else if (dlgVm.LastError is not null)
        {
            StatusMessage = EditorStrings.Format(EditorStrings.DisconnectDialog_ReconnectFailed, ("Error", dlgVm.LastError));
        }
        else
        {
            StatusMessage = EditorStrings.Get(EditorStrings.DisconnectDialog_ReconnectCanceled);
        }
    }

    /// <summary>Leave the dialog without reconnecting. Carries no event of its own: the caller treats any
    /// exit that is not a reconnect as the offline choice, so closing the window by any route lands here.</summary>
    [RelayCommand]
    private void GoOffline() => CloseRequested?.Invoke();
}

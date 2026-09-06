using Avalonia.Controls;
using Mirage.Editor.Localization;

namespace Mirage.Editor.Views;

/// <summary>Small modal yes/no prompt, closing with <c>true</c> on confirm and <c>false</c> on
/// cancel. With <c>alertOnly</c> the cancel button is hidden, making it a plain notice.</summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog() { InitializeComponent(); }

    /// <summary>Build a prompt around <paramref name="message"/>.</summary>
    /// <param name="message">Body text shown to the user.</param>
    /// <param name="confirmText">Confirm-button caption; defaults to the localized "OK".</param>
    /// <param name="alertOnly">Hide the cancel button, leaving an acknowledge-only notice.</param>
    public ConfirmDialog(string message, string? confirmText = null, bool alertOnly = false) : this()
    {
        Title = EditorStrings.TitleFor(alertOnly
            ? EditorStrings.ConfirmDialog_AlertTitle
            : EditorStrings.ConfirmDialog_Title);
        this.FindControl<TextBlock>("MessageBlock")!.Text = message;
        var confirmBtn = this.FindControl<Button>("ConfirmButton")!;
        confirmBtn.Content = confirmText ?? EditorStrings.Get(EditorStrings.ConfirmDialog_OkButton);
        confirmBtn.Click += (_, _) => this.CloseDeferred(true);
        confirmBtn.IsDefault = true;
        var cancelBtn = this.FindControl<Button>("CancelButton")!;
        cancelBtn.Content = EditorStrings.Get(EditorStrings.Common_Cancel);
        cancelBtn.Click += (_, _) => this.CloseDeferred(false);
        cancelBtn.IsCancel = true;
        // A notice has no Cancel to carry Esc, so the one remaining button answers both keys.
        if (alertOnly)
        {
            cancelBtn.IsVisible = false;
            confirmBtn.IsCancel = true;
        }
    }
}

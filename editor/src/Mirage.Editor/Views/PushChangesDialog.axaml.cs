using Avalonia.Controls;
using Mirage.Editor.Localization;

namespace Mirage.Editor.Views;

/// <summary>Lists unsaved edits before a transition that would discard them, offering to commit
/// them first. Bound to <see cref="ViewModels.PushChangesDialogViewModel"/>.</summary>
public partial class PushChangesDialog : Window
{
    public PushChangesDialog()
    {
        InitializeComponent();
        Title = EditorStrings.TitleFor(EditorStrings.PushChangesDialog_Title);
        _cancelBtn.Content = EditorStrings.Get(EditorStrings.Common_Cancel);
    }
}

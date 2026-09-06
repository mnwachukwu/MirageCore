using Avalonia.Controls;
using Mirage.Editor.Localization;

namespace Mirage.Editor.Views;

/// <summary>A world's record ceilings. Captions are assigned here rather than bound, matching the other
/// dialogs; being a Window, it is built fresh on each open and so needs no re-apply hook.</summary>
public partial class WorldSettingsDialog : Window
{
    public WorldSettingsDialog()
    {
        InitializeComponent();
        Title = EditorStrings.TitleFor(EditorStrings.WorldSettings_DialogTitle);
        _cancelBtn.Content = EditorStrings.Get(EditorStrings.Common_Cancel);
        _confirmBtn.Content = EditorStrings.Get(EditorStrings.ConfirmDialog_OkButton);
    }
}

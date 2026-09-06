using Avalonia.Controls;
using Mirage.Editor.Localization;

namespace Mirage.Editor.Views;

/// <summary>One map's size. Captions are assigned here rather than bound, matching the other dialogs;
/// being a Window, it is built fresh on each open and so needs no re-apply hook.</summary>
public partial class MapResizeDialog : Window
{
    public MapResizeDialog()
    {
        InitializeComponent();
        Title = EditorStrings.TitleFor(EditorStrings.MapResize_DialogTitle);
        _cancelBtn.Content = EditorStrings.Get(EditorStrings.Common_Cancel);
        _confirmBtn.Content = EditorStrings.Get(EditorStrings.ConfirmDialog_OkButton);
    }
}

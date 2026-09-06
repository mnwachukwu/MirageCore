using Avalonia.Controls;
using Mirage.Editor.Localization;

namespace Mirage.Editor.Views;

/// <summary>Per-editor auto-save configuration. Captions are assigned here rather than bound, matching
/// the other dialogs; being a Window, it is built fresh on each open and so needs no re-apply hook.</summary>
public partial class AutoSaveDialog : Window
{
    public AutoSaveDialog()
    {
        InitializeComponent();
        Title = EditorStrings.TitleFor(EditorStrings.AutoSave_DialogTitle);
        _intro.Text = EditorStrings.Get(EditorStrings.AutoSave_DialogIntro);
        _colEditor.Text = EditorStrings.Get(EditorStrings.AutoSave_ColumnEditor);
        _colEnabled.Text = EditorStrings.Get(EditorStrings.AutoSave_ColumnEnabled);
        _colInterval.Text = EditorStrings.Get(EditorStrings.AutoSave_ColumnInterval);
        _colReach.Text = EditorStrings.Get(EditorStrings.AutoSave_ColumnReach);
        _cancelBtn.Content = EditorStrings.Get(EditorStrings.Common_Cancel);
        _confirmBtn.Content = EditorStrings.Get(EditorStrings.ConfirmDialog_OkButton);
    }
}

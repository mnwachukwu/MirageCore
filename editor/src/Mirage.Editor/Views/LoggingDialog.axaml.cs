using Avalonia.Controls;
using Mirage.Editor.Localization;

namespace Mirage.Editor.Views;

/// <summary>File-logging configuration: capture level, how long files are kept, and where they live.
/// Captions are assigned here rather than bound, matching the other dialogs.</summary>
public partial class LoggingDialog : Window
{
    public LoggingDialog()
    {
        InitializeComponent();
        Title = EditorStrings.TitleFor(EditorStrings.Logging_DialogTitle);
        _intro.Text = EditorStrings.Get(EditorStrings.Logging_DialogIntro);
        _levelLabel.Text = EditorStrings.Get(EditorStrings.Logging_LevelLabel);
        _retentionLabel.Text = EditorStrings.Get(EditorStrings.Logging_RetentionLabel);
        _folderLabel.Text = EditorStrings.Get(EditorStrings.Logging_FolderLabel);
        _openFolderBtn.Content = EditorStrings.Get(EditorStrings.Logging_OpenFolderButton);
        _cancelBtn.Content = EditorStrings.Get(EditorStrings.Common_Cancel);
        _confirmBtn.Content = EditorStrings.Get(EditorStrings.ConfirmDialog_OkButton);
    }
}

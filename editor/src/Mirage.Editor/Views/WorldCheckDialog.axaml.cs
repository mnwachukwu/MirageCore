using Avalonia.Controls;
using Mirage.Editor.Localization;

namespace Mirage.Editor.Views;

/// <summary>The world check's results. Each row follows through to the map it names, so the window closes
/// on the way.</summary>
public partial class WorldCheckDialog : Window
{
    public WorldCheckDialog()
    {
        InitializeComponent();
        Title = EditorStrings.TitleFor(EditorStrings.WorldCheck_Title);
        _intro.Text = EditorStrings.Get(EditorStrings.WorldCheck_Intro);
        _cleanNote.Text = EditorStrings.Get(EditorStrings.WorldCheck_CleanNote);
        _closeBtn.Content = EditorStrings.Get(EditorStrings.Common_Close);
    }
}

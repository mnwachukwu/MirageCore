using Avalonia.Controls;
using Mirage.Editor.Localization;

namespace Mirage.Editor.Views;

/// <summary>Asks what a new world is called. Where it goes is asked next, by a folder picker.</summary>
public partial class NewWorldDialog : Window
{
    public NewWorldDialog()
    {
        InitializeComponent();
        Title = EditorStrings.TitleFor(EditorStrings.NewWorld_Title);
    }
}

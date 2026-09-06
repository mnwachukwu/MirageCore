using Avalonia.Controls;
using Mirage.Editor.Localization;

namespace Mirage.Editor.Views;

/// <summary>Where one map's warps lead, as a grid of rendered destinations. Each card follows through to
/// that map, so the dialog closes on the way.</summary>
public partial class WarpTargetsDialog : Window
{
    public WarpTargetsDialog()
    {
        InitializeComponent();
        Title = EditorStrings.TitleFor(EditorStrings.WarpTargets_Title);
        _noneNote.Text = EditorStrings.Get(EditorStrings.WarpTargets_None);
    }
}

using Avalonia.Controls;
using Mirage.Editor.Localization;

namespace Mirage.Editor.Views;

/// <summary>Every graphics sheet the editor can see, and the operations that change them. Chrome captions
/// are set here rather than bound, matching the other dialogs — each is built fresh per open, so there is
/// nothing to re-localize in place.</summary>
public partial class AssetManagerDialog : Window
{
    public AssetManagerDialog()
    {
        InitializeComponent();
        Title = EditorStrings.TitleFor(EditorStrings.AssetManager_Title);
        _categoryLabel.Text = EditorStrings.Get(EditorStrings.AssetManager_CategoryLabel);
        _sizeLabel.Text = EditorStrings.Get(EditorStrings.AssetManager_SizeLabel);
        _emptyNote.Text = EditorStrings.Get(EditorStrings.AssetManager_Empty);
        _problemsHeader.Text = EditorStrings.Get(EditorStrings.AssetManager_ProblemsHeader);
        _recycleHeader.Text = EditorStrings.Get(EditorStrings.AssetManager_RecycleHeader);
        _importBtn.Content = EditorStrings.Get(EditorStrings.AssetManager_Import);
        _openFolderBtn.Content = EditorStrings.Get(EditorStrings.AssetManager_OpenFolder);
        _closeBtn.Content = EditorStrings.Get(EditorStrings.Common_Close);
    }
}

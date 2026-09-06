using Avalonia.Controls;
using Avalonia.Input;
using Mirage.Editor.Localization;

namespace Mirage.Editor.Views;

/// <summary>Reference sheet for the parts of the editor that are not about a map: where art lives, how
/// it is named and managed, and what each Reload re-reads.</summary>
public partial class GeneralHelpDialog : Window
{
    // The sheet has no buttons, so there is no IsCancel button to carry Esc the way the other
    // dialogs do. Closes on Esc here instead.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            this.CloseDeferred();
            return;
        }
        base.OnKeyDown(e);
    }

    public GeneralHelpDialog()
    {
        InitializeComponent();
        Title = EditorStrings.TitleFor(EditorStrings.GeneralHelp_Title);
        _header.Text = EditorStrings.Get(EditorStrings.GeneralHelp_Header);

        _assetsHeader.Text = EditorStrings.Get(EditorStrings.GeneralHelp_AssetsHeader);
        _assetsIntro.Text = EditorStrings.Get(EditorStrings.GeneralHelp_AssetsIntro);
        _assetsSheets.Text = EditorStrings.Get(EditorStrings.GeneralHelp_Assets_Sheets);
        _assetsSheetsDesc.Text = EditorStrings.Get(EditorStrings.GeneralHelp_Assets_SheetsDesc);
        _assetsManager.Text = EditorStrings.Get(EditorStrings.GeneralHelp_Assets_Manager);
        _assetsManagerDesc.Text = EditorStrings.Get(EditorStrings.GeneralHelp_Assets_ManagerDesc);
        _assetsReload.Text = EditorStrings.Get(EditorStrings.GeneralHelp_Assets_Reload);
        _assetsReloadDesc.Text = EditorStrings.Get(EditorStrings.GeneralHelp_Assets_ReloadDesc);
    }
}

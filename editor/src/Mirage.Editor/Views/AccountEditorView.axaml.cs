using Mirage.Editor.Localization;

namespace Mirage.Editor.Views;

/// <summary>Code-behind for the account browser. Localizes the captions, which are assigned in code
/// rather than bound so a language switch can re-apply them in place.
///
/// <para>No panel-width persistence here, unlike the record editors: this pane is a fixed browser and a
/// detail form rather than a resizable split, so there is nothing to remember.</para></summary>
public partial class AccountEditorView : LocalizedUserControl
{
    public AccountEditorView()
    {
        InitializeComponent();
        ApplyStrings();
    }

    protected override void ApplyStrings()
    {
        _searchBox.PlaceholderText = EditorStrings.Get(EditorStrings.AccountEditor_SearchPlaceholder);
        _offlineNotice.Text = EditorStrings.Get(EditorStrings.AccountEditor_OfflineNotice);
        _pickHint.Text = EditorStrings.Get(EditorStrings.AccountEditor_SelectPrompt);
        _accessLabel.Text = EditorStrings.Get(EditorStrings.AccountEditor_AccessLabel);
        _guildLabel.Text = EditorStrings.Get(EditorStrings.AccountEditor_GuildLabel);
        _selfHint.Text = EditorStrings.Get(EditorStrings.AccountEditor_SelfAccessHint);
        _charsHeader.Text = EditorStrings.Get(EditorStrings.AccountEditor_CharactersHeader);
        _noCharsHint.Text = EditorStrings.Get(EditorStrings.AccountEditor_NoCharacters);
        _prevBtn.Content = EditorStrings.Get(EditorStrings.AccountEditor_PrevPage);
        _nextBtn.Content = EditorStrings.Get(EditorStrings.AccountEditor_NextPage);
        _reloadBtn.Content = EditorStrings.Get(EditorStrings.AccountEditor_Reload);
        _saveBtn.Content = EditorStrings.Get(EditorStrings.AccountEditor_Save);
        _blockedHint.Text = EditorStrings.Get(EditorStrings.AccountEditor_SaveBlockedBudget);
        _bankGiveBtn.Content = EditorStrings.Get(EditorStrings.AccountEditor_Give);
    }
}

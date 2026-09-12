using Mirage.Editor.Localization;

namespace Mirage.Editor.Views;

/// <summary>Code-behind for the section that authors a family a module declared. Localizes the captions
/// that are assigned rather than bound; the field labels come from the schema instead, so they are not
/// this build's to translate.</summary>
public partial class SchemaRecordEditorView : LocalizedUserControl
{
    public SchemaRecordEditorView()
    {
        InitializeComponent();
        ApplyStrings();
    }

    protected override void ApplyStrings()
    {
        _filterTextBox.PlaceholderText = EditorStrings.Get(EditorStrings.Common_Filter);
        _selectPrompt.Text = EditorStrings.Get(EditorStrings.SchemaEditor_SelectPrompt);
        _noFields.Text = EditorStrings.Get(EditorStrings.SchemaEditor_NoFields);
        _missingRequired.Text = EditorStrings.Get(EditorStrings.SchemaEditor_MissingRequired);
        _copyBtn.Content = EditorStrings.Get(EditorStrings.Common_Copy);
        _discardBtn.Content = EditorStrings.Get(EditorStrings.Common_Discard);
        _discardAllBtn.Content = EditorStrings.Get(EditorStrings.Common_DiscardAll);
        _saveBtn.Content = EditorStrings.Get(EditorStrings.SchemaEditor_SaveButton);
        _saveAllBtn.Content = EditorStrings.Get(EditorStrings.Common_SaveAll);
    }
}

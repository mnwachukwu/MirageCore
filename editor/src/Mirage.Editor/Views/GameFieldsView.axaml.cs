using Mirage.Editor.Localization;

namespace Mirage.Editor.Views;

/// <summary>Code-behind for the game's-own-fields section. The field labels come from the schema the
/// server or the world folder reported, so the only caption this build translates is the note under
/// the form.</summary>
public partial class GameFieldsView : LocalizedUserControl
{
    public GameFieldsView()
    {
        InitializeComponent();
        ApplyStrings();
    }

    protected override void ApplyStrings() =>
        _missingRequired.Text = EditorStrings.Get(EditorStrings.SchemaEditor_MissingRequired);
}

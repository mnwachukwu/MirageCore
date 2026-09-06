using Mirage.Editor.Localization;

namespace Mirage.Editor.Controls;

/// <summary>
/// Checkbox list for an entity's allowed classes, bound to a <c>ClassSelectionViewModel</c>. Shared by
/// the item, spell and quest editors — one control, so the three cannot drift into three different ideas
/// of what "no restriction" looks like.
/// <para>Derives from <see cref="LocalizedUserControl"/> like the section views: the hint below the list
/// is assigned in code, so it needs the re-apply-on-language-change hook the base owns. The summary line
/// above the list is bound instead, and the view-model re-raises it.</para>
/// <para>No hand-written <c>InitializeComponent</c>: Avalonia generates it, along with the fields for the
/// <c>x:Name</c>d elements. Declaring one here compiles — the generator still emits the fields — but it
/// is the only file in the editor that did, and IDE tooling keys off the standard shape to associate the
/// markup with this class. Written the normal way, the IDE resolves <c>_hint</c> like any other view.</para>
/// </summary>
public partial class ClassSelector : LocalizedUserControl
{
    public ClassSelector()
    {
        InitializeComponent();
        ApplyStrings();
    }

    protected override void ApplyStrings() =>
        _hint.Text = EditorStrings.Get(EditorStrings.ClassSelector_Hint);
}

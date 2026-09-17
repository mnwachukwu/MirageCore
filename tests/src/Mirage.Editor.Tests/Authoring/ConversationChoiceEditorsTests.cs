using Mirage.Editor.ViewModels;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Reflection;

namespace Mirage.Editor.Tests.Authoring;

/// <summary>
/// A conversation choice is authored in TWO places — the inline text view and the node dialog the graph
/// opens — and both edit the same row view-model.
///
/// <para>🔴 That is the trap: a field bound in one and not the other is invisible. The view-model is
/// whole, every test of it passes, the build is green, and half the editor simply cannot author the
/// field. So the bindings are checked at the source, per field, for both views.</para>
/// </summary>
[TestFixture]
public class ConversationChoiceEditorsTests
{
    // Every editable property of a choice, and the binding that must appear in both views. A property
    // added here without a binding in both places fails, which is the whole ask.
    private static readonly string[] ChoiceBindings = ["Label", "SelectedNextNode", "Action", "ActionId"];

    private static readonly string[] Views = ["ConversationEditorView.axaml", "ConversationNodeDialog.axaml"];

    private static string Read(string view)
    {
        string root = typeof(ConversationChoiceEditorsTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!;
        string path = Path.Combine(root, "editor", "src", "Mirage.Editor", "Views", view);
        Assert.That(File.Exists(path), Is.True, $"view not found: {path} — if it moved, teach this test where");
        return File.ReadAllText(path);
    }

    [Test]
    public void BothChoiceEditors_BindEveryEditableField()
    {
        Assert.Multiple(() =>
        {
            foreach (string view in Views)
            {
                string xaml = Read(view);
                foreach (string binding in ChoiceBindings)
                {
                    // Anchored on the end of the path, so "ActionId" does not satisfy a search for
                    // "Action" and "ActionIdPlaceholder" does not satisfy one for "ActionId".
                    Assert.That(xaml, Does.Match($"Binding {binding}[,}}]"),
                        $"{view} does not bind {binding} — a choice edited there cannot set it, and "
                        + "nothing else would say so");
                }
            }
        });
    }

    /// <summary>A game's own verb reaches the record trimmed, so a stray space does not become an id no
    /// handler owns.</summary>
    [Test]
    public void AnActionIdIsTrimmedOnTheWayToTheRecord()
    {
        var row = new ConversationChoiceRowViewModel(0, new ConversationChoice(), () => []) { ActionId = "  survey.note  " };

        Assert.That(row.ToRecord().ActionId, Is.EqualTo("survey.note"));
    }

    [Test]
    public void AnActionIdRoundTripsThroughTheRow()
    {
        var row = new ConversationChoiceRowViewModel(0, new ConversationChoice { ActionId = "survey.note" }, () => []);

        Assert.That(row.ActionId, Is.EqualTo("survey.note"));
    }

    [Test]
    public void TypingAnActionIdDirtiesTheRow()
    {
        var row = new ConversationChoiceRowViewModel(0, new ConversationChoice(), () => []);

        row.ActionId = "survey.note";

        Assert.That(row.IsDirty, Is.True, "an edit nothing marks dirty is an edit Save will not write");
    }
}

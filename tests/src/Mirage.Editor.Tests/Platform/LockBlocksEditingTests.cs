using NUnit.Framework;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Mirage.Editor.Tests.Platform;

/// <summary>
/// A padlock on a row means the record cannot be edited, in every editor that shows one.
///
/// <para>🔴 The badge and the block are separate bindings — <c>LockedByOther</c> on the row draws the
/// padlock, <c>IsSelectedLocked</c> on the view-model deadens the controls — and a view can carry the first
/// without the second. It then reads as protected while accepting every edit, which is worse than showing
/// nothing: the reader believes the lock is working.</para>
///
/// <para>Read from the .axaml source. Whether a control is disabled is a property of the markup, and no
/// test that constructs the view can tell an unbound <c>IsEnabled</c> from one that is simply true right
/// now.</para>
/// </summary>
[TestFixture]
public class LockBlocksEditingTests
{
    private static string SourceRoot()
    {
        string dir = typeof(LockBlocksEditingTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "EditorSourceRoot").Value!;
        Assert.That(Directory.Exists(dir), Is.True, $"Editor source root not found: {dir}");
        return dir;
    }

    private static string[] ViewFiles() =>
        [.. Directory.GetFiles(SourceRoot(), "*.axaml", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .OrderBy(p => p, StringComparer.Ordinal)];

    /// <summary>Markup with comments removed — a commented-out block still contains the text.</summary>
    private static string Markup(string path) =>
        Regex.Replace(File.ReadAllText(path), "<!--.*?-->", "", RegexOptions.Singleline);

    private static readonly Regex ShowsBadge = new(@"IsVisible\s*=\s*""\{Binding\s+LockedByOther\}""", RegexOptions.Compiled);
    private static readonly Regex Blocks = new(@"IsEnabled\s*=\s*""\{Binding\s+!IsSelectedLocked\}""", RegexOptions.Compiled);

    [Test]
    public void EveryViewThatShowsAPadlock_AlsoDeadensTheEditor()
    {
        var offenders = ViewFiles()
            .Select(f => (Name: Path.GetFileName(f), Text: Markup(f)))
            .Where(v => ShowsBadge.IsMatch(v.Text) && !Blocks.IsMatch(v.Text))
            .Select(v => v.Name)
            .ToList();

        Assert.That(offenders, Is.Empty,
            "These draw a padlock and then accept every edit anyway: " + string.Join(", ", offenders));
    }

    /// <summary>The guard above is only worth anything while padlocks exist to find. A rename of either
    /// binding would empty both sides of it and leave it passing on nothing.</summary>
    [Test]
    public void ThePadlockBindingIsStillCalledThat()
    {
        int showing = ViewFiles().Count(f => ShowsBadge.IsMatch(Markup(f)));

        Assert.That(showing, Is.GreaterThanOrEqualTo(9),
            "Nine record editors and the map editor show a lock badge; finding fewer means the binding was "
            + "renamed and the guard above is no longer looking at anything.");
    }

    /// <summary>The map editor is the one whose write surface is not a form: a canvas, a tool panel and a
    /// properties panel, each of which has to be deadened in its own right.</summary>
    [Test]
    public void TheMapEditor_DeadensTheCanvasAndBothSidePanels()
    {
        string map = Markup(ViewFiles().Single(f => Path.GetFileName(f) == "MapEditorView.axaml"));

        Assert.That(Blocks.Matches(map), Has.Count.GreaterThanOrEqualTo(3),
            "The map editor writes through the canvas, the tool panel and the properties panel; each has to "
            + "be disabled on its own, so one binding cannot be enough.");
    }
}

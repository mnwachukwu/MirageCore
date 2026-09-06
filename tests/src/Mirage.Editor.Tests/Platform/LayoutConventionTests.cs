using NUnit.Framework;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Mirage.Editor.Tests.Platform;

/// <summary>
/// Layout rules the XAML has to keep, checked against the sources because nothing else can see them: bindings
/// are reflective here and a clipped caption still builds, still runs, and only shows up when somebody reads
/// the window.
/// </summary>
[TestFixture]
public class LayoutConventionTests
{
    /// <summary>
    /// A label/value row may not pin its label column to a pixel width.
    ///
    /// <para>A fixed column is a cap: the caption is clipped the moment it grows past it, which happens on any
    /// wording change and on every translation, and nothing reports it. <c>mui:FormRow</c> sizes the column to
    /// the caption and treats <c>LabelWidth</c> as a floor, so the row grows instead of cutting text off.</para>
    ///
    /// <para>Scoped to the two-column <c>N,*</c> form row. Wider grids are tables, where a fixed column is a
    /// deliberate choice and re-sizing per row would make the columns jump — those this cannot judge.</para>
    /// </summary>
    [Test]
    public void NoFormRow_PinsItsLabelColumnToAFixedWidth()
    {
        var offenders = new List<string>();

        foreach (string file in Directory.GetFiles(EditorSourceRoot(), "*.axaml", SearchOption.AllDirectories))
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var m = Regex.Match(lines[i], @"ColumnDefinitions=""(\d+),\*""");
                if (m.Success)
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1} — ColumnDefinitions=\"{m.Groups[1].Value},*\"");
            }
        }

        Assert.That(offenders, Is.Empty,
            "these rows cap their label column; use <mui:FormRow LabelWidth=\"N\"> so the caption cannot be "
            + "clipped:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>A <c>FormRow</c>'s shared label groups resolve against the nearest scope above them, so a view
    /// that uses one without declaring a scope silently loses the alignment between its rows.</summary>
    [Test]
    public void EveryViewUsingFormRow_DeclaresASharedSizeScope()
    {
        var offenders = new List<string>();

        foreach (string file in Directory.GetFiles(EditorSourceRoot(), "*.axaml", SearchOption.AllDirectories))
        {
            string src = File.ReadAllText(file);
            if (!src.Contains("<mui:FormRow", StringComparison.Ordinal)) continue;
            if (!src.Contains("Grid.IsSharedSizeScope", StringComparison.Ordinal))
                offenders.Add(Path.GetFileName(file));
        }

        Assert.That(offenders, Is.Empty,
            "these views use FormRow with no Grid.IsSharedSizeScope=\"True\" on the root: "
            + string.Join(", ", offenders));
    }

    /// <summary>
    /// A window the reader cannot resize has to size itself to its content.
    /// 🔴 Pinning <c>Height</c> on a <c>CanResize="False"</c> window is a cap with no way out of it: the
    /// content is clipped from the bottom, and what a dialog keeps at the bottom is its buttons. It fails
    /// the moment a message grows — a longer warning, a translation, an extra line of detail — and it fails
    /// silently, because a clipped window still builds and still opens.
    ///
    /// <para>A fixed height is fine where the content can scroll, which is what a <c>ScrollViewer</c>
    /// settles, and fine on a resizable window, where the reader can always drag it open.</para>
    /// </summary>
    [Test]
    public void NoFixedHeightWindow_RefusesToResizeAndRefusesToGrow()
    {
        var offenders = new List<string>();

        foreach (string file in Directory.GetFiles(EditorSourceRoot(), "*.axaml", SearchOption.AllDirectories))
        {
            string xaml = File.ReadAllText(file);
            if (!xaml.Contains("<Window", StringComparison.Ordinal)) continue;

            // Only the root element's own attributes; a nested Height belongs to some child control.
            int rootEnd = xaml.IndexOf('>', xaml.IndexOf("<Window", StringComparison.Ordinal));
            if (rootEnd < 0) continue;
            string root = xaml[..rootEnd];

            bool pinsHeight = Regex.IsMatch(root, @"\bHeight=""\d");
            bool cannotResize = root.Contains("CanResize=\"False\"", StringComparison.Ordinal);
            bool sizesItself = root.Contains("SizeToContent=", StringComparison.Ordinal);
            bool canScroll = xaml.Contains("<ScrollViewer", StringComparison.Ordinal);

            if (pinsHeight && cannotResize && !sizesItself && !canScroll)
                offenders.Add(Path.GetFileName(file));
        }

        Assert.That(offenders, Is.Empty,
            "these windows pin a Height, refuse to resize, and cannot scroll, so anything that grows is "
            + "clipped along with the buttons underneath it. Use SizeToContent=\"Height\" and let the "
            + "Width be the wrap measure: " + string.Join(", ", offenders));
    }

    private static string EditorSourceRoot()
    {
        string root = typeof(LayoutConventionTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "EditorSourceRoot").Value!;
        Assert.That(Directory.Exists(root), Is.True, $"Editor source root not found: {root}");
        return root;
    }
}

using NUnit.Framework;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Mirage.Editor.Tests.Platform;

/// <summary>
/// Every namespace prefix a view uses is declared in that view.
///
/// <para>An undeclared prefix survives the build and throws when the window is constructed:
/// <c>Unable to resolve namespace for type vm:MainWindowViewModel</c>, raised from the generated
/// <c>XamlIlPopulate</c> and unhandled. Since bindings here are reflective, a build says nothing about it,
/// and the first sign is the editor failing to open.</para>
///
/// <para>Prefixes appear in three places, and only the first is an element the XAML compiler resolves
/// eagerly: element and attribute names, markup-extension type arguments, and casts inside a binding
/// path.</para>
/// </summary>
[TestFixture]
public class XamlNamespaceTests
{
    // The two the XAML parser supplies itself, present whether or not a file names them.
    private static readonly string[] AlwaysAvailable = ["xml", "xmlns"];

    [Test]
    public void EveryPrefix_AViewUses_IsDeclaredInThatView()
    {
        var offenders = new List<string>();

        foreach (string file in Directory.GetFiles(EditorSourceRoot(), "*.axaml", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            var declared = XDocument.Parse(text).Root!
                .Attributes()
                .Where(a => a.IsNamespaceDeclaration && a.Name.LocalName != "xmlns")
                .Select(a => a.Name.LocalName)
                .Concat(AlwaysAvailable)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var (prefix, line) in UsedPrefixes(text))
                if (!declared.Contains(prefix))
                    offenders.Add($"{Path.GetFileName(file)}:{line} — {prefix}:");
        }

        Assert.That(offenders, Is.Empty,
            "these views use a namespace prefix they do not declare, which throws when the window is "
            + "built:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>Every <c>prefix:Type</c> in the file, wherever it sits: an element name, an attribute name,
    /// a <c>DataType="vm:Row"</c>, an <c>{x:Static}</c> argument, or a cast inside a binding path.
    ///
    /// <para>The namespace declarations themselves are removed first, so the <c>using:</c> and <c>http:</c>
    /// inside their values are not read as prefixes, and comments go with them.</para></summary>
    private static IEnumerable<(string Prefix, int Line)> UsedPrefixes(string text)
    {
        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = Regex.Replace(lines[i], @"xmlns(:[A-Za-z0-9]+)?\s*=\s*""[^""]*""", "");
            line = Regex.Replace(line, @"<!--.*?-->", "");
            foreach (Match m in Regex.Matches(line, @"(?<![A-Za-z0-9_.\-])([a-z][A-Za-z0-9]*):[A-Za-z]"))
                yield return (m.Groups[1].Value, i + 1);
        }
    }

    /// <summary>A declared prefix nothing uses is dead weight in every file that copies the header.</summary>
    [Test]
    public void NoView_DeclaresAPrefixItNeverUses()
    {
        var offenders = new List<string>();

        foreach (string file in Directory.GetFiles(EditorSourceRoot(), "*.axaml", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            var used = UsedPrefixes(text).Select(u => u.Prefix).ToHashSet(StringComparer.Ordinal);

            foreach (var a in XDocument.Parse(text).Root!.Attributes())
            {
                if (!a.IsNamespaceDeclaration || a.Name.LocalName == "xmlns") continue;
                // x: carries x:Class and x:Name, which the parser reads before any of this can see them.
                if (a.Name.LocalName == "x") continue;
                if (!used.Contains(a.Name.LocalName))
                    offenders.Add($"{Path.GetFileName(file)} — xmlns:{a.Name.LocalName}");
            }
        }

        Assert.That(offenders, Is.Empty,
            "these declarations are unused:\n  " + string.Join("\n  ", offenders));
    }

    private static string EditorSourceRoot()
    {
        string root = typeof(XamlNamespaceTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "EditorSourceRoot").Value!;
        Assert.That(Directory.Exists(root), Is.True, $"Editor source root not found: {root}");
        return root;
    }
}

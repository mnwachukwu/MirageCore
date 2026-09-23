using NUnit.Framework;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mirage.Shared.Tests.Localization;

/// <summary>
/// Every <c>{Placeholder}</c> in a localized template, against the call site that renders it.
///
/// <para>🔴 <b>A placeholder nobody supplies is a crash, not a missing word.</b> <c>StringLoader.Format</c>
/// throws in DEBUG on a name it was given no value for, so the first render of such a template takes the
/// process down — and the suite stays green, because a template nothing renders in a test is a template
/// nothing checks. That is invisible in the diff that removes the argument: the template is a string in
/// four JSON files nobody rereads while deleting C#.</para>
///
/// <para>These scan the SOURCE rather than rendering anything, because the call sites live in the client,
/// server and editor, and reaching them needs a window, a socket and a world between them.</para>
/// </summary>
[TestFixture]
public class TemplatePlaceholderTests
{
    // Matches StringLoader's own placeholder grammar, format spec included: {Gold:N0} names "Gold".
    private static readonly Regex Placeholder = new(@"\{(\w+)(?::[^}]+)?\}", RegexOptions.Compiled);

    /// <summary>A set of language files and the code that renders them.</summary>
    public sealed record Unit(string LangDir, string StringsClass, params string[] SourceRoots)
    {
        public override string ToString() => StringsClass;
    }

    private static readonly Unit[] Units =
    [
        new("client/src/Mirage.Client.Shell/lang", "ClientStrings", "client/src"),
        new("server/src/Mirage.Server.Core/lang", "ServerStrings", "server/src"),
        new("editor/src/Mirage.Editor/lang", "EditorStrings", "editor/src"),
        new("server/src/Mirage.Server.Shell/lang/shell", "ShellStrings", "server/src"),
    ];

    private static string RepoRoot => typeof(TemplatePlaceholderTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .First(a => a.Key == "RepoRoot").Value!;

    private static Dictionary<string, Dictionary<string, string>> LanguagesOf(Unit unit)
    {
        string dir = Path.Combine(RepoRoot, unit.LangDir.Replace('/', Path.DirectorySeparatorChar));
        Assert.That(Directory.Exists(dir), Is.True, $"no lang dir at {dir} — if it moved, teach this test where");

        var byFile = new Dictionary<string, Dictionary<string, string>>();
        foreach (string path in Directory.GetFiles(dir, "*.json"))
        {
            byFile[Path.GetFileNameWithoutExtension(path)] =
                JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [];
        }
        Assert.That(byFile, Is.Not.Empty, $"no language files in {dir}");
        return byFile;
    }

    private static string SourceOf(Unit unit)
    {
        var text = new System.Text.StringBuilder();
        foreach (string root in unit.SourceRoots)
        {
            string full = Path.Combine(RepoRoot, root.Replace('/', Path.DirectorySeparatorChar));
            foreach (string file in Directory.EnumerateFiles(full, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                    || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                text.AppendLine(File.ReadAllText(file));
            }
        }
        return text.ToString();
    }

    /// <summary>
    /// 🔴 The one that matters: a template asks for a value, and the code that renders it passes none.
    ///
    /// <para>A key is judged only where it is rendered WITH arguments. A key handed about as a value —
    /// selected by a switch arm, stored in a table, passed to a helper that formats it later — supplies
    /// nothing beside itself, and this cannot see where its values come from, so it is skipped rather
    /// than failed. Under-reporting is the acceptable direction: a check that cries wolf gets deleted.
    /// What is left is exactly the case that keeps happening — an argument dropped from a call and left
    /// behind in every translation.</para>
    /// </summary>
    [TestCaseSource(nameof(Units))]
    public void EveryPlaceholder_IsSuppliedByTheCodeThatRendersIt(Unit unit)
    {
        var languages = LanguagesOf(unit);
        string source = SourceOf(unit);

        // Arguments are written as ("Name", value) after the key they belong to and before the statement
        // ends, so the text between the two is where a supplied name has to appear. Bounded by the
        // semicolon rather than by a character count: a fixed window reaches into the NEXT statement and
        // collects its arguments, which reads as "supplied" for a key that is never given anything.
        const int Window = 800;

        var problems = new List<string>();
        foreach (string key in languages.Values.SelectMany(l => l.Keys).Distinct().OrderBy(k => k))
        {
            var wanted = languages.Values
                .Where(l => l.ContainsKey(key))
                .SelectMany(l => Placeholder.Matches(l[key]).Select(m => m.Groups[1].Value))
                .ToHashSet(StringComparer.Ordinal);
            if (wanted.Count == 0) continue;

            var supplied = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match at in Regex.Matches(source, $@"\b{Regex.Escape(unit.StringsClass)}\.{Regex.Escape(key)}\b"))
            {
                int from = at.Index + at.Length;
                string window = source.Substring(from, Math.Min(Window, source.Length - from));
                int end = window.IndexOf(';');
                if (end >= 0) window = window[..end];
                foreach (Match arg in Regex.Matches(window, "\\(\\s*\"(\\w+)\"\\s*,")) supplied.Add(arg.Groups[1].Value);
            }
            // Nothing supplied anywhere means the key travels as a value and is formatted out of sight.
            if (supplied.Count == 0) continue;

            // An ambient token is bound once at startup and resolves itself, so no call site passes one.
            foreach (string missing in wanted.Except(supplied)
                         .Where(n => !Mirage.Shared.Localization.StringLoader.IsAmbient(n))
                         .OrderBy(n => n))
                problems.Add($"  {key}: template wants {{{missing}}}, and no call site supplies it");
        }

        Assert.That(problems, Is.Empty,
            $"{unit.StringsClass}: a template asks for a value nothing passes, so rendering it throws:\n"
            + string.Join("\n", problems));
    }

    /// <summary>
    /// Every language states the same placeholders for a key.
    ///
    /// <para>A translation carrying one the others do not is the same crash, reached only by the players
    /// who read that language — which is the worst way to find it. One carrying FEWER silently drops a
    /// value out of the sentence for them.</para>
    /// </summary>
    [TestCaseSource(nameof(Units))]
    public void EveryLanguage_AsksForTheSamePlaceholders(Unit unit)
    {
        var languages = LanguagesOf(unit);
        string reference = languages.ContainsKey("en") ? "en" : languages.Keys.First();

        var problems = new List<string>();
        foreach (var (key, template) in languages[reference])
        {
            var expected = Placeholder.Matches(template).Select(m => m.Groups[1].Value)
                                      .ToHashSet(StringComparer.Ordinal);
            foreach (var (lang, strings) in languages)
            {
                if (lang == reference || !strings.TryGetValue(key, out string? other)) continue;
                var actual = Placeholder.Matches(other).Select(m => m.Groups[1].Value)
                                        .ToHashSet(StringComparer.Ordinal);
                if (actual.SetEquals(expected)) continue;

                problems.Add($"  {key} [{lang}]: extra {Show(actual.Except(expected))}, "
                             + $"missing {Show(expected.Except(actual))}");
            }
        }

        Assert.That(problems, Is.Empty,
            $"{unit.StringsClass}: languages disagree about what a template needs:\n" + string.Join("\n", problems));

        static string Show(IEnumerable<string> names)
        {
            var list = names.OrderBy(n => n).ToList();
            return list.Count == 0 ? "none" : string.Join(", ", list.Select(n => "{" + n + "}"));
        }
    }
}

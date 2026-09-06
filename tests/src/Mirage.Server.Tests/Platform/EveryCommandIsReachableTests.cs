using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mirage.Server.Tests.Platform;

/// <summary>
/// <c>/help</c> lists every command the console answers to.
///
/// <para>🔴 It did not. <c>/hwban</c> and <c>/hwunban</c> shipped dispatched but unlisted, so the only
/// way to find them was to read the source. A command nobody can discover is a command that does not
/// exist, and the person adding one is the least likely to reread the paragraph describing the set.</para>
///
/// <para>The other half of the same rule — that the shell's Commands tab offers them too — is checked in
/// <c>Mirage.Server.Shell.Tests</c>, which can ask the tab's own factory rather than parsing it.</para>
/// </summary>
[TestFixture]
public class EveryCommandIsReachableTests
{
    /// <summary>The repository root, baked in by the csproj. Not a walk up from the output directory:
    /// that finds nothing under a redirected build and the guard would skip rather than fail.</summary>
    static string RepoRoot()
    {
        string root = typeof(EveryCommandIsReachableTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!;
        Assert.That(Directory.Exists(root), Is.True, $"Repository root not found: {root}");
        return root;
    }

    /// <summary>Every command the console dispatch answers to, read out of the switch itself.</summary>
    static SortedSet<string> Dispatched()
    {
        string dir = Path.Combine(RepoRoot(), "server", "src", "Mirage.Server.Host", "Services");
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string file in Directory.GetFiles(dir, "ConsoleCommands*.cs"))
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"case ""(/[a-z]+)"":"))
                found.Add(m.Groups[1].Value);

        Assert.That(found, Is.Not.Empty, "no `case \"/x\":` found — has the dispatch moved?");
        Assert.That(found, Does.Contain("/who"), "sanity: /who should always be dispatched");
        return found;
    }

    [Test]
    public void EveryDispatchedCommand_IsListedInHelp()
    {
        string en = Path.Combine(RepoRoot(), "server", "src", "Mirage.Server.Core", "lang", "en.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(en));
        string help = doc.RootElement.GetProperty("Console_Help").GetString()!;

        // Word-boundary match, so /ban does not stand in for /hwban.
        var missing = Dispatched()
            .Where(c => !Regex.IsMatch(help, Regex.Escape(c) + @"(?![a-z])"))
            .ToList();

        Assert.That(missing, Is.Empty,
            "dispatched but absent from Console_Help, so nobody at the console can find them: "
            + string.Join(" ", missing));
    }

    /// <summary>Every language shows the same set. A translation that dropped a command would leave that
    /// operator unable to find it, which is the same failure in a quieter place.</summary>
    [Test]
    public void EveryTranslationOfHelp_ListsTheSameCommands()
    {
        string dir = Path.Combine(RepoRoot(), "server", "src", "Mirage.Server.Core", "lang");
        var dispatched = Dispatched();
        var problems = new List<string>();

        foreach (string file in Directory.GetFiles(dir, "*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (!doc.RootElement.TryGetProperty("Console_Help", out var help)) continue;
            string text = help.GetString()!;

            foreach (string c in dispatched)
                if (!Regex.IsMatch(text, Regex.Escape(c) + @"(?![a-z])"))
                    problems.Add($"{Path.GetFileName(file)} is missing {c}");
        }

        Assert.That(problems, Is.Empty, string.Join("; ", problems));
    }
}

using Mirage.Server.Shell.Localization;
using Mirage.Server.Shell.ViewModels;
using NUnit.Framework;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Mirage.Server.Shell.Tests;

/// <summary>
/// The Commands tab is a set of forms that compose console command lines and post them down the same
/// pipe the console box uses. That is what keeps the CLI the single source of truth — and it is also
/// the failure: a form naming a verb the console does not accept builds, renders, and silently does
/// nothing when pressed, because the server simply replies that it does not know the command.
///
/// <para>So every verb offered by the tab is checked against the <c>case "/…":</c> labels in the
/// console's own dispatcher. Renaming a command on either side breaks this rather than the operator.</para>
/// </summary>
[TestFixture]
public class ShellCommandCoverageTests
{
    /// <summary>The command descriptions are localized, and ShellStrings throws on a missing key in
    /// DEBUG rather than returning a placeholder. Nothing else in this suite needs strings loaded, so
    /// the fixture loads them itself.</summary>
    [OneTimeSetUp]
    public void LoadStrings() =>
        ShellStrings.Load(Path.Combine(AppContext.BaseDirectory, "lang", "shell"), "en");

    /// <summary>Repository root, baked in by the csproj — not a walk up from the output directory,
    /// which finds nothing when the suite is built to a redirected path and would skip instead of
    /// failing.</summary>
    private static string RepoRoot()
    {
        string root = typeof(ShellCommandCoverageTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!;
        Assert.That(Directory.Exists(root), Is.True, $"Repository root not found: {root}");
        return root;
    }

    /// <summary>Every verb the console dispatcher answers to, read from its switch labels. The command
    /// set is split across ConsoleCommands partials, so all of them are scanned.</summary>
    private static HashSet<string> ConsoleVerbs()
    {
        string dir = Path.Combine(RepoRoot(), "server", "src", "Mirage.Server.Host", "Services");
        Assert.That(Directory.Exists(dir), Is.True, $"Console command sources not found: {dir}");

        var verbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.GetFiles(dir, "ConsoleCommands*.cs"))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"case\s+""(/[a-z]+)""\s*:", RegexOptions.IgnoreCase))
                verbs.Add(m.Groups[1].Value);
        }
        return verbs;
    }

    private static List<string> TabVerbs()
    {
        // Built through the app's own factory rather than a copy of the list, which would defeat
        // the point of checking it.
        return MainWindowViewModel.BuildCommandGroups(_ => { }).SelectMany(g => g.Commands).Select(c => c.Verb).ToList();
    }

    [Test]
    public void EveryCommandOnTheTab_IsOneTheConsoleAccepts()
    {
        var accepted = ConsoleVerbs();
        Assert.That(accepted, Is.Not.Empty, "found no console command labels to check against");

        var unknown = TabVerbs().Where(v => !accepted.Contains(v)).Distinct().ToList();

        Assert.That(unknown, Is.Empty,
            "These forms would post a line the console does not answer to: " + string.Join(", ", unknown));
    }

    /// <summary>The reverse of the check above: every command the console answers to is offered by the
    /// tab.
    ///
    /// <para>🔴 The tab is how an operator in the window finds out what exists, so a dispatched command
    /// missing from it is invisible to everyone who is not reading source. Nine were: both machine-ban
    /// commands, every moderation lift, and both editor commands.</para>
    ///
    /// <para><c>/help</c> is the exception. The tab IS the listing, rendered, so asking the server to
    /// print one into the log would be a worse copy of what the operator is already looking at.</para></summary>
    [Test]
    public void TheTabOffersEveryConsoleCommand()
    {
        var offered = TabVerbs().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = ConsoleVerbs().Where(v => v != "/help" && !offered.Contains(v)).OrderBy(v => v).ToList();

        Assert.That(missing, Is.Empty,
            "the console answers to these, but the Commands tab does not offer them: " + string.Join(" ", missing));
    }

    /// <summary>Shutting a server down disconnects everyone on it, so the form asks first — the same
    /// treatment /ban and /setaccess already get.</summary>
    [Test]
    public void Shutdown_AsksBeforeItRuns()
    {
        var shutdown = MainWindowViewModel.BuildCommandGroups(_ => { })
            .SelectMany(g => g.Commands)
            .Single(c => c.Verb == "/shutdown");

        Assert.That(shutdown.NeedsConfirmation, Is.True);
    }

    /// <summary>The two report-only commands take no arguments and must not ask for confirmation:
    /// a prompt in front of a question is friction with nothing behind it.</summary>
    [Test]
    public void TheReportingCommands_TakeNoArgumentsAndNoConfirmation()
    {
        var commands = MainWindowViewModel.BuildCommandGroups(_ => { }).SelectMany(g => g.Commands).ToList();

        Assert.Multiple(() =>
        {
            foreach (string verb in new[] { "/update", "/credits" })
            {
                var cmd = commands.Single(c => c.Verb == verb);
                Assert.That(cmd.Parameters, Is.Empty, $"{verb} takes no arguments");
                Assert.That(cmd.NeedsConfirmation, Is.False, $"{verb} only reports");
            }
        });
    }
}

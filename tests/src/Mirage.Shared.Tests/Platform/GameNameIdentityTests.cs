using NUnit.Framework;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Mirage.Shared.Tests.Platform;

/// <summary>
/// The engine's name is written twice: once in MSBuild, where it becomes the assembly name of every
/// executable, and once in code, where it becomes the per-user settings folders and the filename the
/// server shell composes to launch its server.
///
/// <para><b>Nothing but this connects them.</b> Both halves compile, every suite passes, and the
/// failure appears only when the shell goes looking for an executable whose name was built from the
/// other copy and finds nothing there. The two names live in different languages in different files,
/// so the drift is invisible to a reviewer reading either one.</para>
/// </summary>
[TestFixture]
public class GameNameIdentityTests
{
    /// <summary>The repository root, baked in by the csproj at build time — the same route the other
    /// source-scanning guards take, so this fails rather than silently scanning nothing when the suite
    /// is built to a redirected output path.</summary>
    private static string RepoRoot()
    {
        string root = typeof(GameNameIdentityTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!;
        Assert.That(Directory.Exists(root), Is.True, $"Repository root not found: {root}");
        return root;
    }

    private static string DeclaredInMsBuild()
    {
        string path = Path.Combine(RepoRoot(), "Directory.Build.props");
        Assert.That(File.Exists(path), Is.True, $"Directory.Build.props not found: {path}");

        var match = Regex.Match(File.ReadAllText(path), @"<GameName>(?<name>[^<]+)</GameName>");
        Assert.That(match.Success, Is.True, "Directory.Build.props declares no <GameName>.");
        return match.Groups["name"].Value.Trim();
    }

    [Test]
    public void TheNameInCodeMatchesTheNameEveryExecutableIsBuiltFrom()
    {
        Assert.That(Constants.GameName, Is.EqualTo(DeclaredInMsBuild()),
            "Constants.GameName and <GameName> in Directory.Build.props have drifted. Every executable "
            + "is named from the MSBuild copy; the server shell composes the filename it launches from "
            + "the code copy. While these disagree the shell cannot find its own server, and neither "
            + "the build nor the rest of the suite will say so.");
    }

    /// <summary>The slug is the form that reaches a filename, and it is derived rather than written —
    /// so what is worth asserting is that deriving it the way MSBuild does and the way the server
    /// shell does produce the same string.</summary>
    [Test]
    public void TheSlugTheShellComposesMatchesTheSlugMsBuildProduces()
    {
        string msBuildSlug = DeclaredInMsBuild().Replace(' ', '-');
        string codeSlug = Constants.GameName.Replace(' ', '-');

        Assert.That(codeSlug, Is.EqualTo(msBuildSlug));
    }

    [Test]
    public void TheNameIsNotBlank()
    {
        Assert.That(Constants.GameName, Is.Not.Empty);
    }
}

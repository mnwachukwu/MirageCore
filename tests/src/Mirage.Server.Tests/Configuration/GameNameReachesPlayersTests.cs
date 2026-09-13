using NUnit.Framework;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Mirage.Server.Tests.Configuration;

/// <summary>
/// Who is allowed to say <c>Constants.GameName</c>.
///
/// <para>🔴 <b>The engine's name is the last of three answers, never the first.</b> What a game is called
/// is the operator's choice, else the world's, else the engine's — and <c>ServerConfig.GameName</c> is the
/// one place that resolves them. A line reaching past it to the constant hard-codes the bottom layer:
/// the server announces itself correctly everywhere except there, which is the hardest kind of wrong to
/// notice because everything around it is right.</para>
///
/// <para>Two uses are legitimate and are named below. Anything else fails until somebody decides which
/// side of the line it is on, because that decision cannot be made by a regex.</para>
/// </summary>
[TestFixture]
public class GameNameReachesPlayersTests
{
    // Path fragment → why this file may name the engine directly.
    //
    // 🔴 Written with FORWARD slashes, and every path compared against them is normalized. A fragment
    // carrying one platform's separator is a guard that stops matching anywhere else: on Linux this
    // list excused nothing, so the three legitimate files became offenders and the build went red on
    // code nobody had touched. The reverse is the dangerous half — a list that excuses nothing on the
    // platform the rule is enforced on would pass while checking less than it says.
    private static readonly (string File, string Why)[] Allowed =
    [
        ("Configuration/ServerConfig.cs",
         "the resolver itself — this is where the engine becomes the last answer"),
        ("Configuration/ServerPaths.cs",
         "a FOLDER name, which must never move when a game is renamed"),
        ("Services/ConsoleCommands.cs",
         "/credits names the ENGINE and its author, not the game running on it"),
    ];

    /// <summary>A path with one separator, whatever the platform spells it with.</summary>
    private static string Slashed(string path) => path.Replace('\\', '/');

    [Test]
    public void OnlyTheResolverAndThePathsNameTheEngineDirectly()
    {
        string root = typeof(GameNameReachesPlayersTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!;
        string server = Path.Combine(root, "server", "src");
        Assert.That(Directory.Exists(server), Is.True, $"server sources not found at {server}");

        var offenders = new List<string>();
        foreach (string file in Directory.EnumerateFiles(server, "*.cs", SearchOption.AllDirectories))
        {
            string path = Slashed(file);

            if (path.Contains("/bin/") || path.Contains("/obj/")) continue;
            // The server SHELL manages installations rather than serving players: its window chrome and
            // its own settings folder are the engine's, and no line of it is addressed to a player.
            if (path.Contains("/Mirage.Server.Shell/")) continue;
            if (Allowed.Any(a => path.EndsWith(a.File, StringComparison.OrdinalIgnoreCase))) continue;

            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                // Code only: the rule is about what a line DOES, and the doc comments explaining it
                // necessarily name the thing they are explaining.
                string line = lines[i].TrimStart();
                if (line.StartsWith("//") || line.StartsWith("///") || line.StartsWith("*")) continue;
                if (!Regex.IsMatch(line, @"\bConstants\.GameName\b")) continue;

                offenders.Add($"  {Path.GetRelativePath(root, file)}:{i + 1}  {line.Trim()}");
            }
        }

        Assert.That(offenders, Is.Empty,
            "these name the ENGINE where they should ask ServerConfig.GameName what this game is called.\n"
            + "If one of them is genuinely about the engine rather than the game, add it to the allow list\n"
            + "above with the reason:\n" + string.Join("\n", offenders));
    }

    /// <summary>The allow list names files that exist. A stale entry silently stops excusing anything and
    /// starts excusing nothing, which is a guard that has quietly widened.</summary>
    [Test]
    public void EveryAllowedFileStillExists()
    {
        string root = typeof(GameNameReachesPlayersTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!;
        string server = Path.Combine(root, "server", "src");

        var all = Directory.EnumerateFiles(server, "*.cs", SearchOption.AllDirectories).ToList();
        Assert.Multiple(() =>
        {
            foreach (var (file, why) in Allowed)
            {
                Assert.That(all.Any(f => Slashed(f).EndsWith(file, StringComparison.OrdinalIgnoreCase)), Is.True,
                    $"the allow list still excuses {file} ({why}), and no such file exists");
            }
        });
    }
}

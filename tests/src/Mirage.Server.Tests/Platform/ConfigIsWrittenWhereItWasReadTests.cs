using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Mirage.Server.Tests.Platform;

/// <summary>
/// A setting written back goes to the file this server READ.
///
/// <para>🔴 It went to <c>ServerConfigStore.DefaultPath</c> instead — resolved fresh at save time, off
/// <c>AppContext.BaseDirectory</c>, with no memory of <c>--config</c>. So a server started on an
/// alternate config wrote its settings into the DEFAULT one: a scratch instance on scratch ports
/// silently replaced the real installation's ports and token. The load benchmark starts a second server
/// exactly that way, and so does any throwaway run.</para>
///
/// <para>The path is registered as <c>ServerConfigPath</c> at startup and everything that saves reads it
/// from there.</para>
/// </summary>
[TestFixture]
public class ConfigIsWrittenWhereItWasReadTests
{
    static string RepoRoot()
    {
        string root = typeof(ConfigIsWrittenWhereItWasReadTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!;
        Assert.That(Directory.Exists(root), Is.True, $"Repository root not found: {root}");
        return root;
    }

    /// <summary>Source with line comments stripped, so a mention inside a comment cannot satisfy a check
    /// about what the code does.</summary>
    static string CodeOf(params string[] parts)
    {
        string raw = File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray()));
        return string.Join("\n", raw.Split('\n')
            .Select(line => { int i = line.IndexOf("//", StringComparison.Ordinal); return i < 0 ? line : line[..i]; }));
    }

    /// <summary>Every <c>ServerConfigStore.Save</c> call in the host takes the recorded path. Naming the
    /// default there is the bug this exists for.</summary>
    [Test]
    public void NothingSavesToTheDefaultPath()
    {
        string dir = Path.Combine(RepoRoot(), "server", "src", "Mirage.Server.Host");
        var offenders = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f =>
            {
                string code = string.Join("\n", File.ReadAllText(f).Split('\n')
                    .Select(l => { int i = l.IndexOf("//", StringComparison.Ordinal); return i < 0 ? l : l[..i]; }));
                return Regex.IsMatch(code, @"ServerConfigStore\.Save\s*\(\s*ServerConfigStore\.DefaultPath");
            })
            .Select(Path.GetFileName)
            .ToList();

        Assert.That(offenders, Is.Empty,
            "these save to the DEFAULT config rather than the one the server was started with, so a "
            + "--config run would overwrite the real installation's settings: " + string.Join(", ", offenders));
    }

    /// <summary>The path is captured once at startup and registered, rather than each writer resolving
    /// its own answer.</summary>
    [Test]
    public void TheLoadedPathIsRegistered()
    {
        string program = CodeOf("server", "src", "Mirage.Server.Host", "Program.cs");

        Assert.That(program, Does.Match(@"ServerConfigStore\.Load\(\s*configPath\s*\)"),
            "Program.cs no longer loads from a named path, so there is nothing to hand a writer");
        Assert.That(program, Does.Contain("new ServerConfigPath(configPath)"),
            "the loaded path is not registered, so a writer has no way to reach it");
    }

    /// <summary>And the management command — the one that writes settings at runtime — uses it.</summary>
    [Test]
    public void TheManagementCommandSavesToTheLoadedPath()
    {
        string code = CodeOf("server", "src", "Mirage.Server.Host", "Services", "ConsoleCommands.Management.cs");

        Assert.That(code, Does.Match(@"ServerConfigStore\.Save\(\s*_configPath\.Path"),
            "/management does not save to the path the server read, so a scratch server would write its "
            + "ports and token into the real config");
    }
}

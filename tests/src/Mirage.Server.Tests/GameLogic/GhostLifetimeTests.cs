using Mirage.Server.Core.GameLogic;
using NUnit.Framework;
using System.Reflection;

namespace Mirage.Server.Tests.GameLogic;

/// <summary>
/// A body left in the world has to stop being left in the world.
///
/// <para><b>Two halves, and either one missing is silent.</b> A game asks for the body to stay and the
/// engine keeps it; the engine has to take it away again on its own, or the game must remember to come
/// back for it — which is not a primitive, it is a chore with a deadline. Neither half announces its
/// absence: a ghost nobody can create looks like a feature nobody uses, and a ghost nobody clears looks
/// like a player who never logged out.</para>
/// </summary>
[TestFixture]
public class GhostLifetimeTests
{
    private static string RepoRoot()
    {
        string root = typeof(GhostLifetimeTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!;
        Assert.That(Directory.Exists(root), Is.True, $"Repository root not found: {root}");
        return root;
    }

    private static string Source(params string[] parts) =>
        File.ReadAllText(Path.Combine([RepoRoot(), .. parts]));

    [Test]
    public void TheSweepIsDrivenByTheGameLoop()
    {
        string loop = Source("server", "src", "Mirage.Server.Core", "GameLogic", "GameLoop.cs");

        Assert.That(loop, Does.Contain($"{nameof(JoinLeaveSystem.SweepGhosts)}()"),
            "nothing takes an expired ghost out of the world, so a body a game asked to keep is kept forever");
    }

    [Test]
    public void TheSweepIsWhatClearsAGhost()
    {
        string join = Source("server", "src", "Mirage.Server.Core", "GameLogic", "JoinLeaveSystem.cs");

        Assert.That(join, Does.Contain("ClearGhost(i)"),
            "the sweep does not actually clear anything");
    }

    /// <summary>A module is handed the world, or every seam in the engine is unreachable by a game.
    ///
    /// <para>This is the same failure as an unswept ghost, one level up: the interface compiles, the
    /// implementation is registered, and nothing ever calls <c>Start</c>. There is no error — a game
    /// simply never does anything.</para></summary>
    [Test]
    public void EveryModuleIsHandedTheWorld()
    {
        string host = Source("server", "src", "Mirage.Server.Host", "Services", "MirageServerService.cs");

        Assert.Multiple(() =>
        {
            Assert.That(host, Does.Contain("foreach (var module in _registry.Modules)"),
                "nothing walks the loaded modules");
            Assert.That(host, Does.Contain("module.Start("),
                "the modules are walked and never started, so no game can act on anything");
        });
    }

    /// <summary>Whether a body stays is the game's answer, so the engine must not have one of its own.
    /// A condition here would be a rule Core does not get to have.</summary>
    [Test]
    public void NothingInCoreAsksForAGhost()
    {
        string join = Source("server", "src", "Mirage.Server.Core", "GameLogic", "JoinLeaveSystem.cs");

        Assert.That(join, Does.Contain("var linger = LingerFor(index);"),
            "the leave path no longer asks the loaded game how long the body stays");
    }
}

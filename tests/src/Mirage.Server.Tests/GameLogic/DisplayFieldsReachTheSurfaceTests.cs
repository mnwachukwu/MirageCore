using NUnit.Framework;
using System.Reflection;

namespace Mirage.Server.Tests.GameLogic;

/// <summary>
/// A declaration nobody draws is a declaration that does nothing.
///
/// <para><b>Two halves, and neither one announces its absence.</b> A game declares what its heads-up
/// display shows; the server has to send it, and a surface has to ask for it. Drop the send and every
/// client shows an empty sidebar. Drop the ask and the packet arrives, is stored, and is never read.
/// Both look exactly like a game that declared nothing — which is the ordinary case, so neither shows
/// up as a fault.</para>
///
/// <para>This is a source scan because the failure is an absence: with no module in tree there is no
/// declaration to observe going undrawn.</para>
/// </summary>
[TestFixture]
public class DisplayFieldsReachTheSurfaceTests
{
    private static string RepoRoot()
    {
        string root = typeof(DisplayFieldsReachTheSurfaceTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!;
        Assert.That(Directory.Exists(root), Is.True, $"Repository root not found: {root}");
        return root;
    }

    private static string Source(params string[] parts) => File.ReadAllText(Path.Combine([RepoRoot(), .. parts]));

    [Test]
    public void TheServerSendsTheDeclarationOnJoin()
    {
        string join = Source("server", "src", "Mirage.Server.Core", "GameLogic", "JoinLeaveSystem.cs");

        Assert.That(join, Does.Contain("PacketBuilder.DisplayFields(_world.DisplayFields)"),
            "nothing sends what a game declared, so every client draws an empty surface");
    }

    /// <summary>The compiled modules are the authority; the world is the copy every send reads.</summary>
    [Test]
    public void TheWorldIsGivenWhatTheModulesDeclared()
    {
        string service = Source("server", "src", "Mirage.Server.Host", "Services", "MirageServerService.cs");

        Assert.That(service, Does.Contain("_world.DisplayFields = _registry.DisplayFields"),
            "the world keeps an empty set, so the join send has nothing to carry");
    }

    /// <summary>🔴 The CALL, not the method. A guard that only finds the body passes while the body
    /// sits there uncalled, which is the exact shape of the failure it exists to catch.</summary>
    [Test]
    public void TheHudAsksForItsOwnSurface()
    {
        string hud = Source("client", "src", "Mirage.Client.Shell", "Panels", "HudPanel.cs");

        Assert.Multiple(() =>
        {
            Assert.That(hud, Does.Contain("DrawDisplayRows(sb, font, state,"),
                "nothing in Draw calls it, so a declaration reaches the client and is drawn by nobody");
            Assert.That(hud, Does.Contain("DisplayFields.Project(DisplaySurfaces.Hud"),
                "the HUD draws rows it got from somewhere other than the game's declaration");
        });
    }

    /// <summary>🔴 The rows stop where the buttons start. Without the bound a game that declares ten
    /// fields draws over its own Logout button, and the player cannot leave.</summary>
    [Test]
    public void TheHudStopsBeforeItsButtons()
    {
        string hud = Source("client", "src", "Mirage.Client.Shell", "Panels", "HudPanel.cs");

        Assert.That(hud, Does.Contain("y + DisplayRowH <= ButtonBaseY"),
            "nothing bounds the declared rows, so enough of them cover the buttons below");
    }
}

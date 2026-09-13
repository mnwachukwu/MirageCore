using NUnit.Framework;
using System.Reflection;

namespace Mirage.Server.Tests.GameLogic;

/// <summary>
/// A body pushed to a client has to arrive carrying what the game hung on it.
///
/// <para><b>Two halves, and the missing one is silent.</b> An attribute otherwise reaches a client only
/// when it CHANGES, so a player walking onto a map would be sent every NPC standing there and nothing
/// any of them holds. The bar over a mob's head would then be blank until somebody hit it, fill from the
/// first change onward, and look exactly like a game that meant it that way.</para>
///
/// <para>Every place that pushes a map's NPCs is therefore also a place that pushes their values, and
/// this is a source scan because the failure is an absence: there is no call to observe going wrong.</para>
/// </summary>
[TestFixture]
public class NpcValuesRideAlongTests
{
    private static string JoinLeaveSource()
    {
        string root = typeof(NpcValuesRideAlongTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!;
        Assert.That(Directory.Exists(root), Is.True, $"Repository root not found: {root}");

        return File.ReadAllText(Path.Combine(root, "server", "src", "Mirage.Server.Core",
                                             "GameLogic", "JoinLeaveSystem.cs"));
    }

    /// <summary>Three places push a map's NPCs: the region sync's centre map, each of its eight
    /// neighbours, and the refresh an editor save broadcasts. A body's values go out at all three or a
    /// player arrives to bars that are blank on one map and filled on the next.</summary>
    [Test]
    public void EverySiteThatPushesAMapsNpcs_PushesWhatTheyCarry()
    {
        string join = JoinLeaveSource();

        int bodies = CountOf(join, "BuildMapNpcs(_world,");
        int values = CountOf(join, "SendMapNpcAttributes(");

        Assert.Multiple(() =>
        {
            Assert.That(bodies, Is.EqualTo(3), "the push sites moved; check each one still sends values too");
            // One more than the sites: the method's own declaration.
            Assert.That(values, Is.EqualTo(bodies + 1),
                "a map's NPCs are pushed somewhere that does not push their values, so those bodies arrive blank");
        });
    }

    /// <summary>Both kinds of body on a map, not just the ones in its own slots — a chase in progress is
    /// exactly when a bar is worth reading, and a visitor has no slot on the map it is standing on.</summary>
    [Test]
    public void VisitorsAreIncludedAndNotJustNatives()
    {
        string join = JoinLeaveSource();
        int start = join.IndexOf("private void SendMapNpcAttributes", StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThan(0), "SendMapNpcAttributes is gone; nothing sends an NPC's values on arrival");

        string body = join[start..(start + 1400)];

        Assert.Multiple(() =>
        {
            Assert.That(body, Does.Contain("MapNpcs["), "the map's own slots");
            Assert.That(body, Does.Contain("MapTraversalNpcs["), "and whoever is visiting it mid-chase");
        });
    }

    private static int CountOf(string text, string needle)
    {
        int count = 0;
        for (int at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }
}

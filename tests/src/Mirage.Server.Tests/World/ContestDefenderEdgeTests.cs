using Mirage.Shared;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Mirage.Server.Tests.World;

/// <summary>
/// A contest has to KNOW who is defending it.
///
/// <para>🔴 <c>TerritoryContest.DefenderGuild</c> is read in three places and every one of them fails
/// QUIETLY when it is 0: the defender's per-tick bonus is never paid, a tied contest resolves to guild 0
/// instead of to them, and settlement counts them among the challengers. Nothing throws and nothing logs, so
/// a defended territory scores like an attacker all night and then falls to nobody on a draw.</para>
///
/// <para>That is a field which is only ever WRITTEN once, far from where it is read, and the whole failure
/// is the write going missing — which no unit test over the formulas can see, because the formulas were
/// always right. Hence a source scan: the reads are worthless without a write, so the write is what is
/// pinned. The consequences below spell out what it buys, against the same constants the engine uses.</para>
/// </summary>
[TestFixture]
public class ContestDefenderEdgeTests
{
    /// <summary>The repository root, baked in by the csproj — the same anchor the other source scans use,
    /// so this cannot silently skip when the suite builds to a redirected output path.</summary>
    private static string RepoRoot()
    {
        string root = typeof(ContestDefenderEdgeTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!;
        Assert.That(Directory.Exists(root), Is.True, $"Repository root not found: {root}");
        return root;
    }

    private static readonly Regex Assigns = new(@"DefenderGuild\s*=[^=]", RegexOptions.Compiled);

    [Test]
    public void DefenderGuildIsWrittenSomewhereAndNotOnlyRead()
    {
        string dir = Path.Combine(RepoRoot(), "server", "src", "Mirage.Server.Core");
        var writes = new List<string>();

        foreach (string f in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            if (f.Contains(Path.Combine("bin")) || f.Contains(Path.Combine("obj"))) continue;
            string[] lines = File.ReadAllLines(f);
            for (int i = 0; i < lines.Length; i++)
                if (Assigns.IsMatch(lines[i]))
                    writes.Add($"{Path.GetFileName(f)}:{i + 1}");
        }

        Assert.That(writes, Is.Not.Empty,
            "DefenderGuild is read when scoring, when resolving a tie, and when settling. With no "
            + "assignment anywhere it is 0 forever: the defender loses their bonus, a draw hands the "
            + "territory to nobody, and settlement treats them as a challenger.");
    }

    /// <summary>What the field buys, once it holds a real guild: a held point pays double.</summary>
    [Test]
    public void TheDefenderIsPaidMoreThanAChallenger()
    {
        const int defender = 7, challenger = 9;

        int held = TerritoryContestFormulas.ScoreDelta(defender, defender);
        int taken = TerritoryContestFormulas.ScoreDelta(challenger, defender);

        Assert.Multiple(() =>
        {
            Assert.That(held, Is.EqualTo(Constants.TerritoryOwnedScorePerTick + Constants.TerritoryDefenderScoreBonus));
            Assert.That(taken, Is.EqualTo(Constants.TerritoryOwnedScorePerTick));
            Assert.That(held, Is.GreaterThan(taken), "holding ground you own is worth more than taking it");
        });
    }

    /// <summary>And the one that cost a territory: nobody scored, so the holder keeps it.</summary>
    [Test]
    public void ANilNilContestIsKeptByTheDefender()
    {
        const int defender = 7;
        var nobodyScored = new Dictionary<int, long>();

        Assert.That(TerritoryContestFormulas.DetermineWinner(nobodyScored, defender), Is.EqualTo(defender),
            "a draw is not a loss for the guild already holding the ground");
    }

    /// <summary>The control: over UNCLAIMED land there is no defender, and a draw leaves it unclaimed rather
    /// than handing it to whoever was listed first.</summary>
    [Test]
    public void ANilNilContestOverUnclaimedLandStaysUnclaimed()
    {
        Assert.That(TerritoryContestFormulas.DetermineWinner(new Dictionary<int, long>(), 0), Is.Zero);
    }
}

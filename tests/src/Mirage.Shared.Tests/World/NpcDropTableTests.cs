using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Shared.Tests.World;

/// <summary>An NPC's drop table and the canonical form <c>Normalize</c> puts it in.
///
/// <para>The canonical form is what carries the risk: the roller reads the table directly, so an inert
/// line saved to disk is a line that looks authored and does nothing, and a table trimmed too eagerly is
/// payout deleted in silence.</para></summary>
[TestFixture]
public class NpcDropTableTests
{
    [Test]
    public void Normalize_IsIdempotent_SoLoadThenSaveSaysTheSameThing()
    {
        // It runs on load AND on every editor save, so a record that has been through it must survive
        // going through it again unchanged — a second pass that trimmed one more line would quietly cost
        // an NPC its drops over a few saves.
        var npc = new NpcRecord
        {
            Name = "Bandit",
            Drops = [new NpcDrop { ItemNum = 12, Quantity = 7, Chance = 40 },
                     new NpcDrop { ItemNum = 0, Chance = 90 }],
        };

        npc.Normalize();
        npc.Normalize();
        npc.Normalize();

        Assert.Multiple(() =>
        {
            Assert.That(npc.Drops!, Has.Count.EqualTo(1));
            Assert.That(npc.Drops![0].ItemNum, Is.EqualTo(12));
            Assert.That(npc.Drops![0].Chance, Is.EqualTo((short)40));
            Assert.That(npc.Drops![0].Quantity, Is.EqualTo((short)7));
        });
    }

    [Test]
    public void Normalize_LeavesAModernRecordAlone()
    {
        var npc = new NpcRecord
        {
            Name = "Bandit",
            Drops = [new NpcDrop { ItemNum = 1, Quantity = 10, Chance = 90 },
                     new NpcDrop { ItemNum = 40, Chance = 2 }],
        };

        npc.Normalize();

        Assert.Multiple(() =>
        {
            Assert.That(npc.Drops!, Has.Count.EqualTo(2));
            Assert.That(npc.Drops![0].ItemNum, Is.EqualTo(1));
            Assert.That(npc.Drops[1].ItemNum, Is.EqualTo(40));
        });
    }

    [Test]
    public void Normalize_DropsInertLines()
    {
        var npc = new NpcRecord
        {
            Name = "Bandit",
            Drops =
            [
                new NpcDrop { ItemNum = 1, Chance = 50 },    // live
                new NpcDrop { ItemNum = 0, Chance = 50 },    // no item — a half-authored row
                new NpcDrop { ItemNum = 9, Chance = 0 },     // can never land
            ],
        };

        npc.Normalize();

        Assert.Multiple(() =>
        {
            Assert.That(npc.Drops!, Has.Count.EqualTo(1));
            Assert.That(npc.Drops![0].ItemNum, Is.EqualTo(1));
        });
    }

    [Test]
    public void Normalize_CollapsesAnEmptyTableToNull()
    {
        // "Drops nothing" should carry no key on disk at all, matching how an unrestricted AllowedClasses
        // collapses. An empty [] and a missing key must not be two spellings of the same thing.
        var npc = new NpcRecord { Name = "Rat", Drops = [] };

        npc.Normalize();

        Assert.That(npc.Drops, Is.Null);
    }

    // THERE IS NO LENGTH CAP on a drop table, and a long one must survive Normalize intact. Quantity does
    // not stack off anything but a Currency item, so repeated lines are the only way to author a
    // multi-item payout — a cap here would silently delete a boss hoard's loot.
    [Test]
    public void Normalize_KeepsALongTable_ThereIsNoLengthCap()
    {
        const int Lines = 40;
        var npc = new NpcRecord { Name = "Hoarder", Drops = [] };
        for (int i = 1; i <= Lines; i++)
            npc.Drops.Add(new NpcDrop { ItemNum = i, Chance = 10 });

        npc.Normalize();

        Assert.That(npc.Drops!, Has.Count.EqualTo(Lines));
    }

    // A hoard is N lines of ONE, never one line of N. Normalize must not "helpfully" fold identical lines
    // together — twelve independent 2% rolls and one 2% roll for twelve gems are different payouts.
    [Test]
    public void Normalize_DoesNotCollapseRepeatedLinesOfTheSameItem()
    {
        var npc = new NpcRecord { Name = "Boss", Drops = [] };
        for (int i = 0; i < 12; i++)
            npc.Drops.Add(new NpcDrop { ItemNum = 7, Chance = 2 });

        npc.Normalize();

        Assert.That(npc.Drops!, Has.Count.EqualTo(12));
    }

    [Test]
    public void IsLive_RequiresBothAnItemAndAChance()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new NpcDrop { ItemNum = 1, Chance = 1 }.IsLive, Is.True);
            Assert.That(new NpcDrop { ItemNum = 1, Chance = 0 }.IsLive, Is.False);
            Assert.That(new NpcDrop { ItemNum = 0, Chance = 1 }.IsLive, Is.False);
            Assert.That(new NpcDrop().IsLive, Is.False);
        });
    }
}
